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

variable "rds_instance_class" {
  description = "Classe RDS Single-AZ da demonstração; confirmar disponibilidade e preço na região antes do apply."
  type        = string
  default     = "db.t4g.micro"
}

variable "k3s_instance_type" {
  description = "Instância x86 com memória suficiente para três APIs e observabilidade na janela da demo."
  type        = string
  default     = "t3.large"
}

variable "k3s_version" {
  description = "Versão fixada para reproduzir o bootstrap do K3s."
  type        = string
  default     = "v1.36.6+k3s1"
}
