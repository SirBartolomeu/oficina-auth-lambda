output "ecr_repositories" {
  description = "URIs das imagens imutáveis dos três serviços."
  value       = { for name, repo in aws_ecr_repository.service : name => repo.repository_url }
}

output "ecr_repository_names" {
  description = "Nomes a configurar nas variables ECR_REPOSITORY_* dos workflows."
  value       = { for name, repo in aws_ecr_repository.service : name => repo.name }
}

output "queue_urls" {
  description = "URLs a injetar nos ConfigMaps de OS, Execução e Billing."
  value       = { for name, queue in aws_sqs_queue.main : name => queue.id }
}

output "billing_table_name" {
  description = "Tabela própria do Billing."
  value       = aws_dynamodb_table.billing.name
}

output "rds_endpoint" {
  description = "Endpoint privado; criar os bancos e usuários próprios de OS e Execução no bootstrap."
  value       = aws_db_instance.postgres.address
}

output "rds_master_secret_arn" {
  description = "Segredo gerenciado pelo RDS; não imprimir o valor da senha."
  value       = aws_db_instance.postgres.master_user_secret[0].secret_arn
}

output "k3s_security_group_id" {
  description = "Security group para a futura instância EC2/K3s."
  value       = aws_security_group.k3s.id
}

output "k3s_subnet_id" {
  description = "Subnet pública para o futuro nó K3s acessado via SSM."
  value       = aws_subnet.public.id
}

output "k3s_instance_id" {
  description = "Usar no Session Manager ou Run Command; não há SSH público."
  value       = aws_instance.k3s.id
}

output "github_ecr_push_role_arns" {
  description = "Configurar cada ARN como variável do workflow GitHub correspondente."
  value       = { for name, role in aws_iam_role.github_push : name => role.arn }
}

output "workload_role_arns" {
  description = "Roles temporárias distintas para OS, Execução e Billing no K3s."
  value       = { for name, role in aws_iam_role.workload : name => role.arn }
}
