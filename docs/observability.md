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
