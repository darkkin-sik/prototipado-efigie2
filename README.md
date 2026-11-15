# Efigie

Aplicación web local para responder una encuesta, generar una efigie visual a partir de las respuestas y compartir una dinámica grupal en tiempo real.

## Iniciar el proyecto y compartirlo

Abre PowerShell en la carpeta del proyecto y ejecuta:

```powershell
.\iniciar.ps1
```

El iniciador ejecuta `dotnet run --no-restore` para evitar restaurar paquetes por Internet en cada arranque, espera a que el servidor esté disponible en el puerto `5500`, inicia el Quick Tunnel de Cloudflare y publica la URL del túnel en `destino.txt` dentro de `C:\Users\<usuario>\QR-REDIRECT-EFIGIE`. El túnel usa HTTP/2 sobre TCP para evitar redes que bloquean QUIC/UDP. La red debe permitir conexiones salientes al puerto `7844`. Luego crea y sube un commit a `main` en ese repositorio; necesitas tener esa copia local y permisos de `git push` configurados. El redirector de Cloudflare Workers consulta ese archivo en cada acceso y redirige al túnel activo. La URL del túnel también se muestra en la terminal. Los Quick Tunnels son temporales y su dirección puede cambiar cada vez que se inicia el script.

**Enlace estático del QR:** [https://darkkin-sik.github.io/QR-REDIRECT-EFIGIE/](https://darkkin-sik.github.io/QR-REDIRECT-EFIGIE/). Usa esta dirección para el QR; la página redirige al Worker estable y este consulta el túnel activo. El enlace permanece igual mientras el sitio de GitHub Pages siga publicado en esa dirección.

**Enlace estático del hub:** [https://darkkin-sik.github.io/QR-REDIRECT-EFIGIE/?ruta=hub](https://darkkin-sik.github.io/QR-REDIRECT-EFIGIE/?ruta=hub). Es independiente del enlace general y abre una portada con una efigie aleatoria. «Ingresar» abre el login; el servidor verifica el nombre y la estación guardados en la encuesta. Tras dos intentos incorrectos, invita a conocer la experiencia en el cuarto piso del Moro. El acceso correcto abre la efigie personal, permite descargarla como JPG vertical y muestra sus capas.

Por ahora, el hub se ejecuta localmente en el mismo servidor que la encuesta. El enlace `?ruta=hub` sigue siendo independiente del QR general, pero requiere que la computadora y el túnel estén activos. Las pantallas usan endpoints locales y validan el acceso con `respuestas_app.csv`; las respuestas no se sincronizan con una base ni con una API en la nube.

### Preparar el hub local

El pool de 50 efigies se genera una sola vez en `hub-site/efigies` y se comparte entre la bienvenida de la encuesta y el hub. Para generarlo o renovarlo, ejecuta desde PowerShell:

```powershell
.\generar-efigies-hub.ps1
```

El diseño mantiene las pantallas separadas del almacenamiento mediante endpoints de la aplicación; si más adelante se necesita alojar el hub fuera de esta computadora, se podrá reemplazar esa capa sin cambiar el flujo visual del hub.

Mantén abierta la terminal del iniciador mientras uses Efigie. Para detener el servidor y el túnel, presiona **Ctrl+C** en esa terminal. No inicies otra instancia con `dotnet run` ni desde Visual Studio mientras el script esté activo. Si el puerto `5500` ya está ocupado, cierra normalmente la instancia anterior o libera el puerto antes de volver a ejecutar el iniciador.

La consola omite los registros HTTP repetitivos; conserva advertencias, errores, mensajes de inicio y un aviso por cada nuevo participante que se incorpora al lobby.

Direcciones locales:

- Bienvenida y animación: `http://localhost:5500/`
- Encuesta: `http://localhost:5500/encuesta`
- Hub: `http://localhost:5500/hub.html`
- Pantalla grande Pantuno: `http://localhost:5500/pantuno`
- Administración: `http://localhost:5500/admin-sesion`

En la encuesta, escribir **pantuno** como nombre y pulsar **Siguiente** también abre esa pantalla. Pantuno muestra la efigie más reciente, recorre en columnas las últimas 36 efigies y actualiza la galería cuando se generan nuevas. El pequeño control con el QR alterna la efigie destacada y el código QR local `qr.svg`; el control contiguo activa o desactiva pantalla completa.

## Requisitos

- Windows de 64 bits.
- Visual Studio 2022 con la carga de trabajo **Desarrollo de ASP.NET y web** (o **Desarrollo de escritorio de .NET**) y el SDK de .NET 8.
- Rhino 8 instalado en la ubicación esperada por el proyecto: `C:\Program Files\Rhino 8`. El servidor inicializa Rhino en segundo plano y carga sus ensamblados desde esa instalación.
- Para compartir el servidor fuera de la computadora local, el cliente `cloudflared`. Los Quick Tunnels no requieren cuenta ni dominio de Cloudflare.

## Ejecutar desde Visual Studio

1. Abre `ServidorEfigie.csproj` en Visual Studio.
2. Si Rhino 8 está instalado en otra ubicación, actualiza las referencias `RhinoCommon` y `RhinoWindows` en el `.csproj` y las rutas de Rhino configuradas al inicio de `Program.cs`.
3. Selecciona la configuración **Debug** y la plataforma **x64**.
4. Ejecuta el proyecto con **F5** o **Ctrl+F5**. El servidor escucha en el puerto `5500` en todas las interfaces de red. Mantén abierta la consola mientras uses la aplicación.
5. En el navegador, abre:
   - `http://localhost:5500/encuesta` para completar la encuesta.
   - `http://localhost:5500/admin-sesion` para administrar el lobby y la ronda.

No uses únicamente la vista previa estática de Visual Studio: la aplicación necesita ejecutar el servidor ASP.NET Core y el motor de Rhino.

## Exportar 30 efigies a SVG

Para generar una lámina SVG local sin iniciar el servidor, abre PowerShell en la carpeta del proyecto y ejecuta:

```powershell
.\descargar-efigies.ps1
```

El script compila y ejecuta el generador local usando Rhino 8 y las primeras 30 respuestas de `respuestas_app.csv`. Guarda `efigies-30.svg` en la carpeta del proyecto. Cada efigie queda agrupada por separado con sus capas y nombre; conserva los trazos y colores generados para la web. Se requieren los mismos requisitos de .NET 8 y Rhino 8 indicados arriba.

## Archivos principales

- `Program.cs`: servidor, endpoints, persistencia de respuestas, lobby, equipos y estado de la ronda.
- `hub-site/`: pantallas estáticas del hub y el pool local de 50 efigies.
- `generar-efigies-hub.ps1`: generación del pool de efigies aleatorias.
- `GeneradorEfigie.cs`: generación de las efigies SVG a partir de las respuestas.
- `descargar-efigies.ps1`: exportación local de una lámina SVG con 30 efigies.
- `index.html`: encuesta.
- `pantuno.html`: pantalla grande con galería animada y acceso al QR de la actividad.
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
- `respuestas_app.csv`: respuestas guardadas por la aplicación.

## Orden de juego y objetivos

1. **Encuesta y efigie:** cada participante completa [index.html](./index.html). El servidor guarda las respuestas en `respuestas_app.csv` y genera una efigie visual a partir de ellas.
2. **Lobby y equipos:** los participantes se reúnen en `/lobby/{id}`. La sesión admite de 3 a 15 jugadores en múltiplos de tres. Los equipos se forman buscando diferencias útiles entre las respuestas, para que las efigies y sus capas puedan distinguirse durante el juego. En una simulación, se agrupan primero los dispositivos reales en la menor cantidad posible de equipos y los bots completan los lugares restantes priorizando la compatibilidad de esos grupos.
3. **Elección del representante:** antes de cada ronda, los tres integrantes votan desde sus dispositivos. El representante se asigna al coincidir los tres votos. Si cambian de opinión, pueden volver a votar hasta que cierre la elección.
4. **Ronda 1 — recordar y reconocer:** primero, todos observan las efigies de su equipo durante la etapa de memoria. Luego, solo el representante responde desde su dispositivo y selecciona las tres efigies de su equipo entre las de toda la sala; los otros dos integrantes ven «Mira la pantalla de tu representante». Cada acierto suma 50 puntos.
5. **Leaderboard de la Ronda 1:** se muestran los resultados y puntajes de los equipos. Cada participante confirma cuándo está listo para continuar; administración también puede forzar el avance.
6. **Elección del representante de la Ronda 2:** el equipo vuelve a elegir por unanimidad quién coordinará esta ronda.
7. **Ronda 2 — interpretar las capas:** cada integrante identifica las seis capas de la efigie desde su propio dispositivo. El representante controla el cambio de pregunta; puede avanzar cuando los integrantes reales del equipo coinciden en la respuesta. También puede volver a la pregunta anterior sin esperar consenso. Si solicita una pista, esta aparece en un dispositivo real de otro integrante y la pregunta vale la mitad de puntos. Cada acierto suma 100 puntos, o 50 si se usó la pista.
8. **Leaderboard de la Ronda 2:** se presentan los puntajes. Cuando todos confirman que están listos, o administración fuerza el avance, todos pasan a la pantalla de preronda de la Ronda 3.
9. **Ronda 3 — encontrar personas:** los tres integrantes juegan simultáneamente desde sus dispositivos, sin representante. Cada uno resuelve tres consignas buscando participantes que coincidan con características de la encuesta (año de nacimiento, facultad, etapa o rol, estación favorita, forma de trabajo, formato de historia o rol grupal). Las opciones muestran a todos los integrantes de la sala, incluidos los simulados; las consignas se generan a partir de respuestas existentes de participantes reales y priorizan personas de otros equipos. La misma persona puede servir para más de una consigna. Al completar las tres, aparece «Enviar» y se asignan puntos por orden global de finalización: 250, 200 y 150 a los tres primeros; desde el cuarto lugar, 50 puntos, restando 5 por cada posición posterior hasta llegar a cero. Solo los jugadores reales envían desafíos y suman puntos; cada puntaje individual se agrega al total de su equipo.
10. **Leaderboard final:** se actualiza a medida que terminan los jugadores y muestra el acumulado de las tres rondas. Combina el puntaje de la Ronda 1, el promedio por equipo de la Ronda 2 y la suma de los puntos individuales reales de la Ronda 3. Al completar el desafío todos los participantes, la sesión pasa a resultados finales.

## Administración y soporte

El panel de administración está en `/admin-sesion`. Permite observar el lobby, expulsar participantes antes de iniciar, comenzar o simular una sesión, inspeccionar etapas y controlar avances. Las vistas de prueba con `adminPreview=1` no alteran la etapa compartida ni los puntajes reales. «Forzar retroceso» vuelve a la etapa anterior; «Kill all active sessions» devuelve los dispositivos activos a la pantalla de bienvenida anterior a la encuesta.

Las leaderboards comparten el estilo de `leaderboard.css`, resaltan el equipo propio y actualizan los puntajes en tiempo real. Los temporizadores del panel son orientativos; el avance entre etapas depende de las reglas de cada ronda y administración puede forzarlo.

### Experiencia guiada por audio

Los archivos de audio se sirven desde la carpeta del proyecto y se reproducen en la computadora que tiene abierto `/admin-sesion`; conectá esa computadora al sistema de sonido del espacio. Un colaborador presenta la bienvenida y explica el QR en persona. Cuando termina, administración pulsa **Iniciar experiencia** y comienza Audio 2. El control de audio permite reproducir o pausar y ajustar el volumen; al pausar, también se detienen los temporizadores que dependen de esa pista. El volumen queda guardado en el navegador de administración.

Los archivos actuales `audio 2.ogg` a `audio 9.ogg` son borradores de referencia y serán reemplazados por las grabaciones correctas.

El orden y los avances son:

1. Audio 2 comienza al iniciar la experiencia después de la bienvenida presencial. La selección de representantes y el paso a memoria siguen bajo control de administración. No se reproducen pistas de bienvenida ni de explicación del QR; Pantuno conserva su control visual para mostrar el código QR.
2. Al terminar Audio 3 empieza una espera de 30 segundos y luego comienza Audio 4. El límite de dos minutos de la adivinanza empieza al comenzar Audio 4; administración puede agregar 20 segundos. Al finalizar el tiempo se pasa al leaderboard de la Ronda 1.
3. Administración inicia la elección de representantes de la Ronda 2 y luego Audio 5. Al avanzar, comienza Audio 6; la identificación queda bloqueada hasta que termina. Después hay una espera de 2 minutos y 30 segundos antes del leaderboard de la Ronda 2.
4. Al pasar del leaderboard de la Ronda 2, todos llegan a la pantalla de preronda de la Ronda 3 y comienza Audio 7. Esa pantalla espera el final de la pista; entonces se abre el desafío y su límite de 10 minutos. Audio 8 se reproduce al cumplirse 8 minutos si el desafío sigue activo; Audio 9 comienza cuando todos terminan o al vencer el límite.

Si se recarga el panel mientras hay una pista activa, el servidor la pausa para evitar que el audio y los temporizadores queden desincronizados. El operador puede reanudarla desde el control de audio.

El estado de la sesión y su etapa viven en el proceso del servidor; al reiniciar la aplicación, la sesión activa vuelve a empezar. Las respuestas de la encuesta se almacenan aparte en `respuestas_app.csv`.

**Problema conocido:** las sesiones espectadoras (`ESP`) y su navegación todavía pueden fallar en algunos dispositivos Android.

## Cambios futuros y necesarios

- **Evaluar rondas adicionales:** decidir si la experiencia termina con estas tres rondas o si necesita más.
- **Pulir detalles de interfaz:** revisar las pantallas en distintos tamaños de celular, legibilidad, márgenes seguros, estados de espera y error, accesibilidad y consistencia visual entre rondas, leaderboards y administración.
- **Mejorar el sistema de enlace:** simplificar cómo se comparte y abre la experiencia en los celulares; revisar enlaces de ingreso, recuperación al reconectar, persistencia/cambio de dispositivo y el acceso de espectadores. Evaluar QR o códigos de sesión y validar el flujo con Cloudflare Tunnel y distintos navegadores móviles.
