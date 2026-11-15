# Arquitetura da demonstração Fase 4

```mermaid
flowchart LR
  Cliente --> Gateway[Gateway]
  Gateway --> Auth[Lambda / adaptador local de autenticação]
  Gateway --> OS[OS .NET]
  OS --> OSDB[(PostgreSQL OS)]
  OS --> Filas[Filas SQS / ElasticMQ local]
  Filas <--> Billing[Billing .NET]
  Filas <--> Exec[Execução .NET]
  Billing --> Dynamo[(DynamoDB / Local)]
  Exec --> ExecDB[(PostgreSQL Execução)]
  Billing --> MP[Mercado Pago sandbox / simulado nos testes]
  OS --> Otel[OpenTelemetry]
  Billing --> Otel
  Exec --> Otel
  Otel --> Grafana[Prometheus / Grafana]
```

OS é dona do pedido e do estado do processo; Execução é dona do catálogo/estoque e da execução física; Billing é dono do orçamento e pagamento. Outbox/Inbox e chaves de idempotência sustentam a Saga sobre filas. Recusa de pagamento libera reserva quando necessário; falhas ambíguas/estorno podem exigir `ManualReview`.

Na execução local verificada, `ElasticMQ`, `DynamoDB Local`, PostgreSQL, APIs e observabilidade rodam em Docker. O adaptador local exerce o contrato de autenticação da Lambda. Os manifests K3s, Terraform AWS e pipelines representam o caminho de deploy, mas só devem ser chamados de implantados após smoke e evidência remota.
