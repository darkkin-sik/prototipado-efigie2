$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "ejecutar-cli-hub.ps1") `
    -Modo "--generar-efigies-hub" `
    -PatronExito "Pool de 50 efigies generado en " `
    -TiempoLimiteMinutos 10

$cantidadEfigies = (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "hub-site\efigies") -Filter "*.svg" | Measure-Object).Count
if ($cantidadEfigies -ne 50) {
    throw "Se esperaban exactamente 50 efigies SVG y se encontraron $cantidadEfigies."
}
Write-Host "Pool verificado: $cantidadEfigies efigies SVG."
