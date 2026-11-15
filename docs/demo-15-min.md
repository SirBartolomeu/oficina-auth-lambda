# Demonstração Fase 4 — 15 minutos

## Preparação

Com Docker Desktop aberto, na raiz do repositório:

```powershell
./scripts/demo-fase4.ps1 -SkipBuild
```

Na primeira execução ou após mudanças nas imagens, omitir `-SkipBuild`. O comando cria uma OS, verifica o fluxo entre OS, Execução e Billing, autentica um cliente pelo adaptador local da Lambda, consulta o tracking protegido, roda o teste focado da Saga e confirma o dashboard. A saída registra os identificadores e URLs; o resumo fica em `docs/evidence/demo-local.json`. Não exibir o token JWT durante a apresentação.

## Roteiro cronometrado

| Tempo | Mostrar | Mensagem principal |
|---|---|---|
| 0–2 min | Diagrama em `docs/architecture.md` e `docker compose ps` | Três APIs .NET independentes, PostgreSQL para OS/Execução, DynamoDB Local para Billing e filas locais. |
| 2–6 min | Executar `./scripts/demo-fase4.ps1 -SkipBuild`; destacar `OS=...` e `trace=...` | A abertura da OS gera eventos, atravessa serviços e chega a `AwaitingApproval`. CPF recebe JWT e acessa somente seu tracking. |
| 6–9 min | Resultado do teste `OpenedOrderMovesThroughApprovalPaymentAndExecution` impresso pelo comando | Aprovação, pagamento **simulado**, reserva e início da execução são coordenados pela Saga. Há compensações testadas no projeto. |
| 9–12 min | Abrir `http://localhost:3000/d/oficina-local` e, se houver tempo, `http://localhost:9090/alerts` | Métricas, logs/traces correlacionados e alerta local. O script `./scripts/verify-alert-local.ps1` pode demonstrar disparo real, mas demora cerca de um minuto. |
| 12–15 min | `docs/evidence/demo-local.json` e limites abaixo | Evidência reproduzível, decisões de custo e próximos passos para deploy autorizado. |

## Contingência e limites

Se o ambiente demorar a iniciar, usar a última execução registrada em `docs/evidence/demo-local.json`, mostrar os containers já ativos e executar apenas o teste focado. O arquivo de evidência registra verificações, mas não substitui a demonstração ao vivo.

Esta demonstração roda **localmente**. O teste de Saga usa provedor de pagamento simulado. Os manifests K3s foram verificados estaticamente, mas não houve implantação AWS/K3s, transação real de testes no Mercado Pago, publicação dos repositórios separados nem comprovação da cobertura acadêmica por serviço. Esses itens permanecem no backlog de `docs/implementation-plan.md`.
