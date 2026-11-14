$repoRedirect = Join-Path $env:USERPROFILE "QR-REDIRECT-EFIGIE"
$cloudflared = Join-Path $PSScriptRoot "cloudflared.exe.exe"
$logTunnel = "$env:TEMP\cloudflared_log.txt"
$destinationFile = Join-Path $repoRedirect "destino.txt"
$redirectHtml = Join-Path $repoRedirect "index.html"

if (-not (Test-Path $cloudflared)) {
    throw "No se encontró cloudflared.exe.exe en la carpeta del proyecto."
}

if (-not (Test-Path (Join-Path $repoRedirect ".git")) -or -not (Test-Path $destinationFile) -or -not (Test-Path $redirectHtml)) {
    throw "No se encontró una copia preparada de QR-REDIRECT-EFIGIE en $repoRedirect."
}

if (Test-Path $logTunnel) { Remove-Item $logTunnel }

# 1. Iniciar backend sin restaurar paquetes en cada arranque
Write-Host "1/3 Iniciando servidor dotnet..." -ForegroundColor Cyan
$dotnetProcess = Start-Process dotnet -ArgumentList "run --no-restore" -WorkingDirectory $PSScriptRoot -PassThru -NoNewWindow

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
$previousTransportProtocol = $env:TUNNEL_TRANSPORT_PROTOCOL
try {
    $env:TUNNEL_TRANSPORT_PROTOCOL = "http2"
    $tunnelProcess = Start-Process $cloudflared -ArgumentList "tunnel --url http://localhost:5500" -RedirectStandardError $logTunnel -PassThru -NoNewWindow
} finally {
    if ($null -eq $previousTransportProtocol) {
        Remove-Item Env:TUNNEL_TRANSPORT_PROTOCOL
    } else {
        $env:TUNNEL_TRANSPORT_PROTOCOL = $previousTransportProtocol
    }
}

$tunnelUrl = $null
$tunnelConnected = $false
$tunnelDeadline = (Get-Date).AddSeconds(60)
while ((-not $tunnelUrl -or -not $tunnelConnected) -and (Get-Date) -lt $tunnelDeadline) {
    Start-Sleep -Seconds 1
    if (Test-Path $logTunnel) {
        $content = Get-Content $logTunnel -Raw
        if (-not $tunnelUrl -and $content -match "https://[a-zA-Z0-9-]+\.trycloudflare\.com") {
            $tunnelUrl = $matches[0]
        }
        if ($content -match "Registered tunnel connection") {
            $tunnelConnected = $true
        }
    }

    if ($tunnelProcess.HasExited) { break }
}

if (-not $tunnelUrl -or -not $tunnelConnected) {
    $tunnelLogTail = if (Test-Path $logTunnel) { (Get-Content $logTunnel -Tail 10) -join "`n" } else { "No se generó el registro del túnel." }
    if ($tunnelProcess -and -not $tunnelProcess.HasExited) { Stop-Process -Id $tunnelProcess.Id -Force }
    if ($dotnetProcess -and -not $dotnetProcess.HasExited) { Stop-Process -Id $dotnetProcess.Id -Force }
    throw "Cloudflare no estableció la conexión HTTP/2 en 60 segundos. Revisa que la red permita tráfico TCP saliente por el puerto 7844.`n$tunnelLogTail"
}

Write-Host "Tunel activo en: $tunnelUrl" -ForegroundColor Green

# 4. Publicar el destino actualizado para el redirector
Write-Host "3/3 Publicando el destino actualizado..." -ForegroundColor Yellow
Push-Location $repoRedirect
try {
    Set-Content -Path $destinationFile -Value $tunnelUrl -Encoding Ascii -NoNewline

    git add -- index.html destino.txt worker.js README.md
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudieron preparar los archivos del redirector para Git."
    }

    git commit --only -m "Update active tunnel destination" -- index.html destino.txt worker.js README.md
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo crear el commit del destino actualizado."
    }

    git push origin main
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo subir el destino actualizado a GitHub."
    }

    $manifestUrl = "https://raw.githubusercontent.com/darkkin-sik/QR-REDIRECT-EFIGIE/main/destino.txt"
    $publishConfirmed = $false
    $lastPublishError = "GitHub todavía no refleja el destino nuevo."
    for ($attempt = 0; $attempt -lt 10 -and -not $publishConfirmed; $attempt++) {
        try {
            $cacheBust = [DateTime]::UtcNow.Ticks
            $publishedDestination = (Invoke-WebRequest -Uri "${manifestUrl}?cacheBust=$cacheBust" -UseBasicParsing -TimeoutSec 5).Content.Trim()
            $publishConfirmed = $publishedDestination -eq $tunnelUrl
            if (-not $publishConfirmed) {
                $lastPublishError = "GitHub todavía devuelve un destino anterior."
            }
        } catch {
            $lastPublishError = $_.Exception.Message
        }

        if (-not $publishConfirmed) {
            Start-Sleep -Seconds 1
        }
    }

    if (-not $publishConfirmed) {
        throw "Se subió el destino, pero no se pudo confirmar su publicación. $lastPublishError"
    }
} catch {
    if ($tunnelProcess -and -not $tunnelProcess.HasExited) { Stop-Process -Id $tunnelProcess.Id -Force }
    if ($dotnetProcess -and -not $dotnetProcess.HasExited) { Stop-Process -Id $dotnetProcess.Id -Force }
    throw
} finally {
    Pop-Location
}

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