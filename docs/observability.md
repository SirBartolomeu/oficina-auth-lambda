# Observabilidade

## Primeiro incremento: API OS

A API OS emite logs JSON no stdout, em UTC, com escopos. Cada requisição HTTP recebe `X-Trace-Id`; o mesmo `traceId` e o `requestId` aparecem no escopo dos logs, incluindo erros. O log de conclusão contém método, status, endpoint e duração em milissegundos. Requisições `/health` não geram log de conclusão para reduzir volume; erros ainda são registrados. Comandos SQL de sucesso do EF Core ficam em nível `Warning` para reduzir custo e evitar dados desnecessários nos logs.

Validação local: `docker compose -f docker-compose.yml -f compose.gateway.yml up -d --build oficina-api`, seguido de uma requisição a `https://localhost:8443/api/customers`. O retorno 401 inclui `X-Trace-Id`; `docker logs oficina-mvp-api --tail 5` mostra a linha JSON com o mesmo identificador, status 401 e duração. Em execução local, o certificado do Traefik é autogerado.

## Segundo incremento: Saga por SQS

Execução e Billing também emitem JSON no stdout em UTC. Os três consumidores SQS colocam `eventId`, `correlationId` e `workOrderId` no escopo dos logs durante o tratamento de cada mensagem. Após confirmação de remoção da fila, registram o tipo de evento processado. Em falhas, a linha de aviso inclui `MessageId` da SQS, `eventId` e `correlationId` quando o envelope pôde ser lido. O corpo da mensagem, CPF, tokens e dados de pagamento não são registrados. Os 10 testes locais da Saga passaram usando ElasticMQ e DynamoDB Local. As imagens de Execução e Billing foram reconstruídas e a saída de log de ambos foi lida como JSON válido.

`correlationId` é o identificador da Saga, não um trace W3C.

## Terceiro incremento: contexto W3C na SQS

Os publicadores OS, Execução e Billing criam uma atividade W3C e enviam seu `traceparent` no atributo da mensagem SQS. Os três consumidores solicitam esse atributo, validam o formato e retomam uma atividade filha antes de executar o handler. Se o atributo estiver ausente ou inválido, a mensagem continua sendo processada com uma nova atividade. Um teste com ElasticMQ confirmou que o atributo recebido contém o mesmo trace ID da atividade emissora; 11 testes locais da Saga passaram.

## Quarto incremento: contexto durável na Outbox da OS

A OS grava `traceparent` como metadado opcional no JSON da Outbox, na mesma transação da OS. O dispatcher retoma esse contexto antes de publicar na SQS. Um teste salvou a Outbox em SQLite, encerrou a atividade da requisição, releu o registro e confirmou no ElasticMQ o mesmo trace ID; os 12 testes locais de mensageria e os 23 testes de integração da OS passaram. O campo extra não altera os campos obrigatórios do contrato de evento.

## Quinto incremento: Outboxes da Execução e Billing

Execução grava o metadado `traceparent` no corpo da Outbox SQL; Billing o grava no corpo da Outbox DynamoDB dentro da transação de estado + mensagem. Os publicadores retomam esse contexto ao enviar pela SQS. Testes de persistência passaram nos dois bancos locais (11 testes Execução, 13 DynamoDB Billing); os 12 testes da Saga passaram. Um teste do fluxo OS→Billing confirmou que a Outbox do Billing manteve o trace ID iniciado na OS.

O contexto W3C agora atravessa as três Outboxes e a SQS, mas ainda faltam um exportador de spans/métricas, amostragem, correlação da Lambda, dashboards e alertas para demonstrar um trace distribuído. As atividades atuais usam `System.Diagnostics.Activity` sem coletor. O custo de logs na AWS dependerá de volume e retenção; configurar retenção curta e limites antes do deploy.

## Sexto incremento: exportação OTLP da OS

A OS ativa OpenTelemetry apenas quando `Observability:OtlpEndpoint` está configurado. Coleta spans de ASP.NET Core e HttpClient, métricas HTTP e exporta por OTLP/gRPC. `Observability:TraceSampleRatio` aceita 0–1 (padrão 0,1) e `Observability:MetricExportIntervalSeconds` aceita 5–300 (padrão 60). Healthchecks não geram spans. Em produção, apontar o endpoint para um Collector autorizado e manter amostragem/retensão compatíveis com o orçamento.

Para verificar localmente: `docker compose -f docker-compose.yml -f compose.gateway.yml -f compose.telemetry.yml up -d --build otel-collector oficina-api gateway`. O overlay inicia um Collector sem porta publicada no host, com exportador `debug`; envia amostra de 100% e métricas a cada 10 segundos. Três requisições a `https://localhost:8443/api/customers` retornaram 401; `docker logs oficina-mvp-otel-collector-1` confirmou 3 spans e 13 métricas. Os 23 testes de integração da OS passaram. O Collector local imprime dados de telemetria somente para validação; não usar esse exportador em produção.

