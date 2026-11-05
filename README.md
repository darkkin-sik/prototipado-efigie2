# Efigie

Aplicación web local para responder una encuesta, generar una efigie visual a partir de las respuestas y compartir una dinámica grupal en tiempo real.

## Funcionamiento general

1. **Encuesta:** cada participante completa el formulario de [index.html](./index.html). La respuesta se guarda en `respuestas_app.csv` y se le asigna un identificador para continuar en su dispositivo.
2. **Generación de la efigie:** el servidor usa las respuestas y la configuración para construir una ilustración SVG. La geometría se genera con Rhino 8.
3. **Lobby:** hasta 15 participantes esperan en `/lobby/{id}`. El panel de administración está en `/admin-sesion`; muestra quiénes ingresaron y permite expulsar participantes antes de comenzar. El nombre `ESP` en la encuesta abre una sesión espectadora, sincronizada con la etapa global, que no ocupa cupo ni aparece en administración.
4. **Armado de equipos:** al comenzar, el servidor organiza a los participantes en equipos de tres priorizando la compatibilidad de sus respuestas. En la simulación con bots, agrupa primero a los dispositivos reales en los menos equipos posibles y optimiza su compatibilidad; los bots completan los lugares restantes.
5. **Pre-ronda:** los tres integrantes de cada equipo eligen un representante entre ellos. El representante queda asignado automáticamente cuando todos coinciden; administración decide cuándo comenzar la ronda 1.
6. **Memoria:** cada participante ve las efigies de su equipo, sin un temporizador visible. Administración decide cuándo avanzar.
7. **Adivinanza:** solo el representante responde desde su dispositivo; los otros dos integrantes ven «Mira la pantalla de tu representante». El representante elige las tres efigies de su equipo entre las de toda la sala. Cada acierto suma 50 puntos y el resultado se comparte con el equipo.
8. **Leaderboard:** al enviar la respuesta del representante, los integrantes del equipo pasan a la tabla ordenada por puntaje. «Estoy listo» registra el estado para continuar, pero no avanza la sesión; administración controla los cambios de etapa desde su panel.
9. **Ronda 2:** cada integrante identifica las capas de las efigies de su equipo desde su dispositivo. El representante controla el cambio de pregunta y solo puede avanzar cuando hay consenso. Si pide una pista, aparece como ventana emergente en el dispositivo de uno de los otros integrantes; usarla reduce a la mitad los puntos de esa pregunta.

**Problema conocido:** ESP todavía presenta fallas en algunos dispositivos Android; el acceso y la navegación del espectador no están confirmados como compatibles en esos dispositivos.

El panel de administración incluye accesos para enviar a un participante seleccionado a una etapa en su propio dispositivo y temporizadores orientativos para las partes con duración sugerida. En el caso de bots, abre una vista de prueba en una pestaña aparte. Las vistas de prueba con `adminPreview=1` no cambian la etapa compartida ni marcan a nadie como listo; sus respuestas de ronda 2 se mantienen separadas del puntaje real. «Forzar retroceso» devuelve a todos a la etapa anterior sin reiniciar toda la sesión. «Kill all active sessions» envía a los dispositivos activos a la pantalla de bienvenida anterior a la encuesta y conserva las respuestas almacenadas hasta que se envíe una nueva.

Cada equipo comparte el orden de sus seis preguntas de identificación y cada participante pasa individualmente a su leaderboard cuando todos los participantes reales de su equipo coinciden en las seis respuestas; los bots de ese equipo replican las respuestas. Los equipos completamente simulados reciben puntajes aleatorios. Cada acierto suma 100 puntos, o 50 si se usó la pista. Administración controla las transiciones compartidas y puede forzar el avance. **La interfaz de la ronda 3 todavía está pendiente; por ahora es solo una pantalla de borrador.**

Las leaderboards comparten el estilo de `leaderboard.css`, distinguen el equipo propio, actualizan los puntajes periódicamente y animan el reordenamiento solo cuando cambia la tabla. En rondas 2 y 3, «Estoy listo para continuar» registra el estado de cada participante; únicamente administración abre la siguiente etapa. Las pantallas no muestran temporizadores; el panel de administración puede mostrar tiempos orientativos y permite saltar las etapas temporizadas.

El estado de la sesión y su etapa viven en el proceso del servidor; al reiniciar la aplicación, la sesión activa vuelve a empezar. Las respuestas de la encuesta se almacenan aparte en `respuestas_app.csv`.

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
- `pre-ronda-1.html`: elección unánime del representante al comienzo de cada ronda.
- `ronda-en-curso.html`: etapa de memoria de la ronda 1.
- `ronda-1-adivinar.html`: selección de las efigies de los compañeros.
- `ronda-1-leaderboard.html`: resultados y avance a la siguiente ronda.
- `leaderboard.css`: formato compartido de las tablas de resultados.
- `admin-navigation.js`: recepción de navegaciones enviadas desde administración.
- `ronda-3.html`: vista de las tres efigies del equipo, accesible desde la pestaña lateral de la ronda 2.
- `ronda-3-identificacion.html`: interfaz de respuestas de la ronda 2.
- `ronda-3-leaderboard.html`: resultados animados de la ronda 2 y avance compartido.
- `ronda-4.html`: pantalla borrador de la ronda 3.
- `admin-sesion.html`: panel de administración.
- La sesión puede comenzar con entre 3 y 15 participantes, siempre que el total sea múltiplo de tres.
- En la ronda 2, cada integrante responde desde su dispositivo; el representante controla la navegación y el consenso habilita el avance.
- `respuestas_app.csv`: respuestas guardadas por la aplicación.
- `Program.cs` sirve `/ronda-2` desde `ronda-3-identificacion.html` y `/ronda-2-leaderboard` desde `ronda-3-leaderboard.html`.
