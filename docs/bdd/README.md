# Evidência BDD local

Os cenários em [saga-fase4.feature](saga-fase4.feature) descrevem comportamentos observáveis. A execução automatizada atual usa xUnit, sem interpretador Gherkin; por isso o arquivo `.feature` é especificação rastreável, não um teste executável por si só.

| Cenário | Testes executáveis existentes | Situação |
|---|---|---|
| Abertura → aprovação → reserva → pagamento → execução | `OpenedEventFlowTests.OpenedOrderMovesThroughApprovalPaymentAndExecution` (OS/Billing/Execução por ElasticMQ; consulta de pagamento simulada) | Coberto em um cenário local |
| Pagamento `expired`/`canceled` → liberação | `StockReservationFlowTests.ReservationResultUpdatesOrderWithoutOverselling` (Billing consulta order simulada, publica via ElasticMQ; OS/Execução liberam estoque e concluem) | Coberto de Billing até Execução/OS |
| Recusa após pagamento → revisão administrativa | `ExecutionRejectionTests.RejectionMovesPaidOrderToManualReviewWithoutFinalizing` | Coberto |
| Pedido → estorno confirmado → OS cancelada | `RefundFlowTests.RefundRequestCrossesQueuesAndConfirmationCancelsOrder`; `RefundOrderProcessorTests.RefundIsConfirmedOnlyByGetAndDoesNotRepeatAmbiguousPost` | Coberto com provedor simulado |
| POST ambíguo → sem repetição → reconciliação | `RefundOrderProcessorTests.RefundIsConfirmedOnlyByGetAndDoesNotRepeatAmbiguousPost`; `RefundReconciliationHttpTests.InternalRouteRequiresTokenAndConfirmsRefundWithGetOnly` | Coberto com provedor simulado |
| Falha de envio da Outbox → retry | `OpenedEventFlowTests.OpenedOrderMovesThroughApprovalPaymentAndExecution` (falha uma vez, verifica pendência/tentativas, reenvia) | Coberto localmente |
| Reentrega de `PaymentConfirmed` → sem novo consumo | `OpenedEventFlowTests.OpenedOrderMovesThroughApprovalPaymentAndExecution` (reenvia mesmo `eventId` por fila isolada, Inbox rejeita) | Coberto localmente |
| Mensagem inválida → DLQ | `DeadLetterFlowTests.InvalidMessageMovesToDeadLetterQueue` (OS, Billing e Execução) | Coberto em ElasticMQ; AWS pendente |
| Dois publicadores OS → uma seleção da Outbox | `OutboxConcurrencyTests.TwoDispatchersDoNotPublishTheSamePendingMessage` | Coberto em PostgreSQL local; outras Outboxes pendentes |

Execute os testes com ElasticMQ e DynamoDB Local:

```powershell
$env:MESSAGING_SQS_ENDPOINT='http://localhost:9324'
$env:BILLING_DYNAMO_ENDPOINT='http://localhost:8000'
.\.dotnet_home\sdk10\dotnet.exe test tests/Oficina.Messaging.Local.Tests --no-restore
.\.dotnet_home\sdk10\dotnet.exe test services/Oficina.Billing.Dynamo.Tests --no-restore
.\.dotnet_home\sdk10\dotnet.exe test tests/OficinaMvp.Integration.Tests --no-restore
docker compose -f compose.execution.yml up -d execution-db
docker compose -f compose.execution.yml exec -T execution-db psql -U execution -d oficina_execution -c 'CREATE SCHEMA IF NOT EXISTS os_outbox_retry_validation_20261114'
$env:OS_POSTGRES_CONNECTION_STRING='Host=localhost;Port=5433;Database=oficina_execution;Username=execution;Password=execution-local-password;Search Path=os_outbox_retry_validation_20261114'
.\.dotnet_home\sdk10\dotnet.exe test tests/OficinaMvp.Postgres.Tests --no-restore
```

O teste PostgreSQL aplica as migrations no schema isolado antes de disputar a linha da Outbox. O workflow [phase4-local.yml](../../.github/workflows/phase4-local.yml) prepara ElasticMQ, DynamoDB Local e PostgreSQL em um runner hospedado pelo GitHub, executa Billing/Execução/Saga/concorrência e publica arquivos TRX. O Compose combinado e o teste passaram localmente, mas ainda não há execução remota nem relatório de CI.

Antes de marcar BDD como completo, ampliar duplicatas e falhas para os demais caminhos e gerar evidência de execução no CI. O arquivo `.feature` ainda precisa de um runner Gherkin se a avaliação exigir execução direta da especificação. Testes com Mercado Pago real dependem de credenciais de testes.
