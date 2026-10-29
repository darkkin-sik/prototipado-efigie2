# Efigie

Aplicación web local para responder una encuesta, generar una efigie visual a partir de las respuestas y compartir una dinámica grupal en tiempo real.

## Funcionamiento general

1. **Encuesta:** cada participante completa el formulario de [index.html](./index.html). La respuesta se guarda en `respuestas_app.csv` y se le asigna un identificador para continuar en su dispositivo.
2. **Generación de la efigie:** el servidor usa las respuestas y la configuración para construir una ilustración SVG. La geometría se genera con Rhino 8.
3. **Lobby:** los participantes esperan en `/lobby/{id}`. El panel de administración está en `/admin-sesion`; muestra quiénes ingresaron y permite expulsar participantes antes de comenzar.
4. **Armado de equipos:** al comenzar, el servidor organiza 15 participantes en cinco equipos de tres. El panel permite iniciar una simulación con bots o completar lugares vacíos con bots.
5. **Pre-ronda:** cada participante marca que está listo. Cuando todos están listos, comienza la ronda 1.
6. **Memoria (30 segundos):** cada participante ve su efigie y las otras dos de su equipo para memorizarlas.
7. **Adivinanza (2 minutos):** cada participante elige dos efigies de las demás personas de la sala. El puntaje individual suma 50 puntos por cada acierto y hasta 100 puntos adicionales según la rapidez de respuesta. El puntaje del equipo es el promedio de sus tres integrantes.
8. **Leaderboard:** al enviar la respuesta, cada participante pasa directamente a la tabla de equipos ordenada por puntaje. El botón «Estoy listo» se habilita al terminar los dos minutos; la ronda 2 empieza cuando todos los participantes reales están listos. Administración puede forzar el cierre de la adivinanza (simulando las respuestas de los bots) o el avance desde la leaderboard.
9. **Ronda 2:** cada participante observa por 10 segundos la efigie de cada uno de sus dos compañeros, ve la transición negra con «¡Algo cambió!» durante 5 segundos y luego identifica la capa modificada entre cuatro opciones. La modificación se vuelve a generar con Rhino y excluye el color. Cada acierto suma 100 puntos más hasta 100 por rapidez; las respuestas incorrectas o fuera de tiempo suman 0. Al terminar los dos desafíos aparece la tabla de resultados.

El panel de administración incluye accesos para enviar a un participante seleccionado a una etapa en su propio dispositivo. En el caso de bots, abre una vista de prueba en una pestaña aparte. Las vistas de prueba con `adminPreview=1` no cambian la etapa compartida ni marcan a nadie como listo; sus respuestas de ronda 2 se mantienen separadas del puntaje real. El botón «Forzar retroceso» también envía a los dispositivos la pantalla correspondiente al estado restablecido.

En la ronda 3, una etapa de notas de dos minutos precede a una etapa de tres minutos para relacionar las seis partes de la efigie con su significado. Cada acierto suma 100 puntos y se agregan hasta 100 puntos por rapidez; al terminar el tiempo se consideran las selecciones existentes. El leaderboard se actualiza mientras responden los participantes y habilita «Estoy listo para continuar» cuando termina la etapa. Al completarse el avance compartido se abre el borrador de la ronda 4. **La interfaz de la ronda 4 todavía está pendiente; por ahora es solo una pantalla de borrador.**

Las leaderboards comparten el estilo de `leaderboard.css`, distinguen el equipo propio, actualizan los puntajes periódicamente y animan el reordenamiento. En rondas 2 y 3, «Estoy listo para continuar» espera a todos los participantes reales antes de abrir la siguiente etapa; administración puede forzar ese avance desde su panel.

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
- `ronda-en-curso.html`: etapa de memoria de la ronda 1.
- `ronda-1-adivinar.html`: selección de las efigies de los compañeros.
- `ronda-1-leaderboard.html`: resultados y avance a la siguiente ronda.
- `ronda-2.html`: minijuego de diferencias de la ronda 2.
- `ronda-2-leaderboard.html`: resultados de la ronda 2.
- `leaderboard.css`: formato compartido de las tablas de resultados.
- `admin-navigation.js`: recepción de navegaciones enviadas desde administración.
- `ronda-3.html`: primera parte de la ronda 3, con notas y efigies del equipo.
- `ronda-3-identificacion.html`: identificación de las seis capas de la efigie.
- `ronda-3-leaderboard.html`: resultados animados de la ronda 3 y avance compartido.
- `ronda-4.html`: pantalla borrador de la ronda 4.
- `admin-sesion.html`: panel de administración.
- `respuestas_app.csv`: respuestas guardadas por la aplicación.
