# Preparação manual para a apresentação GitHub/AWS

Execute na ordem. O assistente prepara Terraform, workflows e extração dos repositórios; os passos abaixo dependem da sua conta ou de autorização explícita. Não envie senhas, tokens ou chaves pelo chat e não os comite.

## 1. Conta, região e orçamento

1. Entre na conta AWS que usará na apresentação e escolha **uma região** para todos os recursos. Anote somente o ID da conta (12 dígitos) e a região, sem chaves.
2. Em **Billing and Cost Management → Budgets → Create budget**, crie um orçamento mensal pequeno, com alerta por e-mail para o valor que você aceita gastar. Budget **avisa, não impede gastos**; confirme o e-mail e monitore o painel. [Passos oficiais AWS](https://docs.aws.amazon.com/cost-management/latest/userguide/bcm-lite-create-a-budget.html).
3. Confirme que você tem permissão temporária para criar IAM OIDC, EC2, ECR, RDS, DynamoDB, SQS, VPC, Secrets Manager e orçamento. Se for conta de organização, peça ao administrador essas permissões. O `terraform plan` apontará a lista concreta.
4. Informe ao assistente **região, ID da conta, teto de gasto e janela da apresentação**. Ele preparará e validará o plan. Autorize `terraform apply` só depois de revisar recursos e custo. Uma hora de apresentação não inclui o tempo de criação/migração do RDS.

## 2. Repositórios e GitHub Actions

1. Escolha owner e nomes para `oficina-os`, `oficina-execution` e `oficina-billing`. Informe se serão privados. Autorize expressamente a publicação; a extração será preparada preservando o histórico Git.
2. Após a publicação, em cada repositório abra **Actions** e rode manualmente o workflow CI do serviço (`os-ci`, `execution-ci` ou `billing-ci`). Os workflows atuais usam os caminhos do monorepo; o assistente os ajustará durante a extração. Salve URL e captura de cada run verde. O workflow `phase4-local` valida a Saga com emuladores. O `deploy-kind` demonstra Kubernetes no runner hospedado, caso a AWS não esteja pronta.
3. Em **Settings → Rules → Rulesets**, crie regra para `main`: exigir pull request, checks CI verdes e bloquear force push. O nome do job dos três workflows é `test-and-image`; exija o check correspondente no repositório certo depois que ele aparecer pela primeira vez. Confirme a regra com um PR de teste. [Regras oficiais GitHub](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets).
4. O Terraform preparará a role OIDC restrita ao owner/repo/branch ou environment. Depois do plan/aplicação autorizada, configure `AWS_REGION` e `AWS_ROLE_ARN` como **Actions variables** (ou environment variables) em cada repositório. Verifique o subject/branch na trust policy antes de liberar deploy. [Orientação oficial AWS sobre GitHub OIDC](https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles_create_for-idp_oidc.html). Não criar `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY` no GitHub.

## 3. Credenciais externas e deploy

1. Se houver tempo e conta Mercado Pago de testes, obtenha Access Token e segredo de webhook **de testes** e configure-os no AWS Secrets Manager apenas após a infraestrutura existir. O callback precisa de URL HTTPS pública confiável. Sem isso, apresentar o pagamento simulado e dizer claramente que a transação externa não foi testada.
2. Revise o `terraform plan` e autorize explicitamente o `apply`; depois valide no console: EC2/K3s, RDS, tabela DynamoDB, três filas SQS com DLQ e três repositórios ECR. A existência dos recursos não prova que os pods estão prontos: confirme `kubectl -n oficina get pods,svc,hpa`, readiness dos três serviços e smoke do fluxo.
3. Rode os workflows de publicação/deploy quando preparados; guarde URL do run, SHA da imagem em cada ECR, `kubectl`/healthchecks e um trace/OS real. A demonstração local em `docs/evidence/demo-local.json` é evidência complementar.

## 4. Apresentação e encerramento

Prepare abas do GitHub Actions, console AWS e terminal K3s antes de iniciar os 15 minutos. Use `docs/demo-15-min.md` como roteiro. Após a gravação, autorize o procedimento de desligamento ou `terraform destroy` com a lista de recursos. Confira RDS, volumes EBS, IPv4, snapshots, ECR e logs residuais, e consulte Cost Explorer no dia seguinte. Parar a EC2 sozinha não zera a conta.

## Ponto de decisão

Se conta, permissões, autorização de publicação e `apply` não estiverem disponíveis no início do segundo dia, use o deploy `kind` no GitHub Actions e o demo Docker como prova executada. Mostre Terraform apenas como plano validado e declare que o deploy AWS ficou pendente. Não substituir evidência real por captura simulada.
