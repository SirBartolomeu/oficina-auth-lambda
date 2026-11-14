# Oficina Billing (extração em andamento)

Serviço independente para orçamento versionado em DynamoDB. Usa valores monetários em centavos inteiros; decisões são escritas de forma condicional. Consome `WorkOrderOpened` de uma fila SQS quando `Messaging:WorkOrdersQueueUrl` é configurado. Grava orçamento, Inbox e Outbox `BudgetReady` na mesma transação DynamoDB. A rota de decisão grava `BudgetApproved` ou `BudgetRefused` junto com a alteração de status, também em uma transação. Após `StockReserved`, consome `CreatePaymentRequested` e grava tentativa + Inbox atomicamente. Publica eventos quando `Messaging:OsEventsQueueUrl` é configurado. Com credenciais de teste configuradas, o worker cria a order QR dinâmica usando `paymentAttemptId` como chave de idempotência e grava order/QR em transação condicional. Um webhook de order assinado aciona consulta ao provedor; pagamento processado e coerente grava `PaymentConfirmed`, enquanto order expirada/cancelada sem pagamento grava `PaymentRejected` na Outbox. A OS inicia a liberação de estoque após recusa. O adaptador de estorno total da order usa `X-Idempotency-Key`; ainda não está ligado a um comando nem a um worker, e sua resposta não confirma a devolução. Nenhuma cobrança é efetuada apenas pela criação da tentativa.

Da raiz do workspace:

```powershell
docker compose -f compose.billing.yml up -d --build
Invoke-WebRequest http://localhost:8082/health/ready
dotnet test services/Oficina.Billing.Tests/Oficina.Billing.Tests.csproj
```

Teste transacional com DynamoDB Local em memória:

```powershell
docker compose -f compose.billing.yml up -d dynamodb-local
$env:BILLING_DYNAMO_ENDPOINT='http://127.0.0.1:8000'
dotnet test services/Oficina.Billing.Dynamo.Tests/Oficina.Billing.Dynamo.Tests.csproj
```

Fluxo local OS → SQS compatível → Billing → SQS compatível → OS:

```powershell
docker compose -f compose.messaging.yml up -d elasticmq
$env:MESSAGING_SQS_ENDPOINT='http://127.0.0.1:9324'
$env:BILLING_DYNAMO_ENDPOINT='http://127.0.0.1:8000'
dotnet test tests/Oficina.Messaging.Local.Tests/Oficina.Messaging.Local.Tests.csproj
```

O teste cria filas e tabela com nomes únicos no emulador local e verifica orçamento, duplicata, `BudgetReady`, `BudgetApproved`, pedido de reserva, pedido de pagamento e status da OS. Em execução real, configure `Messaging:WorkOrdersQueueUrl` e `Messaging:OsEventsQueueUrl`; use `Messaging:SqsServiceUrl` apenas para o emulador. Sem URL de serviço, o SDK usa as credenciais temporárias do ambiente AWS.

Para ativar o worker de QR dinâmico, configure `MercadoPago__AccessToken` e `MercadoPago__ExternalPosId` com o Access Token de teste e o identificador de um caixa previamente criado. Configure também `MercadoPago__WebhookSecret` para habilitar `POST /webhooks/mercado-pago/orders` e cadastre sua URL HTTPS no painel do provedor. Não grave segredos no repositório. A resposta da order deve corresponder ao valor e à referência da tentativa. `GET /internal/payments/{paymentAttemptId}` devolve estado, identificador da order e QR após a criação. Falha HTTP mantém a tentativa pendente para nova chamada com a mesma chave; tentativa pendente há mais de 23 horas vira `ManualReview` sem chamar o provedor. O webhook compara `x-signature` com HMAC-SHA256 do identificador em `data.id`, `x-request-id` e `ts`; depois confere o identificador no corpo e consulta a order por API. Os testes usam HTTP simulado; nenhum teste atual chama o Mercado Pago.

Rotas iniciais com `X-Internal-Token`: `POST /internal/budgets`, `GET /internal/budgets/{workOrderId}/{version}`, `POST /internal/budgets/{workOrderId}/{version}/decision` e `GET /internal/payments/{paymentAttemptId}`. Swagger apenas em Development: `http://localhost:8082/swagger`. DynamoDB Local usa memória e perde dados ao parar. Em AWS, a tabela será criada por Terraform e o SDK usará identidade temporária do workload; nenhuma chave AWS deve ser gravada em configuração.
