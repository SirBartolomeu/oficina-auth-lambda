# Oficina.Auth

Function Serverless .NET 10 para autenticação de cliente por CPF na Fase 3. O handler `Oficina.Auth::Oficina.Auth.Function::FunctionHandler` recebe o evento HTTP API v2 do API Gateway como `Stream`; a rota planejada é `POST /auth/token`. A função valida os dígitos do CPF, consulta a OS por `POST /internal/customers/lookup` com token de serviço e emite JWT de cliente com validade de 15 minutos. CPF não entra na URL, no JWT ou nos logs da função. Cliente ausente, inativo ou CPF inválido recebe a mesma resposta 401.

Configuração por ambiente/segredos:

| Variável | Uso |
|---|---|
| `AUTH_LOOKUP_URL` | URL completa do endpoint interno da OS |
| `AUTH_LOOKUP_TOKEN` | Mesmo segredo configurado como `Auth:LookupToken` na OS; mínimo 32 caracteres |
| `AUTH_JWT_KEY` | Mesma chave de validação JWT da OS; mínimo 32 caracteres |
| `AUTH_JWT_ISSUER`, `AUTH_JWT_AUDIENCE` | Mesmo emissor e audiência aceitos pela OS |

Execução local: `dotnet test services/Oficina.Auth.Tests`. Para gerar o pacote local: `dotnet publish services/Oficina.Auth -c Release`. O deploy futuro usa runtime gerenciado `dotnet10`, handler acima e pacote ZIP do conteúdo de `bin/Release/net10.0/publish`. Não é necessário pacote `Amazon.Lambda.Core` porque o handler recebe e devolve `Stream`. A função ainda precisa ser ligada ao gateway, aos segredos e ao pipeline por Terraform.

CPF sozinho atende ao enunciado acadêmico, mas não comprova posse da identidade; limitar taxa no gateway e usar segundo fator antes de exposição real de dados pessoais. O serviço consulta a API da OS, sem acesso ao PostgreSQL.
