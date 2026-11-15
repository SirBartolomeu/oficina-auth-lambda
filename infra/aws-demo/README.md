# Terraform AWS da demonstração — em construção

Este primeiro incremento define três ECR, três filas SQS com DLQ e uma tabela DynamoDB `PAY_PER_REQUEST`. Ainda faltam EC2/K3s, RDS, IAM/OIDC, segredos, estado remoto e pipelines de deploy. **Não executar `apply`** nesta versão parcial; ela não sobe a aplicação. Nenhum recurso AWS foi criado.

```powershell
terraform -chdir=infra/aws-demo fmt -check
terraform -chdir=infra/aws-demo init -backend=false
terraform -chdir=infra/aws-demo validate
terraform -chdir=infra/aws-demo plan -var='aws_region=us-east-1'
```

O `plan` consulta a conta AWS configurada e só deve ser executado após a escolha da conta/região. Não guardar credenciais ou `tfstate` no Git. O estado remoto e o plano completo serão definidos antes de pedir autorização para `apply`. Os nomes/URLs de saída alimentarão os manifests K3s. A política ECR retém cinco imagens por serviço para limitar armazenamento, mas não apaga os repositórios durante um `destroy` se ainda houver imagens.
