# Observabilidade

## Primeiro incremento: API OS

A API OS emite logs JSON no stdout, em UTC, com escopos. Cada requisição HTTP recebe `X-Trace-Id`; o mesmo `traceId` e o `requestId` aparecem no escopo dos logs, incluindo erros. O log de conclusão contém método, status, endpoint e duração em milissegundos. Requisições `/health` não geram log de conclusão para reduzir volume; erros ainda são registrados. Comandos SQL de sucesso do EF Core ficam em nível `Warning` para reduzir custo e evitar dados desnecessários nos logs.

Validação local: `docker compose -f docker-compose.yml -f compose.gateway.yml up -d --build oficina-api`, seguido de uma requisição a `https://localhost:8443/api/customers`. O retorno 401 inclui `X-Trace-Id`; `docker logs oficina-mvp-api --tail 5` mostra a linha JSON com o mesmo identificador, status 401 e duração. Em execução local, o certificado do Traefik é autogerado.

## Segundo incremento: Saga por SQS

Execução e Billing também emitem JSON no stdout em UTC. Os três consumidores SQS colocam `eventId`, `correlationId` e `workOrderId` no escopo dos logs durante o tratamento de cada mensagem. Após confirmação de remoção da fila, registram o tipo de evento processado. Em falhas, a linha de aviso inclui `MessageId` da SQS, `eventId` e `correlationId` quando o envelope pôde ser lido. O corpo da mensagem, CPF, tokens e dados de pagamento não são registrados. Os 10 testes locais da Saga passaram usando ElasticMQ e DynamoDB Local. As imagens de Execução e Billing foram reconstruídas e a saída de log de ambos foi lida como JSON válido.

`correlationId` é o identificador da Saga, não um trace W3C. Ainda faltam exportação de traces e métricas, propagação de `traceparent` por SQS, correlação dos publicadores Outbox e da Lambda, dashboards e alertas. O custo de logs na AWS dependerá de volume e retenção; configurar retenção curta e limites antes do deploy.
