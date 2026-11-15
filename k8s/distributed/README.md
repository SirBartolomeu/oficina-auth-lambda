# Três serviços no K3s

Este Kustomize prepara OS, Execução e Billing como Deployments independentes no namespace `oficina`, cada um com Service privado, ConfigMap, ServiceAccount, healthchecks e HPA de 1–2 réplicas por CPU. Substitui o Kustomize legado da raiz `k8s/`; não aplicar os dois conjuntos sobre a mesma implantação. As imagens `:local` são marcadores até o pipeline publicar imagens no ECR.

## Dependências antes do deploy

1. Criar no RDS dois databases/usuários separados (`oficina_os` e `oficina_execution`), a tabela DynamoDB `oficina_billing` e as três filas SQS com DLQ. OS e Execução executam migrations ao iniciar em `Production`; Billing exige tabela já criada. Não há banco dentro deste overlay.
2. Substituir os `REPLACE_WITH_...` em `os.env`, `execution.env` e `billing.env` pelos URLs SQS reais, sem adicionar credenciais AWS ao Git. Ajustar região e nome da tabela de acordo com o Terraform.
3. Criar os Secrets `oficina-os-secret`, `oficina-execution-secret` e `oficina-billing-secret` fora do Git, com as chaves de `secret.example.yaml`. A OS e a Execução usam a mesma credencial interna de Execução; a Lambda também precisa do token de consulta da OS. Tokens Mercado Pago devem ser do ambiente de testes.
4. Na EC2 K3s, após criar o namespace, executar `scripts/aws-demo/prepare-workload-credentials.sh` com a região e os três ARNs do output `workload_role_arns`. O script cria um Secret AWS temporário por serviço (até 1h) e um Secret de pull ECR (até 12h). Executar pouco antes do deploy e renovar/reiniciar os pods se expirar. O SDK nos pods ignora IMDS; o nó exige IMDSv2 com hop limit 1. Para uso contínuo, trocar a renovação manual por identidade de workload automatizada.
5. Publicar as três imagens no ECR por Actions, atualizar os manifests com URIs e SHAs reais e aplicar `k8s/monitoring` na janela da demonstração para habilitar o endpoint OTLP configurado nos três serviços.

```powershell
./scripts/verify-distributed-manifests.ps1
kubectl apply -k k8s/distributed
kubectl -n oficina rollout status deployment/oficina-api --timeout=300s
kubectl -n oficina rollout status deployment/execution-api --timeout=300s
kubectl -n oficina rollout status deployment/billing-api --timeout=300s
kubectl -n oficina get hpa
```

O HPA depende do metrics-server do K3s e não cria nós: se faltar capacidade na EC2, uma segunda réplica ficará pendente. OS e Execução têm startup probe de até cinco minutos para migrations; a readiness consulta seus bancos. Billing consulta a tabela DynamoDB. As três APIs reservam juntas 300 millicores/384 MiB e têm limites somados de 1500 millicores/1536 MiB por réplica inicial. A telemetria usa o Collector no Service `otel-collector:4317`.

`ingress.example.yaml` é apenas referência para publicar `/api` da OS e o webhook de Orders do Mercado Pago; não expõe `/internal`. Exige host real, certificado TLS e gateway/rate limit definidos no Terraform antes de aplicar. A autenticação serverless por CPF é uma Lambda separada. Nenhum `kubectl apply` foi executado nesta etapa: faltam AWS, Secrets e identidade de workload. [Referência IMDS da AWS](https://docs.aws.amazon.com/sdkref/latest/guide/feature-imds-credentials.html).
