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
Write-Host "`n1. Очистка старых релизов..." -ForegroundColor Yellow
helm uninstall postgresql 2>$null
kubectl delete -f .\kubernetes-manifests\ --recursive 2>$null
kubectl delete pvc --all 2>$null

# 2. Применение манифестов
Write-Host "`n2. Применение манифестов..." -ForegroundColor Yellow
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

# 4. Установка секретов
Write-Host "`n4. Установка секретов..." -ForegroundColor Cyan
kubectl apply -f .\kubernetes-manifests\secrets\db-secret.yaml
kubectl get secrets -n default
kubectl describe secret db-secret -n default

$POSTGRES_USER = Decode-Base64 (kubectl get secret db-secret -o jsonpath='{.data.POSTGRES_USER}')
$POSTGRES_PASSWORD = Decode-Base64 (kubectl get secret db-secret -o jsonpath='{.data.POSTGRES_PASSWORD}')
$POSTGRES_DB = Decode-Base64 (kubectl get secret db-secret -o jsonpath='{.data.POSTGRES_DB}')

helm upgrade --install postgresql oci://registry-1.docker.io/bitnamicharts/postgresql `
  --set global.postgresql.auth.postgresPassword=$POSTGRES_PASSWORD `
  --set global.postgresql.auth.username=$POSTGRES_USER `
  --set global.postgresql.auth.password=$POSTGRES_PASSWORD `
  --set global.postgresql.auth.database=$POSTGRES_DB `
  --set persistence.enabled=true `
  --set persistence.size=1Gi `
  --set image.repository=bitnamilegacy/postgresql `
  --set image.tag=17.6.0-debian-12-r4 `
  --set volumePermissions.image.repository=bitnamilegacy/os-shell `
  --set global.security.allowInsecureImages=true
  
# 5. Проверка и установка Ingress Nginx Controller
Write-Host "`n5. Проверка и установка Ingress Nginx Controller..." -ForegroundColor Cyan

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

# 6. Ожидание БД
Write-Host "`n6. Ожидание БД..." -ForegroundColor Cyan
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

# 7. Запуск job для миграции БД
Write-Host "`n7. Запуск job для миграции БД..." -ForegroundColor Cyan

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
kubectl logs job/db-migration

# 7.2 Развертывание API
Write-Host "`n7.2. Развертывание flowers-api..." -ForegroundColor Green

# 7.3. Сборка образа
Write-Host "`n7.3. Сборка образа..." -ForegroundColor Green
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
Write-Host "`n8. Проверка flowers-api и вывод логов..." -ForegroundColor Cyan
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
Write-Host "`n9. Итоговый статус:" -ForegroundColor Green
kubectl get pods,svc,ingress

# 10. Проверка доступности API
Write-Host "`n10. Проверка доступности API..." -ForegroundColor Cyan
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

# 15. Проверки
Write-Host "`n15. Проверки:" -ForegroundColor Green

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

# 14. Установка NetData (альтернатива Grafana)
Write-Host "`n16. Установка NetData..." -ForegroundColor Cyan

