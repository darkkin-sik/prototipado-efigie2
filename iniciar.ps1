$repoRedirect = Join-Path $env:USERPROFILE "QR-REDIRECT-EFIGIE"
$cloudflared = Join-Path $PSScriptRoot "cloudflared.exe.exe"
$logTunnel = "$env:TEMP\cloudflared_log.txt"
$logServerOutput = Join-Path $env:TEMP "efigie_servidor_stdout.log"
$logServerError = Join-Path $env:TEMP "efigie_servidor_stderr.log"
$destinationFile = Join-Path $repoRedirect "destino.txt"
$redirectHtml = Join-Path $repoRedirect "index.html"
$serverLogOffsets = @{}
$serverLogOffsets[$logServerOutput] = [long]0
$serverLogOffsets[$logServerError] = [long]0
$networkDisconnectReported = $false
$serverErrorReported = $false

function Read-NewServerLogText([string]$Path) {
    if (-not (Test-Path $Path)) { return "" }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $offset = [long]$script:serverLogOffsets[$Path]
        if ($stream.Length -lt $offset) { $offset = 0 }
        [void]$stream.Seek($offset, [System.IO.SeekOrigin]::Begin)
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8, $true, 1024, $true)
        try {
            $text = $reader.ReadToEnd()
        } finally {
            $reader.Dispose()
        }
        $script:serverLogOffsets[$Path] = $stream.Position
        return $text
    } finally {
        $stream.Dispose()
    }
}

function Check-ServerOutput {
    $newText = (Read-NewServerLogText $script:logServerOutput) + "`n" + (Read-NewServerLogText $script:logServerError)
    if (-not $script:networkDisconnectReported -and $newText -match "(?i)Unable to read data from the transport connection|SocketException \(10054\)|forcibly closed by the remote host") {
        Write-Host "Se interrumpio una conexion auxiliar de red. Detalles: $($script:logServerOutput) y $($script:logServerError)." -ForegroundColor Yellow
        $script:networkDisconnectReported = $true
    } elseif (-not $script:serverErrorReported -and $newText -match "(?i)\bFatal\b|Unhandled exception") {
        Write-Host "Se registró un error interno. Detalles: $($script:logServerOutput) y $($script:logServerError)." -ForegroundColor Yellow
        $script:serverErrorReported = $true
    }
}

function Stop-StartedServer {
    if (-not $script:dotnetProcess) { return }

    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($script:dotnetProcess.Id)")
    foreach ($child in $children) {
        if ($child.Name -eq "ServidorEfigie.exe" -and $child.ExecutablePath -and
            $child.ExecutablePath.StartsWith($script:serverBin, [System.StringComparison]::OrdinalIgnoreCase)) {
            Stop-Process -Id $child.ProcessId -Force -ErrorAction Stop
        }
    }
    if (-not $script:dotnetProcess.HasExited) {
        Stop-Process -Id $script:dotnetProcess.Id -Force -ErrorAction Stop
    }
}

