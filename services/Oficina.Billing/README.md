# Oficina Billing (extração em andamento)

Serviço independente para orçamento versionado em DynamoDB. Usa valores monetários em centavos inteiros; decisões são escritas de forma condicional. Consome `WorkOrderOpened` de uma fila SQS quando `Messaging:WorkOrdersQueueUrl` é configurado. Grava orçamento, Inbox e Outbox `BudgetReady` na mesma transação DynamoDB. A rota de decisão grava `BudgetApproved` ou `BudgetRefused` junto com a alteração de status, também em uma transação. Após `StockReserved`, consome `CreatePaymentRequested` e grava tentativa + Inbox atomicamente. Publica eventos quando `Messaging:OsEventsQueueUrl` é configurado. O adaptador `MercadoPagoQrClient` prepara a criação de order QR dinâmica com chave de idempotência estável; ainda falta o worker de cobrança, webhook, reconciliação e estorno. Nenhuma cobrança é efetuada apenas pela criação da tentativa.

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

Para usar QR dinâmico será preciso Access Token de teste e `external_pos_id` de um caixa previamente criado no Mercado Pago. A resposta da order deve corresponder ao valor e à referência da tentativa. O teste do adaptador usa um handler HTTP local, sem chamar o provedor.

Rotas iniciais com `X-Internal-Token`: `POST /internal/budgets`, `GET /internal/budgets/{workOrderId}/{version}` e `POST /internal/budgets/{workOrderId}/{version}/decision`. Swagger apenas em Development: `http://localhost:8082/swagger`. DynamoDB Local usa memória e perde dados ao parar. Em AWS, a tabela será criada por Terraform e o SDK usará identidade temporária do workload; nenhuma chave AWS deve ser gravada em configuração.
