<#
.SYNOPSIS
  Тест
#>

# Переход в папку скрипта (совместимая версия)
if ($PSScriptRoot) {
    $scriptDir = $PSScriptRoot
} else {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

newman run flowers-auth-tests.json