function Get-PortListeners([int]$Port) {
    $connections = Get-CimInstance -Namespace "root\StandardCimv2" -ClassName "MSFT_NetTCPConnection" `
        -Filter "LocalPort = $Port AND RemotePort = 0" -ErrorAction Stop
    foreach ($connection in $connections) {
        $state = [int]$connection.CimInstanceProperties["State"].Value
        # Some Windows environments report state 0 even for a listening socket.
        if ($state -eq 2 -or $state -eq 0) { $connection }
    }
}

if (-not (Test-Path $cloudflared)) {
    throw "No se encontró cloudflared.exe.exe en la carpeta del proyecto."
}

if (-not (Test-Path (Join-Path $repoRedirect ".git")) -or -not (Test-Path $destinationFile) -or -not (Test-Path $redirectHtml)) {
    throw "No se encontró una copia preparada de QR-REDIRECT-EFIGIE en $repoRedirect."
}

$serverBin = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "bin")) + [System.IO.Path]::DirectorySeparatorChar
$listeners = @(Get-PortListeners 5500)
if ($listeners.Count -gt 0) {
    $oldServerIds = @()
    foreach ($listener in $listeners) {
        $owner = Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)"
        if (-not $owner -or $owner.Name -ne "ServidorEfigie.exe" -or
            -not $owner.ExecutablePath -or
            -not $owner.ExecutablePath.StartsWith($serverBin, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "El puerto 5500 esta ocupado por otro programa (PID $($listener.OwningProcess)). No se detuvo; libera el puerto y vuelve a intentar."
        }
        $oldServerIds += $owner.ProcessId
    }

    Write-Host "Cerrando la instancia anterior de Efigie para iniciar una nueva..." -ForegroundColor Yellow
    foreach ($serverId in ($oldServerIds | Select-Object -Unique)) {
        $oldServer = Get-Process -Id $serverId -ErrorAction Stop
        Stop-Process -InputObject $oldServer -Force -ErrorAction Stop
        if (-not $oldServer.WaitForExit(10000)) {
            throw "No se pudo cerrar la instancia anterior de Efigie (PID $serverId)."
        }
    }
}

if (Test-Path $logTunnel) { Remove-Item $logTunnel }

# 1. Iniciar backend sin restaurar paquetes en cada arranque
Write-Host "1/3 Iniciando servidor dotnet..." -ForegroundColor Cyan
$dotnetProcess = Start-Process dotnet -ArgumentList "run --no-restore" -WorkingDirectory $PSScriptRoot -PassThru -NoNewWindow -RedirectStandardOutput $logServerOutput -RedirectStandardError $logServerError

# 2. Esperar a que el puerto 5500 este abierto (comprobacion TCP rapida sin Invoke-WebRequest)
Write-Host "Esperando inicio del puerto 5500 (Rhino 8 + Kestrel)..." -ForegroundColor DarkGray
$serverReady = $false
while (-not $serverReady) {
    Check-ServerOutput
    if ($dotnetProcess.HasExited) {
        throw "El servidor .NET se cerró durante el arranque. Revisa los registros en $logServerOutput y $logServerError."
    }

    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect("127.0.0.1", 5500)
    } catch {
        Start-Sleep -Seconds 1
        continue
    } finally {
        if ($tcp) { $tcp.Close() }
    }

    $listeners = @(Get-PortListeners 5500)
    foreach ($listener in $listeners) {
        $owner = Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)"
        if ($owner -and $owner.ParentProcessId -eq $dotnetProcess.Id -and $owner.Name -eq "ServidorEfigie.exe" -and
            $owner.ExecutablePath -and $owner.ExecutablePath.StartsWith($serverBin, [System.StringComparison]::OrdinalIgnoreCase)) {
            $serverReady = $true
            break
        }
    }
    if (-not $serverReady -and $listeners.Count -gt 0) {
        Stop-StartedServer
        throw "El puerto 5500 esta ocupado por otro proceso; Efigie no se inicio. No se publico el QR."
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
    Check-ServerOutput
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
    Stop-StartedServer
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
    $lastPublishError = "GitHub aun no refleja el destino nuevo."
    for ($attempt = 0; $attempt -lt 10 -and -not $publishConfirmed; $attempt++) {
        Check-ServerOutput
        try {
            $cacheBust = [DateTime]::UtcNow.Ticks
            $publishedDestination = (Invoke-WebRequest -Uri "${manifestUrl}?cacheBust=$cacheBust" -UseBasicParsing -TimeoutSec 5).Content.Trim()
            $publishConfirmed = $publishedDestination -eq $tunnelUrl
            if (-not $publishConfirmed) {
                $lastPublishError = "GitHub devuelve un destino anterior."
            }
        } catch {
            $lastPublishError = $_.Exception.Message
        }

        if (-not $publishConfirmed) {
            Start-Sleep -Seconds 1
        }
    }

    if ($dotnetProcess.HasExited -or $tunnelProcess.HasExited) {
        throw "El servidor o el tunel se detuvo durante el inicio. Revisa los registros antes de volver a intentar."
    }
} catch {
    if ($tunnelProcess -and -not $tunnelProcess.HasExited) { Stop-Process -Id $tunnelProcess.Id -Force }
    Stop-StartedServer
    throw
} finally {
    Pop-Location
}

Write-Host "`n========================================================" -ForegroundColor Magenta
if ($publishConfirmed) {
    Write-Host "Destino nuevo confirmado en GitHub. Proba el QR en tu celular." -ForegroundColor Green
} else {
    Write-Host "No se pudo confirmar el destino en GitHub: $lastPublishError" -ForegroundColor Red
    Write-Host "El servidor y el tunel siguen activos. Proba el QR en tu celular; podria apuntar al destino anterior." -ForegroundColor Red
}
Write-Host "`nEnlaces permanentes para compartir:" -ForegroundColor Cyan
Write-Host "Encuesta para la experiencia: https://darkkin-sik.github.io/QR-REDIRECT-EFIGIE/" -ForegroundColor White
Write-Host "Hub: https://darkkin-sik.github.io/QR-REDIRECT-EFIGIE/?ruta=hub" -ForegroundColor White
Write-Host "Encuesta para agregar usuarios sin experiencia: https://darkkin-sik.github.io/QR-REDIRECT-EFIGIE/?ruta=comunidad" -ForegroundColor White
Write-Host "Acceso directo al tunel (temporal): $tunnelUrl" -ForegroundColor White
Write-Host "Presiona Ctrl + C en esta terminal para detener todo." -ForegroundColor DarkGray
Write-Host "========================================================`n" -ForegroundColor Magenta

try {
    while (-not $tunnelProcess.HasExited) {
        Check-ServerOutput
        if ($dotnetProcess.HasExited) {
            Write-Host "El servidor Efigie se detuvo. Revisa los registros en $logServerOutput y $logServerError." -ForegroundColor Red
            break
        }
        Start-Sleep -Seconds 1
    }
} finally {
    Write-Host "`nApagando procesos..." -ForegroundColor Red
    if ($tunnelProcess -and -not $tunnelProcess.HasExited) { Stop-Process -Id $tunnelProcess.Id -Force }
    Stop-StartedServer
}