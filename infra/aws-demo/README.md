# Terraform AWS da demonstração — em construção

O módulo define três ECR, três filas SQS com DLQ, uma tabela DynamoDB `PAY_PER_REQUEST`, VPC, subnet pública para um nó K3s, duas subnets privadas em zonas distintas e RDS PostgreSQL Single-AZ. O RDS só aceita PostgreSQL do security group K3s e usa senha mestre gerenciada pelo RDS/Secrets Manager. Não há NAT Gateway. A EC2 Amazon Linux 2023 instala K3s na primeira inicialização e pode ser acessada pelo Systems Manager; o security group não tem regra de entrada pública ou SSH. O IMDSv2 é obrigatório com hop limit 1 para não expor a role do host aos pods.

O módulo também cria o provedor OIDC do GitHub e três roles distintas, cada uma vinculada a `main` de um repositório e autorizada a publicar somente no ECR do próprio serviço. Os workflows de CI usam essas roles para enviar a imagem após testes/build em `main`, quando as variáveis GitHub estiverem configuradas. A role do host K3s continua limitada ao Systems Manager. Se o provedor OIDC GitHub já existir na conta, ele precisa ser importado ao estado Terraform antes do `apply`.

Ainda faltam criação dos databases e usuários `oficina_os`/`oficina_execution` dentro do RDS, IAM por workload, ECR pull no K3s, segredos por serviço, estado remoto e pipelines de **deploy**. O script de bootstrap ainda não foi executado em EC2 real. **Não executar `apply`** nesta versão parcial; ela não sobe a aplicação. Nenhum recurso AWS foi criado.

```powershell
terraform -chdir=infra/aws-demo fmt -check
terraform -chdir=infra/aws-demo init -backend=false
terraform -chdir=infra/aws-demo validate
terraform -chdir=infra/aws-demo plan -var='aws_region=REGIAO_ESCOLHIDA' -var='github_owner=SEU_OWNER'
```

O `plan` consulta a conta AWS configurada e só deve ser executado após a escolha da conta/região e nomes finais dos repositórios. Confirmar se `db.t4g.micro` e `gp3` estão disponíveis e o preço real da região escolhida. Não guardar credenciais ou `tfstate` no Git. O estado remoto e o plano completo serão definidos antes de pedir autorização para `apply`. Os nomes/URLs de saída alimentarão os manifests K3s. A política ECR retém cinco imagens por serviço para limitar armazenamento, mas não apaga os repositórios durante um `destroy` se ainda houver imagens. O RDS foi configurado para pular snapshot final em um eventual `destroy` autorizado; produzir backup se for necessário conservar dados antes desse ato.
