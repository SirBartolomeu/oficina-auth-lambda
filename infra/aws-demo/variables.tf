variable "aws_region" {
  description = "Região única da demonstração AWS."
  type        = string
}

variable "name_prefix" {
  description = "Prefixo para evitar colisão com recursos de outras demonstrações."
  type        = string
  default     = "oficina-fiap-demo"

  validation {
    condition     = can(regex("^[a-z0-9-]{3,40}$", var.name_prefix))
    error_message = "Use 3 a 40 letras minúsculas, números ou hífens."
  }
}
