# oficina-auth-lambda

Autenticação serverless de clientes por CPF e JWT.

Este repositório foi extraído do histórico Git do projeto `oficina-mvp`.
Commit de origem: 0021f836eeab5760085300d4cb1d4f0d33e32afa.

```powershell
dotnet test services/Oficina.Auth.Tests --configuration Release
```

O workflow em `.github/workflows` testa e empacota a Function; após configurar OIDC, atualiza o código da Lambda na AWS.
