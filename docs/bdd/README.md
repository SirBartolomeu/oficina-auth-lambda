# Evidência BDD local

Os cenários em [saga-fase4.feature](saga-fase4.feature) descrevem comportamentos observáveis. A execução automatizada atual usa xUnit, sem interpretador Gherkin; por isso o arquivo `.feature` é especificação rastreável, não um teste executável por si só.

| Cenário | Testes executáveis existentes | Situação |
|---|---|---|
| Orçamento aprovado → reserva → pedido de pagamento | `OpenedEventFlowTests.OutboxRoundTripCreatesBudgetAndMovesOrderToApproval`; `StockReservationFlowTests.ReservationResultUpdatesOrderWithoutOverselling` | Coberto em segmentos; cenário único completo pendente |
| Pagamento `expired`/`canceled` → liberação | `StockReservationFlowTests.ReservationResultUpdatesOrderWithoutOverselling` (Billing consulta order simulada, publica via ElasticMQ; OS/Execução liberam estoque e concluem) | Coberto de Billing até Execução/OS |
| Recusa após pagamento → revisão administrativa | `ExecutionRejectionTests.RejectionMovesPaidOrderToManualReviewWithoutFinalizing` | Coberto |
| Pedido → estorno confirmado → OS cancelada | `RefundFlowTests.RefundRequestCrossesQueuesAndConfirmationCancelsOrder`; `RefundOrderProcessorTests.RefundIsConfirmedOnlyByGetAndDoesNotRepeatAmbiguousPost` | Coberto com provedor simulado |
| POST ambíguo → sem repetição → reconciliação | `RefundOrderProcessorTests.RefundIsConfirmedOnlyByGetAndDoesNotRepeatAmbiguousPost`; `RefundReconciliationHttpTests.InternalRouteRequiresTokenAndConfirmsRefundWithGetOnly` | Coberto com provedor simulado |

Execute os testes com ElasticMQ e DynamoDB Local:

```powershell
$env:MESSAGING_SQS_ENDPOINT='http://localhost:9324'
$env:BILLING_DYNAMO_ENDPOINT='http://localhost:8000'
.\.dotnet_home\sdk10\dotnet.exe test tests/Oficina.Messaging.Local.Tests --no-restore
.\.dotnet_home\sdk10\dotnet.exe test services/Oficina.Billing.Dynamo.Tests --no-restore
.\.dotnet_home\sdk10\dotnet.exe test tests/OficinaMvp.Integration.Tests --no-restore
```

Antes de marcar BDD como completo, unir abertura/aprovação/reserva/pagamento/execução em um cenário único, validar duplicatas e falhas pelas filas e gerar evidência de execução no CI. Testes com Mercado Pago real dependem de credenciais de testes.
