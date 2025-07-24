<#
.SYNOPSIS
  Полное развертывание инфраструктуры (Helm + kubectl apply)
#>

# 0. Автоматический переход в папку скрипта
$scriptDir = if ($PSScriptRoot) { 
    $PSScriptRoot 
} else { 
    Split-Path -Parent $MyInvocation.MyCommand.Path 
}
Write-Host "Автоматический переход в папку скрипта: $scriptDir" -ForegroundColor DarkGray
Set-Location $scriptDir

# 1. Установка PostgreSQL через Helm
Write-Host "1. Установка PostgreSQL через Helm..." -ForegroundColor Cyan
helm repo add bitnami https://raw.githubusercontent.com/bitnami/charts/archive-full-index/bitnami
helm repo update

helm upgrade --install postgresql bitnami/postgresql `
  --set global.postgresql.auth.postgresPassword=postgres `
  --set global.postgresql.auth.username=postgres `
  --set global.postgresql.auth.password=postgres `
  --set global.postgresql.auth.database=users_db `
  --set persistence.enabled=true `
  --set persistence.size=1Gi

# 2. Ожидание готовности БД
Write-Host "`n2. Ожидание готовности PostgreSQL..." -ForegroundColor Cyan
kubectl wait --for=condition=Ready pod `
  -l app.kubernetes.io/name=postgresql `
  --timeout=300s

# 3. Применение манифестов в правильном порядке
Write-Host "`n3. Применение манифестов Kubernetes..." -ForegroundColor Cyan

# 3.1. ConfigMap
kubectl apply -f .\kubernetes-manifests\configmaps\app-config.yaml

# 3.2. Secrets
kubectl apply -f .\kubernetes-manifests\secrets\db-secret.yaml

# 3.3. Миграции (Job)
kubectl apply -f .\kubernetes-manifests\jobs\db-migration-job.yaml
kubectl wait --for=condition=complete job/db-migration --timeout=300s

# 3.4. Основное приложение
kubectl apply -f .\kubernetes-manifests\deployments\app-deployment.yaml
kubectl apply -f .\kubernetes-manifests\services\app-service.yaml
kubectl apply -f .\kubernetes-manifests\ingress\app-ingress.yaml

# 4. Проверка
Write-Host "`n4. Проверка развертывания:" -ForegroundColor Green
kubectl get pods,svc,ingress

Write-Host "`nПроверка таблицы Users:"
kubectl exec pod/postgresql-0 -- env PGPASSWORD=postgres psql -U postgres -d users_db -c "\dt"

Write-Host "`nДля доступа к API:"
kubectl get ingress -o jsonpath='{.items[0].spec.rules[0].host}'