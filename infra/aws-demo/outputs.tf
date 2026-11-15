output "ecr_repositories" {
  description = "URIs das imagens imutáveis dos três serviços."
  value       = { for name, repo in aws_ecr_repository.service : name => repo.repository_url }
}

output "queue_urls" {
  description = "URLs a injetar nos ConfigMaps de OS, Execução e Billing."
  value       = { for name, queue in aws_sqs_queue.main : name => queue.id }
}

output "billing_table_name" {
  description = "Tabela própria do Billing."
  value       = aws_dynamodb_table.billing.name
}
