$ErrorActionPreference = "Stop"

$proyecto = Join-Path $PSScriptRoot "ServidorEfigie.csproj"
$ejecutable = Join-Path $PSScriptRoot "bin\Debug\net8.0-windows\ServidorEfigie.exe"
$csv = Join-Path $PSScriptRoot "respuestas_app.csv"
$salida = Join-Path $PSScriptRoot "efigies-30.svg"

if (-not (Test-Path $csv)) {
    throw "No se encontró respuestas_app.csv en la carpeta del proyecto."
}

Push-Location $PSScriptRoot
$proceso = $null
try {
    Write-Host "Compilando el generador local..." -ForegroundColor Cyan
    & dotnet build $proyecto --configuration Debug
    if ($LASTEXITCODE -ne 0) {
        throw "Falló la compilación del generador."
    }

    if (-not (Test-Path $ejecutable)) {
        throw "No se encontró el ejecutable compilado: $ejecutable"
    }

    if (Test-Path $salida) {
        Remove-Item $salida
    }

    Write-Host "Generando la lámina SVG localmente..." -ForegroundColor Cyan
    $inicio = [DateTime]::UtcNow
    $proceso = Start-Process $ejecutable -ArgumentList "--exportar-efigies-svg" -WorkingDirectory $PSScriptRoot -NoNewWindow -PassThru
    $limite = [DateTime]::UtcNow.AddMinutes(10)

    while ([DateTime]::UtcNow -lt $limite) {
        $proceso.Refresh()
        if (Test-Path $salida) {
            $archivo = Get-Item $salida
            if ($archivo.Length -gt 0 -and $archivo.LastWriteTimeUtc -ge $inicio) {
                Start-Sleep -Seconds 2
                $archivo = Get-Item $salida
                if ($archivo.Length -gt 0 -and $archivo.LastWriteTimeUtc -ge $inicio) {
                    break
                }
            }
        }

        if ($proceso.HasExited) {
            throw "El generador terminó sin crear $salida (código $($proceso.ExitCode))."
        }
        Start-Sleep -Seconds 1
    }

    if (-not (Test-Path $salida)) {
        throw "Se agotó el tiempo de espera al generar el SVG."
    }

    [xml]$documento = Get-Content $salida -Raw
    $efigies = $documento.SelectNodes("//*[local-name()='g' and starts-with(@id, 'efigie-')]")
    if ($efigies.Count -ne 30) {
        throw "El archivo generado contiene $($efigies.Count) efigies en vez de 30."
    }

    if ($proceso -and -not $proceso.HasExited) {
        Stop-Process -Id $proceso.Id -Force
    }

    Write-Host "Archivo listo: $salida (30 efigies)" -ForegroundColor Green
} finally {
    if ($proceso -and -not $proceso.HasExited) {
        Stop-Process -Id $proceso.Id -Force
    }
    Pop-Location
}
