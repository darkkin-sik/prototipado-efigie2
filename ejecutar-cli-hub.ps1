param(
    [Parameter(Mandatory = $true)]
    [string]$Modo,

    [Parameter(Mandatory = $true)]
    [string]$PatronExito,

    [Parameter(Mandatory = $true)]
    [int]$TiempoLimiteMinutos
)

$ErrorActionPreference = "Stop"
$buildOutput = Join-Path $env:TEMP "EfigieHubBuild\bin\"
$assemblyPath = Join-Path $buildOutput "Debug\net8.0-windows\ServidorEfigie.dll"
$processId = [Guid]::NewGuid().ToString("N")
$stdoutPath = Join-Path $env:TEMP "EfigieHub-$processId.out"
$stderrPath = Join-Path $env:TEMP "EfigieHub-$processId.err"

function Stop-HubCliProcess {
    param(
        [int]$Id,
        [string]$Modo
    )

    $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $Id" -ErrorAction SilentlyContinue
    if (-not $processInfo) {
        return
    }
    if ($processInfo.Name -ne "dotnet.exe" -or $processInfo.CommandLine -notlike "*$Modo*") {
        throw "El proceso $Id ya no corresponde al comando del hub que se inició."
    }

    $procesoEnEjecucion = Get-Process -Id $Id -ErrorAction SilentlyContinue
    if ($procesoEnEjecucion) {
        Stop-Process -Id $Id -Force
        $procesoEnEjecucion = Get-Process -Id $Id -ErrorAction SilentlyContinue
        if ($procesoEnEjecucion) {
            $procesoEnEjecucion.Refresh()
            if (-not $procesoEnEjecucion.HasExited) {
                $procesoEnEjecucion.Kill()
                [void]$procesoEnEjecucion.WaitForExit(5000)
            }
        }
    }

    $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $Id" -ErrorAction SilentlyContinue
    if ($processInfo -and $processInfo.Name -eq "dotnet.exe" -and $processInfo.CommandLine -like "*$Modo*") {
        $termination = Invoke-CimMethod -InputObject $processInfo -MethodName Terminate
        if ($termination.ReturnValue -ne 0) {
            throw "No se pudo detener el proceso CLI del hub (código $($termination.ReturnValue))."
        }
    }

    $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $Id" -ErrorAction SilentlyContinue
    if ($processInfo -and $processInfo.Name -eq "dotnet.exe" -and $processInfo.CommandLine -like "*$Modo*") {
        throw "El proceso CLI del hub $Id sigue activo."
    }
}

Push-Location $PSScriptRoot
try {
    dotnet build .\ServidorEfigie.csproj --no-restore "-p:BaseOutputPath=$buildOutput"
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo compilar el proyecto para la operación del hub."
    }

    if (-not (Test-Path $assemblyPath)) {
        throw "No se encontró el ensamblado compilado en $assemblyPath."
    }

    $argumentos = @("`"$assemblyPath`"", $Modo)
    $proceso = Start-Process -FilePath "dotnet" -ArgumentList $argumentos -PassThru `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $limite = (Get-Date).AddMinutes($TiempoLimiteMinutos)
    $operacionCompleta = $false

    while ((Get-Date) -lt $limite) {
        $proceso.Refresh()
        $salida = if (Test-Path $stdoutPath) { Get-Content -Path $stdoutPath -Raw } else { "" }

        if ($salida -match $PatronExito) {
            $operacionCompleta = $true
            break
        }
        if ($proceso.HasExited) {
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not $operacionCompleta) {
        $proceso.Refresh()
        $salidaError = if (Test-Path $stderrPath) { Get-Content -Path $stderrPath -Raw } else { "" }
        if (-not $proceso.HasExited) {
            Stop-HubCliProcess -Id $proceso.Id -Modo $Modo
            throw "La operación del hub excedió el límite de $TiempoLimiteMinutos minutos."
        }
        throw "La operación del hub terminó con código $($proceso.ExitCode). $salidaError"
    }

    Stop-HubCliProcess -Id $proceso.Id -Modo $Modo

    Write-Host $salida.TrimEnd()
} finally {
    Pop-Location
    Remove-Item -LiteralPath $stdoutPath, $stderrPath -ErrorAction SilentlyContinue
}
