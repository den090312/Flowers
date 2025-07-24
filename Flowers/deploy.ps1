<#
.SYNOPSIS
  Полное развертывание инфраструктуры с проверкой таблицы users
#>

# 0. Переход в папку скрипта (совместимая версия)
if ($PSScriptRoot) {
    $scriptDir = $PSScriptRoot
} else {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
Write-Host "Автоматический переход в папку скрипта: $scriptDir" -ForegroundColor DarkGray
Set-Location $scriptDir

# 1. Очистка
Write-Host "1. Очистка старых релизов..." -ForegroundColor Yellow
helm uninstall postgresql 2>$null
kubectl delete -f .\kubernetes-manifests\ --recursive 2>$null
kubectl delete pvc --all 2>$null

# 2. Применение манифестов
$manifests = @(
    ".\kubernetes-manifests\configmaps\",
    ".\kubernetes-manifests\secrets\",
    ".\kubernetes-manifests\deployments\",
    ".\kubernetes-manifests\services\",
    ".\kubernetes-manifests\ingress\"
)


foreach ($manifest in $manifests) {
    try {
        kubectl apply -f $manifest
    } catch {
        Write-Host "Ошибка при применении манифеста $manifest : $_" -ForegroundColor Yellow
    }
}

# 3. Установка PostgreSQL
Write-Host "`n3. Установка PostgreSQL..." -ForegroundColor Cyan

$POSTGRES_USER = kubectl get secret db-secret -o jsonpath='{.data.POSTGRES_USER}' | base64 -d
$POSTGRES_PASSWORD = kubectl get secret db-secret -o jsonpath='{.data.POSTGRES_PASSWORD}' | base64 -d
$POSTGRES_DB = kubectl get secret db-secret -o jsonpath='{.data.POSTGRES_DB}' | base64 -d

helm upgrade --install postgresql bitnami/postgresql `
  --set global.postgresql.auth.postgresPassword=$POSTGRES_PASSWORD `
  --set global.postgresql.auth.username=$POSTGRES_USER `
  --set global.postgresql.auth.password=$POSTGRES_PASSWORD `
  --set global.postgresql.auth.database=$POSTGRES_DB `
  --set persistence.enabled=true `
  --set persistence.size=1Gi
  
# 4. Проверка и установка Ingress Nginx Controller
Write-Host "`n4. Проверка и установка Ingress Nginx Controller..." -ForegroundColor Cyan

# Проверяем, установлен ли уже ingress-nginx
$ingressInstalled = helm list -n ingress-nginx | findstr "ingress-nginx"

if (-not $ingressInstalled) {
    # Добавляем репозиторий (если еще не добавлен)
    helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx 2>$null
    helm repo update

    Write-Host "Установка Ingress Nginx Controller..." -ForegroundColor Yellow
    
    # Устанавливаем с явными параметрами и таймаутом
    $ingressJob = Start-Job -ScriptBlock {
        helm upgrade --install ingress-nginx ingress-nginx/ingress-nginx `
            --namespace ingress-nginx `
            --create-namespace `
            --set controller.service.type=LoadBalancer `
            --set controller.ingressClassResource.default=true `
            --atomic `
            --timeout 5m
    }

    # Ожидание с прогресс-баром
    Write-Host "Ожидаем завершения установки (максимум 5 минут)..." -NoNewline
    $timeout = 180 # 3 минуты
    $startTime = Get-Date

    while ($ingressJob.State -eq "Running") {
        if (((Get-Date) - $startTime).TotalSeconds -gt $timeout) {
            Stop-Job $ingressJob
            Write-Host "`nТаймаут установки Ingress Controller!" -ForegroundColor Red
            exit 1
        }
        
        Write-Host "." -NoNewline
        Start-Sleep -Seconds 5
    }

    Receive-Job $ingressJob
    Remove-Job $ingressJob

    Write-Host "`nIngress Nginx Controller успешно установлен!" -ForegroundColor Green
} else {
    Write-Host "Ingress Nginx Controller уже установлен, пропускаем установку" -ForegroundColor Green
}

# 5. Ожидание БД
Write-Host "`n5. Ожидание PostgreSQL..." -ForegroundColor Cyan
$timeout = 180
$startTime = Get-Date
$dbReady = $false

while (-not $dbReady) {
    try {
        $status = kubectl get pod -l app.kubernetes.io/name=postgresql -o json | ConvertFrom-Json
        if ($status.items.status.containerStatuses.ready -eq $true) {
            $dbReady = $true
            break
        }
    } catch {
        # Продолжаем ожидать если команда не сработала
    }
    
    if (((Get-Date) - $startTime).TotalSeconds -gt $timeout) {
        Write-Host "Таймаут ожидания PostgreSQL!" -ForegroundColor Red
        exit 1
    }
    
    Start-Sleep -Seconds 5
    Write-Host "Ожидание..." -ForegroundColor Gray
}

# 6. Запуск job для миграции БД
Write-Host "`n6. Запуск job для миграции БД..." -ForegroundColor Cyan

# Применяем job
kubectl apply -f .\kubernetes-manifests\jobs\db-migration-job.yaml

# Ожидаем завершения job
Write-Host "Ожидаем завершения миграции..." -ForegroundColor Yellow
$timeout = 120
$startTime = Get-Date

while ($true) {
    $jobStatus = kubectl get job db-migration -o json | ConvertFrom-Json
    if ($jobStatus.status.succeeded -eq 1) {
        Write-Host "Миграция БД успешно завершена!" -ForegroundColor Green
        break
    }
    elseif ($jobStatus.status.failed -gt 0) {
        Write-Host "Ошибка при выполнении миграции!" -ForegroundColor Red
        kubectl logs -l job-name=db-migration
        exit 1
    }
    
    if (((Get-Date) - $startTime).TotalSeconds -gt $timeout) {
        Write-Host "Таймаут ожидания миграции БД!" -ForegroundColor Red
        kubectl logs -l job-name=db-migration
        exit 1
    }
    
    Start-Sleep -Seconds 5
    Write-Host "." -NoNewline
}

# Выводим логи job
Write-Host "`nЛоги миграции:" -ForegroundColor Cyan
kubectl logs -l job-name=db-migration

# 7.2 Развертывание API
Write-Host "`n7.2. Развертывание flowers-api..." -ForegroundColor Green

# 7.3. Сборка образа
try {
    docker build -t flowers-api:latest .
    if (-not $?) {
        throw "Ошибка сборки Docker образа"
    }
} catch {
    Write-Host "Ошибка при сборке образа: $_" -ForegroundColor Red
    exit 1
}

# 8. Проверка запуска API и вывод логов
Write-Host "`n7. Проверка flowers-api и вывод логов..." -ForegroundColor Cyan
$timeout = 120
$startTime = Get-Date
$apiReady = $false
$logsChecked = $false

while (-not $apiReady) {
    try {
        $pod = kubectl get pod -l app=flowers-api -o json | ConvertFrom-Json
        $status = $pod.items.status
        
        if ($status.phase -eq "Running" -and $status.containerStatuses.ready -eq $true) {
            Write-Host "Pod запущен!" -ForegroundColor Green
            
            # Выводим логи API только один раз
            if (-not $logsChecked) {
                Write-Host "`nЛоги flowers-api:" -ForegroundColor Yellow
                $logs = kubectl logs -l app=flowers-api --tail=20
                $logs | ForEach-Object {
                    if ($_ -match "Now listening on:|Application started|Hosting environment|Content root path") {
                        Write-Host $_ -ForegroundColor Cyan
                    } else {
                        Write-Host $_
                    }
                }
                $logsChecked = $true
                
                # Дополнительная проверка ключевых сообщений
                if ($logs -notmatch "Application started") {
                    Write-Host "Предупреждение: API не отправил сообщение о старте приложения" -ForegroundColor Yellow
                }
            }
            
            $apiReady = $true
            break
        }
    } catch {
        # Продолжаем ожидать если команда не сработала
    }
    
    if (((Get-Date) - $startTime).TotalSeconds -gt $timeout) {
        Write-Host "Таймаут ожидания API!" -ForegroundColor Red
        Write-Host "Последние логи:" -ForegroundColor Red
        kubectl logs -l app=flowers-api --tail=50
        exit 1
    }
    
    Start-Sleep -Seconds 5
    Write-Host "Ожидание API..." -ForegroundColor Gray
}

# 9. Финальные проверки
Write-Host "`n8. Итоговый статус:" -ForegroundColor Green
kubectl get pods,svc,ingress

# 10. Проверка доступности API
Write-Host "`n9. Проверка доступности API..." -ForegroundColor Cyan
$ingressHost = kubectl get ingress -o jsonpath='{.items[0].spec.rules[0].host}'
$apiUrl = "http://$ingressHost/"

Write-Host "Выполняем тестовый запрос к API: $apiUrl" -ForegroundColor Yellow

try {
    $response = Invoke-WebRequest -Uri $apiUrl -Method Get -UseBasicParsing -TimeoutSec 10
    
    if ($response.StatusCode -eq 200) {
        Write-Host "API успешно отвечает! Результат:" -ForegroundColor Green
        Write-Host $response.Content -ForegroundColor DarkGray
    } else {
        Write-Host "API вернул неожиданный статус: $($response.StatusCode)" -ForegroundColor Yellow
        Write-Host "Ответ сервера:" -ForegroundColor DarkGray
        Write-Host $response.Content
    }
} catch {
    Write-Host "Ошибка при запросе к API:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    
    # Дополнительная диагностика
    Write-Host "`nПроверка endpoints сервиса:" -ForegroundColor Yellow
    kubectl get endpoints
    
    Write-Host "`nПоследние логи API:" -ForegroundColor Yellow
    kubectl logs -l app=flowers-api --tail=20
}

Write-Host "`nГотово! Для ручной проверки выполните:" -ForegroundColor Green
Write-Host "curl $apiUrl" -ForegroundColor Cyan
Write-Host "или откройте в браузере: $apiUrl" -ForegroundColor Cyan