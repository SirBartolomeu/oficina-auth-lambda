# Gateway da demonstração

O K3s planejado inclui Traefik. `k8s/ingress.yaml` usa `ingressClassName: traefik` e encaminha `/api` à OS sem reescrever caminhos. `/internal/customers/lookup` não está na rota pública; a Function o consulta pela rede interna com `X-Internal-Token`. O token de serviço e a chave JWT devem vir de segredos, nunca da imagem.

O teste `CustomerAuthFlowTests` executa localmente CPF → handler da Function → consulta HTTP interna à OS → JWT → tracking protegido, sem criar recursos AWS. A ligação da Function ao gateway público, TLS, rate limit e as rotas dos outros serviços ainda precisam de manifests/Terraform e validação em K3s. O Ingress foi validado com `kubectl kustomize k8s`; não há prova de roteamento no cluster nesta etapa.
