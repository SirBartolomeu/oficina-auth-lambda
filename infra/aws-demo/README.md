# Terraform AWS da demonstração — em construção

O módulo define três ECR, três filas SQS com DLQ, uma tabela DynamoDB `PAY_PER_REQUEST`, VPC, subnet pública para um nó K3s, duas subnets privadas em zonas distintas e RDS PostgreSQL Single-AZ. O RDS só aceita PostgreSQL do security group K3s e usa senha mestre gerenciada pelo RDS/Secrets Manager. Não há NAT Gateway. A EC2 Amazon Linux 2023 instala K3s na primeira inicialização e pode ser acessada pelo Systems Manager; o security group não tem regra de entrada pública ou SSH. O IMDSv2 é obrigatório com hop limit 1 para não expor a role do host aos pods.

O módulo também cria o provedor OIDC do GitHub e três roles distintas, cada uma vinculada a `main` de um repositório e autorizada a publicar somente no ECR do próprio serviço. Os workflows de CI usam essas roles para enviar a imagem após testes/build em `main`, quando as variáveis GitHub estiverem configuradas. A role do host K3s tem Systems Manager, leitura das três imagens ECR e permissão para assumir três roles de workload. OS, Execução e Billing têm permissões separadas por fila; somente Billing acessa sua tabela DynamoDB. Se o provedor OIDC GitHub já existir na conta, ele precisa ser importado ao estado Terraform antes do `apply`.

Na janela da demo, `scripts/aws-demo/prepare-workload-credentials.sh` obtém credenciais STS de até uma hora por serviço e cria Secrets Kubernetes separados; ele também cria a credencial temporária de pull ECR. Os pods não acessam IMDS e leem apenas o Secret de sua role. O K3s habilita criptografia de Secrets em repouso. Rodar o script pouco antes da apresentação, reiniciar pods ao renovar credenciais e remover os Secrets ao encerrar. É uma solução de demonstração de um nó; para operação contínua, substituir a renovação manual por identidade de workload automatizada.

O Terraform também cria três entradas vazias no Secrets Manager. `scripts/aws-demo/bootstrap-rds-and-app-secrets.sh` cria os databases e usuários `oficina_os`/`oficina_execution`, grava senhas e tokens fora do Terraform state e prepara os Secrets Kubernetes de aplicação. O nó K3s pode ler o segredo mestre gerenciado pelo RDS e gravar apenas essas três entradas; a role do Billing continua sem acesso a segredos. A criação dos bancos e a distribuição de credenciais ainda não foram executadas em EC2 real.

Três documentos SSM de deploy recebem somente um SHA Git válido, atualizam a imagem do Deployment correspondente e verificam o rollout. Cada role OIDC pode executar apenas seu documento no nó K3s e consultar o resultado. Os workflows de CI chamam esses documentos após publicar ECR. Para o primeiro deploy, um bucket S3 privado recebe um pacote curto de scripts/manifests (objetos expiram em dois dias); a role do nó pode somente ler `demo/*`. `scripts/aws-demo/package-deployment.ps1` gera o pacote a partir dos outputs do Terraform. Após o upload e conferência do SHA256, `initial-deploy.sh` prepara RDS/Secrets e injeta região, tabela, URLs SQS e imagens ECR publicadas no Kustomize antes de aplicar.

Ainda faltam estado remoto, upload/execução real do pacote e smoke HTTP de API; scripts e K3s não foram testados em EC2. **Não executar `apply`** antes do plan revisado e da autorização; nenhum recurso AWS foi criado.

```powershell
terraform -chdir=infra/aws-demo fmt -check
terraform -chdir=infra/aws-demo init -backend=false
terraform -chdir=infra/aws-demo validate
terraform -chdir=infra/aws-demo plan -var='aws_region=REGIAO_ESCOLHIDA' -var='github_owner=SEU_OWNER'
```

O `plan` consulta a conta AWS configurada e só deve ser executado após a escolha da conta/região e nomes finais dos repositórios. Confirmar se `db.t4g.micro` e `gp3` estão disponíveis e o preço real da região escolhida. Não guardar credenciais ou `tfstate` no Git. O estado remoto e o plano completo serão definidos antes de pedir autorização para `apply`. Os nomes/URLs de saída alimentarão os manifests K3s. A política ECR retém cinco imagens por serviço para limitar armazenamento, mas não apaga os repositórios durante um `destroy` se ainda houver imagens. O RDS foi configurado para pular snapshot final e os três segredos para remoção sem recuperação em um eventual `destroy` autorizado; produzir backup se for necessário conservar dados antes desse ato.
