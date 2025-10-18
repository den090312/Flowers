# Автоматически находим контейнер Nginx Ingress
$nginx_container = docker ps --filter "name=ingress-nginx" --format "{{.Names}}" | Select-Object -First 1

if (-not $nginx_container) {
    Write-Host "Контейнер Nginx Ingress не найден!" -ForegroundColor Red
    exit 1
}

Write-Host "🎬 Логи в реальном времени (Ctrl+C для остановки)" -ForegroundColor Magenta
Write-Host "Контейнер: $nginx_container" -ForegroundColor Cyan
Write-Host "=" * 80 -ForegroundColor DarkGray

docker logs -f $nginx_container | ForEach-Object {
    $line = $_
    
    # Парсим строку лога
    if ($line -match '(\S+) - - \[([^\]]+)\] "(\S+) (\S+) (\S+)" (\d+) (\d+) "[^"]*" "[^"]*"') {
        $ip = $Matches[1]
        $timestamp = $Matches[2]
        $method = $Matches[3]
        $url = $Matches[4]
        $protocol = $Matches[5]
        $status = $Matches[6]
        $size = $Matches[7]
        
        # Преобразуем время в нормальный формат
        try {
            $utcTime = [DateTime]::ParseExact($timestamp, "dd/MMM/yyyy:HH:mm:ss zzz", [System.Globalization.CultureInfo]::InvariantCulture)
            $localTime = $utcTime.ToLocalTime()
            $formattedTime = $localTime.ToString("dd MMMM yyyy 'года' HH:mm:ss", [System.Globalization.CultureInfo]::GetCultureInfo("ru-RU"))
        }
        catch {
            $formattedTime = $timestamp
        }
        
        # Определяем цвет в зависимости от статуса
        $statusColor = switch -regex ($status) {
            "^2" { "Green" }     # 2xx - зеленый
            "^3" { "Blue" }      # 3xx - синий  
            "^4" { "DarkYellow" } # 4xx - оранжевый/темно-желтый
            "^5" { "Red" }       # 5xx - красный
            default { "Gray" }
        }
        
        # Определяем цвет метода
        $methodColor = switch ($method) {
            "GET" { "Cyan" }
            "POST" { "Yellow" }
            "PUT" { "Magenta" }
            "DELETE" { "Red" }
            "PATCH" { "Green" }
            default { "White" }
        }
        
        # Выводим основную информацию
        Write-Host "`n┌─── ЗАПРОС ───" -ForegroundColor DarkGray
        Write-Host "│ Время:    " -NoNewline -ForegroundColor DarkGray
        Write-Host $formattedTime -ForegroundColor White
        Write-Host "│ Метод:    " -NoNewline -ForegroundColor DarkGray
        Write-Host $method -ForegroundColor $methodColor -NoNewline
        Write-Host " | URL: " -NoNewline -ForegroundColor DarkGray
        Write-Host $url -ForegroundColor White
        Write-Host "│ Статус:   " -NoNewline -ForegroundColor DarkGray
        Write-Host "$status ($size bytes)" -ForegroundColor $statusColor
        Write-Host "│ Клиент:   " -NoNewline -ForegroundColor DarkGray
        Write-Host $ip -ForegroundColor White
        
        # Дополнительная информация если есть в логе
        if ($line -match 'request_time:([\d.]+)') {
            Write-Host "│ Время:    " -NoNewline -ForegroundColor DarkGray
            Write-Host "$($Matches[1])s" -ForegroundColor White
        }
        
        # Разделитель между запросами
        Write-Host "─" * 80 -ForegroundColor DarkGray
    }
    else {
        # Если строка не распарсилась как стандартный лог, выводим как есть
        Write-Host $line -ForegroundColor Gray
    }
}