# Efigie

Aplicación web local para responder una encuesta, generar una efigie visual a partir de las respuestas y compartir una dinámica grupal en tiempo real.

## Orden de juego y objetivos

1. **Encuesta y efigie:** cada participante completa [index.html](./index.html). El servidor guarda las respuestas en `respuestas_app.csv` y genera una efigie visual a partir de ellas.
2. **Lobby y equipos:** los participantes se reúnen en `/lobby/{id}`. La sesión admite de 3 a 15 jugadores en múltiplos de tres. Los equipos se forman buscando diferencias útiles entre las respuestas, para que las efigies y sus capas puedan distinguirse durante el juego. En una simulación, se agrupan primero los dispositivos reales en la menor cantidad posible de equipos y los bots completan los lugares restantes priorizando la compatibilidad de esos grupos.
3. **Elección del representante:** antes de cada ronda, los tres integrantes votan desde sus dispositivos. El representante se asigna al coincidir los tres votos. Si cambian de opinión, pueden volver a votar hasta que cierre la elección.
4. **Ronda 1 — recordar y reconocer:** primero, todos observan las efigies de su equipo durante la etapa de memoria. Luego, solo el representante responde desde su dispositivo y selecciona las tres efigies de su equipo entre las de toda la sala; los otros dos integrantes ven «Mira la pantalla de tu representante». Cada acierto suma 50 puntos.
5. **Leaderboard de la Ronda 1:** se muestran los resultados y puntajes de los equipos. Cada participante confirma cuándo está listo para continuar; administración también puede forzar el avance.
6. **Elección del representante de la Ronda 2:** el equipo vuelve a elegir por unanimidad quién coordinará esta ronda.
7. **Ronda 2 — interpretar las capas:** cada integrante identifica las seis capas de la efigie desde su propio dispositivo. El representante controla el cambio de pregunta; puede avanzar cuando los integrantes reales del equipo coinciden en la respuesta. También puede volver a la pregunta anterior sin esperar consenso. Si solicita una pista, esta aparece en un dispositivo real de otro integrante y la pregunta vale la mitad de puntos. Cada acierto suma 100 puntos, o 50 si se usó la pista.
8. **Leaderboard de la Ronda 2:** se presentan los puntajes. Al estar todos listos, comienza automáticamente la Ronda 3; administración puede forzar el avance.
9. **Ronda 3 — encontrar personas:** los tres integrantes juegan simultáneamente desde sus dispositivos, sin representante. Cada uno resuelve tres consignas buscando participantes que coincidan con características de la encuesta (año de nacimiento, facultad, etapa o rol, estación favorita, forma de trabajo, formato de historia o rol grupal). Las opciones muestran a todos los integrantes de la sala, incluidos los simulados; las consignas se generan a partir de respuestas existentes de participantes reales y priorizan personas de otros equipos. La misma persona puede servir para más de una consigna. Al completar las tres, aparece «Enviar» y se asignan puntos por orden global de finalización: 250, 200 y 150 a los tres primeros; desde el cuarto lugar, 50 puntos, restando 5 por cada posición posterior hasta llegar a cero. Solo los jugadores reales envían desafíos y suman puntos; cada puntaje individual se agrega al total de su equipo.
10. **Leaderboard final:** se actualiza a medida que terminan los jugadores y muestra el acumulado de las tres rondas. Combina el puntaje de la Ronda 1, el promedio por equipo de la Ronda 2 y la suma de los puntos individuales reales de la Ronda 3. Al completar el desafío todos los participantes, la sesión pasa a resultados finales.

## Administración y soporte

El panel de administración está en `/admin-sesion`. Permite observar el lobby, expulsar participantes antes de iniciar, comenzar o simular una sesión, inspeccionar etapas y controlar avances. Las vistas de prueba con `adminPreview=1` no alteran la etapa compartida ni los puntajes reales. «Forzar retroceso» vuelve a la etapa anterior; «Kill all active sessions» devuelve los dispositivos activos a la pantalla de bienvenida anterior a la encuesta.

Las leaderboards comparten el estilo de `leaderboard.css`, resaltan el equipo propio y actualizan los puntajes en tiempo real. Los temporizadores del panel son orientativos; el avance entre etapas depende de las reglas de cada ronda y administración puede forzarlo.

**Problema conocido:** las sesiones espectadoras (`ESP`) y su navegación todavía pueden fallar en algunos dispositivos Android.

## Cambios futuros y necesarios

- **Evaluar rondas adicionales:** decidir si la experiencia termina con estas tres rondas o si necesita más.
- **Pulir detalles de interfaz:** revisar las pantallas en distintos tamaños de celular, legibilidad, márgenes seguros, estados de espera y error, accesibilidad y consistencia visual entre rondas, leaderboards y administración.
- **Mejorar el sistema de enlace:** simplificar cómo se comparte y abre la experiencia en los celulares; revisar enlaces de ingreso, recuperación al reconectar, persistencia/cambio de dispositivo y el acceso de espectadores. Evaluar QR o códigos de sesión y validar el flujo con ngrok y distintos navegadores móviles.

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
- `ronda-4.html`: interfaz de desafíos de la Ronda 3.
- `ronda-3-final-leaderboard.html`: leaderboard final con puntajes acumulados.
- `admin-sesion.html`: panel de administración.
- La sesión puede comenzar con entre 3 y 15 participantes, siempre que el total sea múltiplo de tres.
- En la ronda 2, cada integrante responde desde su dispositivo; el representante controla la navegación y el consenso habilita el avance.
- `respuestas_app.csv`: respuestas guardadas por la aplicación.
- `Program.cs` sirve `/ronda-2` desde `ronda-3-identificacion.html`, `/ronda-2-leaderboard` desde `ronda-3-leaderboard.html`, `/ronda-3` desde `ronda-4.html` y `/ronda-3-leaderboard` desde `ronda-3-final-leaderboard.html`.
