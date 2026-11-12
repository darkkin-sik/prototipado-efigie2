$repoRedirect = Join-Path $env:USERPROFILE "QR-REDIRECT-EFIGIE"
$cloudflared = Join-Path $PSScriptRoot "cloudflared.exe.exe"
$logTunnel = "$env:TEMP\cloudflared_log.txt"

if (-not (Test-Path $cloudflared)) {
    throw "No se encontró cloudflared.exe.exe en la carpeta del proyecto."
}

if (Test-Path $logTunnel) { Remove-Item $logTunnel }

# 1. Iniciar backend con dotnet run
Write-Host "1/3 Iniciando servidor dotnet..." -ForegroundColor Cyan
$dotnetProcess = Start-Process dotnet -ArgumentList "run" -WorkingDirectory $PSScriptRoot -PassThru -NoNewWindow

# 2. Esperar a que el puerto 5500 este abierto (comprobacion TCP rapida sin Invoke-WebRequest)
Write-Host "Esperando inicio del puerto 5500 (Rhino 8 + Kestrel)..." -ForegroundColor DarkGray
$serverReady = $false
while (-not $serverReady) {
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect("127.0.0.1", 5500)
        if ($tcp.Connected) {
            $serverReady = $true
            $tcp.Close()
        }
    } catch {
        Start-Sleep -Seconds 1
    }
}
Write-Host "Servidor .NET activo en puerto 5500." -ForegroundColor Green

# 3. Iniciar el tunel de Cloudflare
Write-Host "2/3 Iniciando tunel de Cloudflare..." -ForegroundColor Cyan
$tunnelProcess = Start-Process $cloudflared -ArgumentList "tunnel --url http://localhost:5500" -RedirectStandardError $logTunnel -PassThru -NoNewWindow

$tunnelUrl = $null
while (-not $tunnelUrl) {
    Start-Sleep -Seconds 1
    if (Test-Path $logTunnel) {
        $content = Get-Content $logTunnel -Raw
        if ($content -match "https://[a-zA-Z0-9-]+\.trycloudflare\.com") {
            $tunnelUrl = $matches[0]
        }
    }
}

Write-Host "Tunel activo en: $tunnelUrl" -ForegroundColor Green

# 4. Actualizar GitHub Pages
Write-Host "3/3 Actualizando GitHub Pages..." -ForegroundColor Yellow
$htmlFile = Join-Path $repoRedirect "index.html"
(Get-Content $htmlFile) -replace 'const DESTINO = "https://.*";', "const DESTINO = `"$tunnelUrl`";" | Set-Content $htmlFile

Push-Location $repoRedirect
git commit -am "Auto-update tunnel: $tunnelUrl"
git push origin main
Pop-Location

Write-Host "`n========================================================" -ForegroundColor Magenta
Write-Host "TODO LISTO Y FUNCIONANDO:" -ForegroundColor Green
Write-Host "Acceso QR permanente: https://darkkin-sik.github.io/QR-REDIRECT-EFIGIE/" -ForegroundColor White
Write-Host "Presiona Ctrl + C en esta terminal para detener todo." -ForegroundColor DarkGray
Write-Host "========================================================`n" -ForegroundColor Magenta

try {
    Wait-Process -Id $tunnelProcess.Id
} finally {
    Write-Host "`nApagando procesos..." -ForegroundColor Red
    if ($tunnelProcess -and -not $tunnelProcess.HasExited) { Stop-Process -Id $tunnelProcess.Id -Force }
    if ($dotnetProcess -and -not $dotnetProcess.HasExited) { Stop-Process -Id $dotnetProcess.Id -Force }
}