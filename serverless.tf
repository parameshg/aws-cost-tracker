terraform {
  backend "s3" {
    bucket = "aspnetcore"
    key    = "terraform-state/aws-cost-tracker.tfstate"
  }
}

variable "AWS_ACCOUNT" {
  type = string
  description = "AWS_ACCOUNT"
}

variable "AWS_REGION" {
  type = string
  description = "AWS_REGION"
}

variable "AWS_ACCESS_KEY_ID" {
  type = string
  description = "AWS_ACCESS_KEY_ID"
}

variable "AWS_SECRET_ACCESS_KEY" {
  type = string
  description = "AWS_SECRET_ACCESS_KEY"
}

variable "SENTRY_ENDPOINT" {
  type    = string
  description = "Sentry Dsn"
  default = ""
}

variable "HTTP_TIMEOUT" {
  type        = number
  description = "Lambda Timeout"
  default     = 15
}

variable "PUSHOVER_USER" {
  type    = string
  description = "Pushover User Token"
  default = ""
}

variable "PUSHOVER_TOKEN" {
  type    = string
  description = "Pushover Application Token"
  default = ""
}

provider "aws" {
  region     = var.AWS_REGION
  access_key = var.AWS_ACCESS_KEY_ID
  secret_key = var.AWS_SECRET_ACCESS_KEY
}

# AWS IAM ROLE ####################################################################################################################################################

data "aws_iam_policy_document" "aws-cost-tracker-trust" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

data "aws_iam_policy_document" "aws-cost-tracker-policy" {
  statement {
    actions   = ["ce:GetCostAndUsageWithResources","ce:GetCostAndUsage","ce:GetCostForecast","ce:GetUsageForecast"]
    resources = ["*"]
  }
}

resource "aws_iam_role" "aws-cost-tracker" {
  name               = "aws-cost-tracker"
  managed_policy_arns = ["arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"]
  assume_role_policy = data.aws_iam_policy_document.aws-cost-tracker-trust.json
  inline_policy {
    name   = "inline-policy"
    policy = data.aws_iam_policy_document.aws-cost-tracker-policy.json
  }
  tags               = {
    provisioner      = "terraform"
    executioner      = "github-actions"
    project          = "aws-cost-tracker"
    url              = "https://github.com/parameshg/aws-cost-tracker"
  }
}

# AWS LAMBDA ######################################################################################################################################################

resource "aws_lambda_function" "aws-cost-tracker" {
  function_name  = "aws-cost-tracker"
  role           = "${aws_iam_role.aws-cost-tracker.arn}"
  handler        = "CostTracker::CostTracker.Program::Execute"
  package_type   = "Zip"
  s3_bucket      = "aspnetcore"
  s3_key         = "aws-cost-tracker.zip"
  runtime        = "dotnet6"
  memory_size    = 128
  timeout        = var.HTTP_TIMEOUT
  environment          {
    variables        = {
      SENTRY_DSN     = var.SENTRY_ENDPOINT
      PUSHOVER_USER  = var.PUSHOVER_USER
      PUSHOVER_TOKEN = var.PUSHOVER_TOKEN
    }
  }
  tags           = {
    provisioner  = "terraform"
    executioner  = "github-actions"
    project      = "aws-cost-tracker"
    url          = "https://github.com/parameshg/aws-cost-tracker"
  }
}