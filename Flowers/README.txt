ссылка на директорию в github, где находится директория с манифестами кубернетеса - https://github.com/den090312/Flowers/tree/master/Flowers
инструкция по запуску приложения. - 1) Запустить Docker Desktop (с включенным Kubernetes). 2) Запустить run.cmd
команда установки БД из helm, вместе с файлом values.yaml. - это внутри https://github.com/den090312/Flowers/blob/master/Flowers/run.cmd *
команда применения первоначальных миграций - это внутри https://github.com/den090312/Flowers/blob/master/Flowers/run.cmd **
команда kubectl apply -f, которая запускает в правильном порядке манифесты кубернетеса - это внутри https://github.com/den090312/Flowers/blob/master/Flowers/run.cmd ***
Postman коллекция, в которой будут представлены примеры запросов к сервису на создание, получение, изменение и удаление пользователя. Важно: в postman коллекции использовать базовый url - arch.homework. - https://github.com/den090312/Flowers/blob/master/Flowers/my_collection.json
Проверить корректность работы приложения используя созданную коллекцию newman run коллекция_постман и приложить скриншот/вывод исполнения корректной работы - https://github.com/den090312/Flowers/blob/master/Flowers/newman%20run%20my-collection.png

*# 3. Установка PostgreSQL
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

**# 6. Запуск job для миграции БД
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

***# 2. Применение манифестов
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
