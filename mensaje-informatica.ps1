$ErrorActionPreference = "Stop"

# La ruta a esta direccion de Cloudflare identifica la interfaz de salida.
$rutaCloudflare = @(Find-NetRoute -RemoteIPAddress "198.41.192.77")
$direccion = @($rutaCloudflare | Where-Object { $_.CimClass.CimClassName -eq "MSFT_NetIPAddress" -and $_.AddressFamily -eq "IPv4" })
$ruta = @($rutaCloudflare | Where-Object { $_.CimClass.CimClassName -eq "MSFT_NetRoute" -and $_.AddressFamily -eq "IPv4" })

if ($direccion.Count -ne 1 -or $ruta.Count -ne 1 -or $direccion[0].InterfaceIndex -ne $ruta[0].InterfaceIndex) {
    throw "No se pudo determinar una interfaz IPv4 de salida. Conecta la red y vuelve a intentar."
}

$adaptador = Get-NetAdapter -InterfaceIndex $direccion[0].InterfaceIndex
if ($adaptador.Status -ne "Up" -or -not $adaptador.MacAddress) {
    throw "La interfaz de salida no esta activa o no tiene una direccion MAC disponible."
}

@"
Hola. Necesito usar Cloudflare Tunnel para publicar temporalmente un servidor local.
El programa usa HTTP/2 y necesita conexiones salientes TCP al puerto 7844 hacia
los servidores de Cloudflare Tunnel. No necesito conexiones entrantes.

Pueden revisar si la red permite ese trafico para mi equipo o indicarme una
alternativa autorizada?

Equipo: $env:COMPUTERNAME
Interfaz de salida: $($adaptador.Name) - $($adaptador.InterfaceDescription)
IP local actual: $($direccion[0].IPAddress)
MAC de la interfaz: $($adaptador.MacAddress)
Puerta de enlace actual: $($ruta[0].NextHop)

La IP puede cambiar al cambiar de aula o reconectarme. La interfaz mostrada es
la seleccionada por Windows hacia una direccion de Cloudflare en este momento.
"@
