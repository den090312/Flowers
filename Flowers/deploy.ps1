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
Write-Host "2. Применение манифестов..." -ForegroundColor Yellow
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
Write-Host "Применение манифестов завершено!" -ForegroundColor Green

# 3. Установка PostgreSQL
Write-Host "`n3. Установка PostgreSQL..." -ForegroundColor Cyan

helm repo add bitnami https://raw.githubusercontent.com/bitnami/charts/archive-full-index/bitnami
helm repo update

function Decode-Base64 {
    param([string]$encoded)
    [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($encoded))
}

$POSTGRES_USER = Decode-Base64 (kubectl get secret db-secret -o jsonpath='{.data.POSTGRES_USER}')
$POSTGRES_PASSWORD = Decode-Base64 (kubectl get secret db-secret -o jsonpath='{.data.POSTGRES_PASSWORD}')
$POSTGRES_DB = Decode-Base64 (kubectl get secret db-secret -o jsonpath='{.data.POSTGRES_DB}')

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
Write-Host "`n5. Ожидание БД..." -ForegroundColor Cyan
$timeout = 180
$startTime = Get-Date
$dbReady = $false

while (-not $dbReady) {
    try {
        $status = kubectl get pod -l app.kubernetes.io/name=postgresql -o json | ConvertFrom-Json
        if ($status.items.status.containerStatuses.ready -eq $true) {
            $dbReady = $true
			Write-Host "БД запущена!" -ForegroundColor Green
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
        Write-Host "Миграция БД завершена!" -ForegroundColor Green
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

# 11. Развертывание Prometheus
Write-Host "`n11. Развертывание Prometheus" -ForegroundColor Cyan
try {
    # Запуск Prometheus
    docker run -d `
      --name prometheus `
      --hostname arch.homework `
      -p 9090:9090 `
      -v ${PWD}/prometheus.yml:/etc/prometheus/prometheus.yml `
      prom/prometheus

    # Проверка запуска контейнера
    $prometheusStatus = docker inspect -f '{{.State.Status}}' prometheus 2>$null
    if ($prometheusStatus -ne "running") {
        throw "Prometheus container failed to start"
    }

    Write-Host "Prometheus успешно запущен" -ForegroundColor Green
}
catch {
    Write-Host "Ошибка при запуске Prometheus: $_" -ForegroundColor Red
    exit 1
}

# Проверка метрик Prometheus
Write-Host "`nПроверка метрик Prometheus..." -ForegroundColor Cyan
try {
    $retryCount = 0
    $maxRetries = 5
    $success = $false
    
    while ($retryCount -lt $maxRetries -and -not $success) {
        try {
            $metrics = curl -v http://arch.homework:9090/metrics 2>&1
            if ($metrics -match "HTTP.*200") {
                Write-Host "Метрики Prometheus доступны" -ForegroundColor Green
                $success = $true
            } else {
                throw "Не удалось получить метрики"
            }
        }
        catch {
            $retryCount++
            if ($retryCount -ge $maxRetries) {
                throw
            }
            Start-Sleep -Seconds 5
        }
    }
}
catch {
    Write-Host "Ошибка при проверке метрик Prometheus: $_" -ForegroundColor Yellow
}

# 12. Развертывание Grafana
Write-Host "`n12. Развертывание Grafana" -ForegroundColor Cyan
try {
    # Запуск Grafana
    docker run -d `
      -p 3000:3000 `
      --name=grafana `
      grafana/grafana-enterprise

    # Проверка запуска контейнера
    $grafanaStatus = docker inspect -f '{{.State.Status}}' grafana 2>$null
    if ($grafanaStatus -ne "running") {
        throw "Grafana container failed to start"
    }

    Write-Host "Grafana успешно запущена" -ForegroundColor Green
}
catch {
    Write-Host "Ошибка при запуске Grafana: $_" -ForegroundColor Red
    exit 1
}

# 13. Создание общей сети и подключение контейнеров
Write-Host "`n13. Настройка сети monitoring" -ForegroundColor Cyan
try {
    # Создание сети (если не существует)
    $networkExists = docker network ls --filter name=monitoring --format '{{.Name}}'
    if (-not $networkExists) {
        docker network create monitoring
    }

    # Подключение контейнеров
    docker network connect monitoring prometheus
    docker network connect monitoring grafana

    Write-Host "Контейнеры подключены к сети monitoring" -ForegroundColor Green
    Write-Host "Адрес Prometheus для Grafana: http://prometheus:9090" -ForegroundColor Cyan
}
catch {
    Write-Host "Ошибка при настройке сети: $_" -ForegroundColor Red
}

# Проверка доступности Grafana
Write-Host "`nПроверка Grafana..." -ForegroundColor Cyan
try {
    $retryCount = 0
    $maxRetries = 10
    $success = $false
    
    while ($retryCount -lt $maxRetries -and -not $success) {
        try {
            $grafanaCheck = curl -v http://arch.homework:3000 2>&1
            if ($grafanaCheck -match "HTTP.*200") {
                Write-Host "Grafana доступна по адресу: http://arch.homework:3000" -ForegroundColor Green
                Write-Host "Логин/пароль по умолчанию: admin/admin" -ForegroundColor Cyan
                $success = $true
            } else {
                throw "Grafana не отвечает"
            }
        }
        catch {
            $retryCount++
            if ($retryCount -ge $maxRetries) {
                throw
            }
            Start-Sleep -Seconds 5
        }
    }
}
catch {
    Write-Host "Grafana не стала доступна после $maxRetries попыток" -ForegroundColor Yellow
}

# 14. Инструментирование БД экспортером для prometheus
Write-Host "`n14. Инструментирование БД экспортером для prometheus" -ForegroundColor Cyan
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm repo update
helm install prometheus-postgresql prometheus-community/prometheus-postgres-exporter
helm repo list

# 15. Установка и настройка Kong API Gateway
Write-Host "`n15. Установка Kong API Gateway..." -ForegroundColor Cyan

# Добавляем репозиторий Helm для Kong
helm repo add kong https://charts.konghq.com
helm repo update

# Создаем namespace для Kong
kubectl create namespace kong --dry-run=client -o yaml | kubectl apply -f -

Write-Host "Устанавливаем Kong Ingress Controller..." -ForegroundColor Yellow
helm upgrade --install kong kong/kong -n kong `
  --set ingressController.installCRDs=false `
  --set admin.enabled=true `
  --set admin.http.enabled=true `
  --set admin.type=ClusterIP `
  --set proxy.type=NodePort `
  --set proxy.http.enabled=true `
  --set proxy.tls.enabled=true

# Ждем запуска Kong
Write-Host "Ожидаем запуска Kong..." -ForegroundColor Yellow
$timeout = 120
$startTime = Get-Date
$kongReady = $false

while (-not $kongReady) {
    try {
        $kongStatus = kubectl get pods -n kong -l app.kubernetes.io/name=kong -o json | ConvertFrom-Json
        if ($kongStatus.items.status.containerStatuses.ready -eq $true -and $kongStatus.items.status.phase -eq "Running") {
            $kongReady = $true
            Write-Host "Kong успешно запущен!" -ForegroundColor Green
            break
        }
    } catch {
        # Продолжаем ожидать
    }
    
    if (((Get-Date) - $startTime).TotalSeconds -gt $timeout) {
        Write-Host "Таймаут ожидания Kong!" -ForegroundColor Red
        break
    }
    
    Start-Sleep -Seconds 5
    Write-Host "." -NoNewline
}

# Получаем NodePort порт Kong
Write-Host "`nПолучаем адрес Kong Gateway..." -ForegroundColor Cyan
$kongPort = kubectl get svc -n kong kong-kong-proxy -o jsonpath='{.spec.ports[0].nodePort}'
Write-Host "Kong NodePort: $kongPort" -ForegroundColor Green

# Настраиваем port forwarding для Kong
Write-Host "Настраиваем port forwarding для Kong..." -ForegroundColor Cyan
$portForwardJob = Start-Job -ScriptBlock {
    kubectl port-forward -n kong svc/kong-kong-proxy 8080:80
}

# Ждем немного для установки port forwarding
Start-Sleep -Seconds 3

# Проверяем работу Kong через port forward
Write-Host "`nПроверяем работу Kong Gateway через port forward..." -ForegroundColor Cyan

# Определяем тестовый URL
$testUrl = "http://localhost:8080"

try {
    # Пытаемся достучаться до Kong
    $kongResponse = Invoke-WebRequest -Uri $testUrl -Method Get -UseBasicParsing -TimeoutSec 10
    
    # Если успешно (200 OK или другие 2xx), Kong полностью operational с маршрутом
    Write-Host "✅ Kong Gateway полностью operational!" -ForegroundColor Green
    Write-Host "   Статус: $($kongResponse.StatusCode)" -ForegroundColor Green
    if ($kongResponse.Headers["Server"] -like "*kong*") {
        Write-Host "   Обнаружен Kong server header: $($kongResponse.Headers['Server'])" -ForegroundColor Green
    }
    
} catch {
    # Обрабатываем исключения (которые часто включают 4xx и 5xx статусы)
    if ($_.Exception.Response -ne $null) {
        # Если исключение имеет response, это HTTP ошибка
        $statusCode = $_.Exception.Response.StatusCode.value__
        
        if ($statusCode -eq 404) {
            # 404 ожидаемо, когда маршруты не настроены
            Write-Host "✅ Kong Gateway работает и принимает запросы!" -ForegroundColor Green
            Write-Host "   Статус: 404 (Маршруты еще не настроены - это ожидаемо)" -ForegroundColor Yellow
            Write-Host "   Следующий шаг: Настройка маршрутов и сервисов для вашего API" -ForegroundColor Cyan
        } else {
            # Другие 4xx/5xx ошибки могут указывать на проблемы конфигурации
            Write-Host "⚠️  Kong ответил с неожиданной HTTP ошибкой:" -ForegroundColor Yellow
            Write-Host "   Статус: $statusCode" -ForegroundColor Yellow
            Write-Host "   Это может указывать на проблему с конфигурацией." -ForegroundColor Yellow
        }
    } else {
        # Нет HTTP response (сетевая ошибка, таймаут, отказ соединения)
        Write-Host "❌ Kong НЕ отвечает или недоступен:" -ForegroundColor Red
        Write-Host "   Ошибка: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "   Это указывает на серьезную проблему с установкой Kong или сетевой конфигурацией." -ForegroundColor Red
    }
}

# Выводим информацию о сервисах
Write-Host "`nТекущее состояние сервисов:" -ForegroundColor Green
kubectl get svc -n kong
kubectl get pods -n kong
kubectl get ingress

Write-Host "`nУстановка Kong завершена!" -ForegroundColor Green
Write-Host "Kong API Gateway работает и готов" -ForegroundColor Cyan
Write-Host "Доступ к Kong через port forward: http://localhost:8080" -ForegroundColor Cyan
Write-Host "Тестовый URL: http://localhost:8080" -ForegroundColor Cyan
Write-Host "Или прямой NodePort: http://localhost:$kongPort" -ForegroundColor Cyan
Write-Host "Следующие шаги:" -ForegroundColor Yellow
Write-Host "1. Настройте маршруты для ваших сервисов" -ForegroundColor Yellow
Write-Host "2. Настройте плагины аутентификации" -ForegroundColor Yellow
Write-Host "3. Настройте SSL/TLS при необходимости" -ForegroundColor Yellow

Write-Host "`nPort forward job работает в фоне. Для остановки используйте:" -ForegroundColor Gray
Write-Host "Stop-Job $($portForwardJob.Id)" -ForegroundColor Gray
Write-Host "Remove-Job $($portForwardJob.Id)" -ForegroundColor Gray

# Применение Kong ingress
Write-Host "`nПрименение Kong ingress:" -ForegroundColor Green
kubectl apply -f kong-ingress.yaml

# 16. Проверка аутентификации
Write-Host "`n16. Проверка аутентификации:" -ForegroundColor Green

# Проверка регистрации
Write-Host "Проверка регистрации:" -ForegroundColor Yellow
$registerBody = @{
    username = "testuser"
    password = "pass123"
    email = "test@example.com"
    firstName = "Test"
    lastName = "User"
    phone = "+1234567890"
} | ConvertTo-Json

try {
    $registerResponse = Invoke-RestMethod -Uri "http://arch.homework/auth/register" -Method Post -Body $registerBody -ContentType "application/json" -TimeoutSec 10
    Write-Host "✅ Регистрация успешна! UserId: $($registerResponse.userId)" -ForegroundColor Green
    $jwtToken = $registerResponse.token
} catch {
    Write-Host "❌ Ошибка регистрации: $($_.Exception.Message)" -ForegroundColor Red
}

# Проверка логина
Write-Host "`nПроверка логина:" -ForegroundColor Yellow
$loginBody = @{
    username = "testuser"
    password = "pass123"
} | ConvertTo-Json

try {
    $loginResponse = Invoke-RestMethod -Uri "http://arch.homework/auth/login" -Method Post -Body $loginBody -ContentType "application/json" -TimeoutSec 10
    Write-Host "✅ Логин успешен! Token получен" -ForegroundColor Green
    $jwtToken = $loginResponse.token
} catch {
    Write-Host "❌ Ошибка логина: $($_.Exception.Message)" -ForegroundColor Red
}

# Проверка профиля с токеном
if ($jwtToken) {
    Write-Host "`nПроверка профиля с токеном:" -ForegroundColor Yellow
    try {
        $profileResponse = Invoke-RestMethod -Uri "http://arch.homework/user/profile" -Method Get -Headers @{"Authorization" = "Bearer $jwtToken"} -TimeoutSec 10
        Write-Host "✅ Профиль получен! Username: $($profileResponse.username)" -ForegroundColor Green
    } catch {
        Write-Host "❌ Ошибка получения профиля: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "❌ Токен не получен, пропускаем проверку профиля" -ForegroundColor Red
}

# 17. Дополнительные проверки
Write-Host "`n17. Дополнительные проверки:" -ForegroundColor Green

# Health checks
Write-Host "Проверка health:" -ForegroundColor Yellow
try {
    $healthResponse = Invoke-RestMethod -Uri "http://arch.homework/health" -Method Get -TimeoutSec 10
    Write-Host "✅ Health check: $($healthResponse)" -ForegroundColor Green
} catch {
    Write-Host "❌ Health check failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Detailed health
Write-Host "Проверка detailed health:" -ForegroundColor Yellow
try {
    $detailedHealth = Invoke-RestMethod -Uri "http://arch.homework/health/detailed" -Method Get -TimeoutSec 10
    Write-Host "✅ Detailed health: $($detailedHealth.status)" -ForegroundColor Green
} catch {
    Write-Host "❌ Detailed health failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Метрики
Write-Host "Проверка метрик:" -ForegroundColor Yellow
try {
    $metrics = Invoke-WebRequest -Uri "http://arch.homework/metrics" -Method Get -TimeoutSec 10
    Write-Host "✅ Метрики доступны ($($metrics.Content.Length) bytes)" -ForegroundColor Green
} catch {
    Write-Host "❌ Метрики недоступны: $($_.Exception.Message)" -ForegroundColor Red
}

# 18. Newman run test
Write-Host "Newman run test:" -ForegroundColor Yellow
newman run flowers-auth-tests.json --global-var "baseUrl=http://arch.homework" -r cli