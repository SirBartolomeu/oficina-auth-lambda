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
