# Efigie en Cloudflare

Versión completa de Efigie (encuesta, lobby, las tres rondas, leaderboards y administración) que corre en Mac
y en Cloudflare, gratis y sin tarjeta, sin C#, Windows ni Rhino. Las URLs son las mismas que en `../Program.cs`.

## Jugar en local (Mac)

```bash
cd cloudflare
npm install
npm run db:migrar:local        # solo la primera vez: crea las tablas en la D1 local
npm run db:importar-csv        # opcional: importa ../respuestas_app.csv
npm run dev
```

| Quién | URL |
|---|---|
| Jugador | http://localhost:8787/ (o `/encuesta`) |
| Admin de sesión | http://localhost:8787/admin-sesion (o escribir "admin" como nombre en la encuesta) — pide contraseña |
| Espectador | http://localhost:8787/espectador (o "ESP" como nombre) |
| Ajustes de la efigie | http://localhost:8787/admin-efigies/ — pide contraseña |

**Contraseña de admin:** en local es la de `.dev.vars` (`ADMIN_KEY=efigie-local`; cambiala ahí). Publicado, es el
secreto `ADMIN_KEY` de Cloudflare. Una vez ingresada, queda recordada 12 horas en ese navegador. Protege
`/admin-sesion`, `/admin-efigies` y todas las acciones `/api/admin/*` (por HTTP y por WebSocket); los jugadores no la necesitan.

Cada jugador se identifica con una cookie y con `localStorage`. Para simular varios jugadores en la misma
computadora, usá **ventanas privadas distintas** o navegadores distintos. Para probar desde el celular en la
misma red Wi-Fi: `npm run dev -- --ip 0.0.0.0` y abrí `http://<ip-de-tu-mac>:8787`.

Para completar la sala con bots, usá "Simular" en `/admin-sesion`.

## Cómo está armado

| Archivo | Qué es |
|---|---|
| `worker/index.js` | Worker: rutas de páginas, respuestas y config en D1, y reenvío de `/api/*` a la sesión. |
| `worker/sesion.js` | Durable Object `SesionEfigie`: una sesión de juego con estado persistente y WebSocket. |
| `worker/juego.js` | Port de la lógica de `Program.cs` (lobby, equipos, rondas, puntajes, admin). Sin Cloudflare: se prueba en Node. |
| `worker/datos.js` | Consultas a D1. |
| `worker/admin.js` | Contraseña de administración (cookie de 12 h con un hash de `ADMIN_KEY`). |
| `public/juego/` | Las páginas HTML del juego (copiadas de `..`, con `transporte.js` agregado). `bienvenida`, `lobby` y `efigie` antes las generaba C#. |
| `public/lib/transporte.js` | Manda las llamadas `fetch('/api/...')` de las páginas por un único WebSocket (si falla, usa HTTP). |
| `public/lib/efigie/` | Generador de la efigie en JS (port de `GeneradorEfigie.cs`), usado por el servidor y el navegador. |
| `public/admin-efigies/` | Vista con p5.js para ajustar los parámetros de la efigie. |
| `rhino/` | Script para Rhino 8 (Mac) con el generador original, como referencia. |

**Por qué WebSocket:** las páginas consultan al servidor cada 1 o 2 segundos. Por HTTP, 15 celulares serían
unas 150.000 solicitudes por hora, y el plan gratuito permite 100.000 por día. Por el WebSocket esas consultas no
cuentan como solicitudes HTTP. Las páginas no cambiaron: `transporte.js` intercepta `fetch`.

**Estado:** la sesión vive en el Durable Object y se guarda cada 3 segundos (y enseguida tras cada acción de
admin), así que sobrevive a reinicios. "Kill all active sessions" en el admin la vacía.

## Pruebas

```bash
npm test                         # partida completa simulada contra worker/juego.js (sin servidor)
npm run dev                      # en otra terminal…
npm run probar-partida           # …juega una partida completa contra el servidor por WebSocket
```

`probar-partida` usa las 3 primeras respuestas de la base y termina con "terminar todas".

## Diferencias con el servidor C#

- **Leaderboard final:** `Program.cs` sumaba los puntos de la ronda 2 antigua (`PuntajesRondaDos`, que ya no se
  juega), por lo que la columna "Ronda 2" quedaba en 0. Ahora suma la ronda 2 actual (identificación de capas),
  como describe el README del proyecto.
- Las efigies se generan con el port en JS (ver "Comparar con Rhino"). Los números del SVG se redondean a 3
  decimales sin ceros de relleno.
- La administración pide contraseña (en C# estaba abierta).
- `ronda-2.html` y `ronda-2-leaderboard.html` no se copiaron: `Program.cs` no tenía rutas para ellas.

## Ajustes de la efigie (`/admin-efigies/`)

- sliders para cada parámetro de `ConfigEfigie`, que redibujan todas las efigies en vivo;
- capas activables, "colorear por capa" y "animar rotación";
- **Guardar config** crea una versión nueva (con nota); el juego la usa a los pocos segundos;
- el **Historial** permite volver a una versión anterior; se puede exportar el SVG o `config.json`.

Para modificar la geometría de la efigie, editá `public/lib/efigie/geometria.js`.

## Comparar con Rhino 8 en Mac

1. `npm run rhino:armar` genera `rhino/EfigieRhino.cs` (el script + el `GeneradorEfigie.cs` original).
2. En Rhino 8: `ScriptEditor` → nuevo script C# → pegar `rhino/EfigieRhino.cs` → Run.
   Cuando lo pida, ingresá la ruta de `respuestas_app.csv` (o la URL `https://.../api/datos`).
3. El script dibuja las efigies (capas `Efigie::<capa>`) y guarda SVG de referencia en `cloudflare/rhino/referencias/`.
4. `npm run comparar` y abrir `comparacion.html`.

## Publicar en Cloudflare

La guía completa (cuenta, subdominio, base de datos, contraseña de admin, actualización y problemas comunes)
está en **[PUBLICAR.md](./PUBLICAR.md)**. Resumen de la primera vez:

```bash
npx wrangler login
npx wrangler d1 create efigie          # dejar una sola entrada "DB" en wrangler.jsonc con ese database_id
npm run db:migrar:remoto
npm run db:importar-csv -- --remoto    # opcional
npm run deploy                         # → https://efigie.<tu-subdominio>.workers.dev
npx wrangler secret put ADMIN_KEY      # contraseña de /admin-sesion y /admin-efigies
```

Antes del primer deploy hay que abrir **Workers & Pages** en el panel de Cloudflare una vez para crear el
subdominio `workers.dev`. Para actualizar después, alcanza con `npm run deploy`.