try {
    # Останавливаем старый контейнер если есть
    docker stop netdata 2>$null
    docker rm netdata 2>$null
    
    # Запуск NetData в Docker
    docker run -d `
      --name=netdata `
      --restart=unless-stopped `
      -p 19999:19999 `
      -v netdataconfig:/etc/netdata `
      -v netdatalib:/var/lib/netdata `
      -v netdatacache:/var/cache/netdata `
      -v /etc/passwd:/host/etc/passwd:ro `
      -v /etc/group:/host/etc/group:ro `
      -v /proc:/host/proc:ro `
      -v /sys:/host/sys:ro `
      -v /etc/os-release:/host/etc/os-release:ro `
      -v /var/log/nginx:/var/log/nginx:ro `
      --cap-add SYS_PTRACE `
      --security-opt apparmor=unconfined `
      netdata/netdata

    Write-Host "NetData успешно запущен: http://localhost:19999" -ForegroundColor Green
    Write-Host "Все метрики собираются автоматически, логин не требуется." -ForegroundColor Cyan
    
    # Даем время на запуск
    Write-Host "Ожидаем запуск NetData..." -ForegroundColor Yellow
    Start-Sleep -Seconds 15
    
    # Настройка web_log для анализа логов Nginx
    Write-Host "Настройка мониторинга логов Nginx (web_log)..." -ForegroundColor Cyan
    
    $web_log_config = @'
jobs:
  - name: nginx_ingress
    path: /var/log/nginx/access.log
'@

    # Сохраняем конфиг
    $web_log_config | Out-File -FilePath ".\web-log.conf" -Encoding utf8
    
    # Копируем конфиг в контейнер NetData
    docker cp .\web-log.conf netdata:/etc/netdata/go.d/web_log.conf
    
    # Перезапускаем NetData для применения конфига
    docker restart netdata
    
    Write-Host "NetData настроен для анализа логов Nginx!" -ForegroundColor Green
    Write-Host "Логи будут доступны в NetData: http://localhost:19999" -ForegroundColor Cyan
    Write-Host "Для просмотра используйте поиск по 'web_log'" -ForegroundColor Cyan
    
    # Удаляем временный файл
    Remove-Item .\web-log.conf -Force 2>$null
    
    # Проверка доступности
    Start-Sleep -Seconds 10
    Write-Host "Проверка работы NetData..." -ForegroundColor Yellow
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:19999/api/v1/info" -TimeoutSec 5 -UseBasicParsing
        Write-Host "✅ NetData полностью настроен и готов к работе!" -ForegroundColor Green
    } catch {
        Write-Host "⚠️ NetData перезагружается, подождите немного..." -ForegroundColor Yellow
    }
}
catch {
    Write-Host "Ошибка при запуске NetData: $_" -ForegroundColor Red
    exit 1
}

# 15. Установка pgAdmin для визуального управления БД
Write-Host "`n15. Установка pgAdmin..." -ForegroundColor Cyan

try {
    # Останавливаем старый контейнер если есть
    docker stop pgadmin 2>$null
    docker rm pgadmin 2>$null

    # Запускаем pgAdmin с правильными настройками
    docker run -d `
      --name pgadmin `
      -p 8080:80 `
      -e PGADMIN_DEFAULT_EMAIL=admin@admin.com `
      -e PGADMIN_DEFAULT_PASSWORD=admin `
      dpage/pgadmin4

    Write-Host "pgAdmin запущен: http://localhost:8080" -ForegroundColor Green
    Write-Host "Логин: admin@admin.com" -ForegroundColor Cyan
    Write-Host "Пароль: admin" -ForegroundColor Cyan
    
    # Даем больше времени на запуск
    Write-Host "Ожидаем запуск pgAdmin (30 секунд)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 30
    
    Write-Host "`nИнструкция по подключению:" -ForegroundColor Yellow
    Write-Host "1. Откройте http://localhost:8080" -ForegroundColor White
    Write-Host "2. Добавьте новый сервер (Right-click Servers → Register → Server)" -ForegroundColor White
    Write-Host "3. В поле 'Host' укажите IP вашего PostgreSQL контейнера" -ForegroundColor White
    Write-Host "4. Используйте логин/пароль из вашего db-secret.yaml" -ForegroundColor White
    
    # Получаем IP адрес PostgreSQL контейнера для подключения
	$pg_ip = kubectl get svc postgresql -o jsonpath='{.spec.clusterIP}' 2>$null

    Write-Host "`nДля подключения используйте:" -ForegroundColor Cyan
    Write-Host "Host: $pg_ip" -ForegroundColor White
}
catch {
    Write-Host "Ошибка при запуске pgAdmin: $_" -ForegroundColor Red
}