Fixamos os pacotes OpenTelemetry em 1.20.1: 1.21.0 causou conflito de `System.Diagnostics.DiagnosticSource` 11 com o runtime .NET 10 deste projeto. Fontes: [guia oficial de traces](https://opentelemetry.io/docs/languages/dotnet/traces/getting-started-aspnetcore/), [métricas](https://opentelemetry.io/docs/languages/dotnet/metrics/getting-started-aspnetcore/), [instalação do Collector](https://opentelemetry.io/docs/collector/install/docker/) e [configuração](https://opentelemetry.io/docs/collector/configuration/).

## Sétimo incremento: exportação OTLP da Execução e Billing

Execução e Billing usam a mesma configuração opt-in da OS, com os nomes de recurso `oficina-execution` e `oficina-billing`. O overlay `compose.telemetry.yml` aponta os três serviços ao Collector local, com 100% de amostragem apenas para demonstração. Sem `Observability:OtlpEndpoint`, não há exportação OTLP. A criação automática da tabela Billing em Development também tolera `ResourceInUseException` quando outra instância a criou entre a listagem e a criação.

Validação local: `docker compose -f docker-compose.yml -f compose.execution.yml -f compose.billing.yml -f compose.telemetry.yml up -d --build execution-api billing-api otel-collector`. Requisições sem token a `/internal/catalog/services` na porta 8081 e a `/internal/budgets/{workOrderId}/{version}` na porta 8082 retornaram 401. O Collector registrou um span de cada serviço e exportações periódicas de métricas HTTP; 11 testes da Execução e 13 testes DynamoDB Billing passaram.

## Oitavo incremento: spans SQS da Saga

Os publicadores e consumidores dos três serviços agora iniciam atividades de `ActivitySource` com `ActivityKind.Producer` ou `Consumer` e o parent W3C já gravado na Outbox ou recebido no atributo SQS. Cada serviço registra sua fonte no OpenTelemetry. Sem exportador configurado, a criação manual de atividades continua preservando `traceparent` para a próxima mensagem. Os spans registram sistema, operação e tipo de evento; não incluem CPF, token, corpo ou dados de pagamento.

Um teste com `ActivityListener` confirmou que a publicação gera um span filho exportável com o mesmo trace ID; os três testes focados de trace SQS, 11 testes de Execução, 13 testes DynamoDB Billing e 23 testes de integração da OS passaram. Ainda falta executar uma Saga completa com os três serviços apontando ao Collector e confirmar o trace distribuído nele.

Próximos itens: métricas de negócio, correlação da Lambda, dashboards e alertas.

## Nono incremento: verificação OTLP da Saga local

O teste `OpenedOrderMovesThroughApprovalPaymentAndExecution` agora captura spans SQS de OS, Billing e Execução e exige o mesmo trace ID para as três fontes. Opcionalmente, `OTEL_TEST_ENDPOINT` ativa um exportador OTLP somente no processo do teste. Para reproduzir: iniciar o Collector com `docker compose -f docker-compose.yml -f compose.execution.yml -f compose.billing.yml -f compose.telemetry.yml -f compose.telemetry.test.yml up -d otel-collector`; configurar `MESSAGING_SQS_ENDPOINT=http://localhost:9324`, `BILLING_DYNAMO_ENDPOINT=http://localhost:8000` e `OTEL_TEST_ENDPOINT=http://localhost:4317`; executar `dotnet test tests/Oficina.Messaging.Local.Tests/Oficina.Messaging.Local.Tests.csproj`. O overlay de teste expõe OTLP somente em `127.0.0.1` e usa exportador `debug` detalhado para inspecionar os IDs. Voltar ao overlay normal após a verificação.

Na execução local, os 13 testes passaram. O Collector registrou spans das três fontes sob `oficina-saga-local-test`, incluindo o trace ID `ee6744894900a665e7354ea9ae06b94e` em cada uma. Essa evidência valida a cadeia de contexto e a exportação, mas os três componentes foram hospedados pelo mesmo processo de teste; ainda falta um trace real entre os três contêineres independentes e sua visualização em dashboard.

## Décimo incremento: trace entre três contêineres

`compose.saga-local.yml` conecta OS, Execução e Billing ao ElasticMQ com filas locais e usa bancos PostgreSQL de demonstração separados dos bancos anteriores. `scripts/verify-saga-containers.ps1` cria esses bancos e as filas caso ainda não existam, sobe os serviços com o Collector de teste, abre uma OS usando a cotação HTTP da Execução e aguarda `AwaitingApproval` após `WorkOrderOpened` e `BudgetReady` passarem pelo Billing. O roteiro compara o `X-Trace-Id` da criação com os spans recebidos pelo Collector sob os recursos `oficina-os`, `oficina-execution` e `oficina-billing`. Executar na raiz do repositório com `./scripts/verify-saga-containers.ps1`; `-SkipBuild` usa as imagens locais existentes. O teste usa apenas Docker Desktop e dados locais.

Validação: OS `20b88abd-7c65-47e1-b846-79a5256dcf1d` chegou a `AwaitingApproval`, orçamento R$ 125,00; o Collector encontrou o trace `d223b18053e4ff8b1b3b90a26d9cf25f` nos três contêineres. Os 23 testes de integração da OS passaram. A primeira execução revelou um registro incorreto do cliente HTTP tipado na OS, corrigido, além de filas ausentes e um banco antigo sem a tabela Outbox; o roteiro cria filas e usa bancos isolados sem alterar os anteriores.

O overlay `compose.telemetry.test.yml` imprime detalhes de trace e publica OTLP apenas em `127.0.0.1`. Após a verificação, restaurar o Collector normal com `docker compose -f docker-compose.yml -f compose.execution.yml -f compose.billing.yml -f compose.telemetry.yml up -d otel-collector`. Faltam dashboard, alertas, métricas de negócio e verificação da mesma cadeia em Kubernetes/AWS.

## Décimo primeiro incremento: contadores de negócio da OS

O meter `Oficina.WorkOrders` emite `work_orders_opened` após a persistência de uma OS e `work_order_saga_transitions` somente quando o handler da OS aplica um evento novo. A segunda métrica usa apenas a etiqueta `event.type`; não usa CPF, ID da OS ou `correlationId` como dimensão. O exporter continua opt-in via `Observability:OtlpEndpoint`. Métricas são acumuladas em memória e podem perder incrementos em caso de queda entre commit e emissão; para totais auditáveis, consultar o banco de dados.

Validação: 23 testes de integração OS e 13 testes locais da Saga passaram. `scripts/verify-saga-containers.ps1 -SkipBuild` abriu a OS `05f25255-2c42-4499-ab66-e1f01fc802b6`, que chegou a `AwaitingApproval`; o Collector detalhado registrou `work_orders_opened=1` e `work_order_saga_transitions{event.type=BudgetReady}=1`. A configuração de teste do Collector passou a aceitar métricas além de traces. Ainda faltam métricas próprias de Execução/Billing, indicadores de tempo e falha, dashboards e alertas.

## Décimo segundo incremento: contadores de Execução e Billing

Execução emite `execution_commands_applied` apenas quando um comando SQS novo foi persistido, com etiqueta `event.type`. Billing emite `billing_budgets_created` após criação efetiva, `billing_budgets_decided` após decisão nova com etiqueta `decision` (`approved`/`refused`) e `billing_commands_accepted` para novos pedidos de pagamento ou estorno. Reentregas idempotentes não incrementam esses contadores. Os dois meters são exportados somente quando OTLP estiver configurado. Como na OS, esses contadores são observacionais e não substituem consultas auditáveis aos bancos.

Validação local: 11 testes de Execução, 13 testes DynamoDB Billing e 13 testes da Saga passaram. A OS `ba39b7ec-20f7-435b-aacb-8d83cb6b9298` chegou a `AwaitingPayment` após aprovação do orçamento. O Collector detalhado recebeu os quatro contadores, incluindo `decision=approved`, `event.type=ReserveStockRequested` e `event.type=CreatePaymentRequested`. O fluxo parou antes da cobrança porque o provedor Mercado Pago não está configurado no ambiente local. Faltam histogramas de duração, indicadores de falha, dashboards e alertas.

## Décimo terceiro incremento: duração e falhas SQS

Os três consumidores agora emitem `sqs_message_processing_duration` em segundos após processar e remover uma mensagem, com apenas `event.type` como etiqueta. `sqs_message_failures` conta tentativas de processamento malsucedidas, inclusive reentregas; `sqs_receive_failures` conta falhas ao consultar a fila. Os contadores de falha não levam IDs nem tipos arbitrários enviados por mensagens inválidas. O histograma mede o tempo dentro do worker, incluindo a confirmação de remoção na SQS, sem incluir o tempo em que a mensagem aguardou na fila.

Validação: 23 testes OS, 11 Execução, 13 DynamoDB Billing e 13 Saga passaram. Os três testes de DLQ confirmaram pelo `MeterListener` ao menos duas falhas de processamento por consumidor. A OS `2067b365-9dbe-41a6-bdd9-c9cf63ee1920` chegou a `AwaitingPayment`; o Collector detalhado recebeu `sqs_message_processing_duration` sob `oficina-os`, `oficina-execution` e `oficina-billing`. Ainda faltam dashboards, alertas e medição da espera na fila e do tempo em cada estado da OS.
