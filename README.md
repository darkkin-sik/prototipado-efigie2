# Efigie

Aplicación web local para responder una encuesta, generar una efigie visual a partir de las respuestas y compartir una dinámica grupal en tiempo real.

## Funcionamiento general

1. **Encuesta:** cada participante completa el formulario de [index.html](./index.html). La respuesta se guarda en `respuestas_app.csv` y se le asigna un identificador para continuar en su dispositivo.
2. **Generación de la efigie:** el servidor usa las respuestas y la configuración para construir una ilustración SVG. La geometría se genera con Rhino 8.
3. **Lobby:** los participantes esperan en `/lobby/{id}`. El panel de administración está en `/admin-sesion`; muestra quiénes ingresaron y permite expulsar participantes antes de comenzar.
4. **Armado de equipos:** al comenzar, el servidor organiza 15 participantes en cinco equipos de tres. El panel permite iniciar una simulación con bots o completar lugares vacíos con bots.
5. **Pre-ronda:** cada participante marca que está listo. Cuando todos están listos, comienza la ronda 1.
6. **Ronda 1:** cada participante ve su efigie y las otras dos de su equipo, una pregunta disparadora y un temporizador de dos minutos. Las notas se guardan en el almacenamiento local del navegador, con un máximo de 20 caracteres por nota. Desde administración se pueden agregar 20 segundos al temporizador.

El estado de la sesión y el temporizador viven en el proceso del servidor; al reiniciar la aplicación, la sesión activa vuelve a empezar. Las respuestas de la encuesta se almacenan aparte en `respuestas_app.csv`.

## Requisitos

- Windows de 64 bits.
- Visual Studio 2022 con la carga de trabajo **Desarrollo de ASP.NET y web** (o **Desarrollo de escritorio de .NET**) y el SDK de .NET 8.
- Rhino 8 instalado en la ubicación esperada por el proyecto: `C:\Program Files\Rhino 8`. El servidor inicializa Rhino en segundo plano y carga sus ensamblados desde esa instalación.
- Para compartir el servidor fuera de la computadora local, una cuenta y el cliente de ngrok.

## Ejecutar desde Visual Studio

1. Abre `ServidorEfigie.csproj` en Visual Studio.
2. Si Rhino 8 está instalado en otra ubicación, actualiza las referencias `RhinoCommon` y `RhinoWindows` en el `.csproj` y las rutas de Rhino configuradas al inicio de `Program.cs`.
3. Selecciona la configuración **Debug** y la plataforma **x64**.
4. Ejecuta el proyecto con **F5** o **Ctrl+F5**. El servidor escucha en el puerto `5500` en todas las interfaces de red. Mantén abierta la consola mientras uses la aplicación.
5. En el navegador, abre:
   - `http://localhost:5500/encuesta` para completar la encuesta.
   - `http://localhost:5500/admin-sesion` para administrar el lobby y la ronda.

No uses únicamente la vista previa estática de Visual Studio: la aplicación necesita ejecutar el servidor ASP.NET Core y el motor de Rhino.

## Compartir mediante ngrok

ngrok no es necesario para usar la aplicación desde la misma computadora. Sí resulta práctico para que celulares u otros dispositivos, incluso fuera de la red local, puedan conectarse sin configurar el router.

1. Instala ngrok y configura tu token de autenticación siguiendo las instrucciones de tu cuenta.
2. Con el servidor ejecutándose en el puerto `5500`, abre otra terminal y ejecuta:

   ```powershell
   ngrok http 5500
   ```

3. Comparte la dirección HTTPS que muestra ngrok. Los participantes deben abrir esa dirección seguida de `/encuesta`; quien administre la sesión abre la misma dirección seguida de `/admin-sesion`.
4. Mantén abiertas tanto la aplicación como la terminal de ngrok durante la actividad. Al detener cualquiera de ellas, se interrumpe el acceso remoto.

## Archivos principales

- `Program.cs`: servidor, endpoints, persistencia de respuestas, lobby, equipos y estado de la ronda.
- `GeneradorEfigie.cs`: generación de las efigies SVG a partir de las respuestas.
- `index.html`: encuesta.
- `pre-ronda-1.html`: pantalla para indicar que se está listo.
- `ronda-en-curso.html`: interfaz de la ronda 1.
- `admin-sesion.html`: panel de administración.
- `respuestas_app.csv`: respuestas guardadas por la aplicación.
