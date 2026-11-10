# Publicar Efigie en Cloudflare

Guía paso a paso para publicar el juego por primera vez, configurar la contraseña de administración y
actualizarlo después. Todo es gratis y no pide tarjeta. No hace falta saber programar: alcanza con copiar
y pegar los comandos en el orden indicado.

---

## Antes de empezar

### Qué vas a usar

| Herramienta | Qué es | Para qué la usamos |
|---|---|---|
| **Terminal** | Una ventana donde se escriben instrucciones de texto en lugar de hacer clic. | Para ejecutar todos los comandos de esta guía. |
| **Node.js** | Un programa que permite ejecutar código JavaScript en tu computadora (no solo en el navegador). | El juego está escrito en JavaScript; Node.js lo hace funcionar en tu compu. |
| **npm** | El "instalador de paquetes" de Node.js. Se instala junto con Node.js. | Descarga las herramientas que necesita el proyecto y ejecuta atajos como `npm run dev`. |
| **Wrangler** | La herramienta oficial de Cloudflare para la terminal. Se instala sola con `npm install`. | Probar el juego en tu compu (`npm run dev`), crear la base de datos, cargar la contraseña y publicar (`npm run deploy`). |
| **Cloudflare** | Una empresa que aloja sitios web. Su plan gratuito alcanza para este juego. | Donde queda publicado el juego, con una dirección a la que se entra desde cualquier celular. |

Cuando la guía dice `npx wrangler …`, significa "ejecutá Wrangler". `npx` lo busca dentro del proyecto, así
que no hace falta instalarlo aparte.

### Cómo leer los comandos

- Los bloques grises como el de abajo son comandos: se copian **de a una línea**, se pegan en la terminal y
  se presiona **Enter**.
- Lo que va después de un `#` es un comentario explicativo; no hace falta copiarlo (si se copia, no pasa nada).
- Esperá a que un comando termine (vuelve a aparecer el cursor para escribir) antes de pegar el siguiente.
- Si aparece texto en rojo o la palabra `ERROR`, frená y revisá la sección "Problemas comunes" al final.

```bash
node -v      # ejemplo de comando: muestra la versión de Node.js
```

### 1. Abrir la terminal

- **Mac:** presioná `Cmd + Espacio`, escribí **Terminal** y Enter.
- **Windows:** abrí el menú Inicio, escribí **PowerShell** y abrí "Windows PowerShell".

### 2. Instalar Node.js (incluye npm)

1. Entrá a https://nodejs.org y descargá la versión marcada como **LTS** (la recomendada).
2. Abrí el instalador y seguí los pasos con las opciones por defecto (Siguiente → Siguiente → Instalar).
   - En Mac puede pedirte la contraseña de tu computadora: es normal.
   - En Windows, si aparece la casilla "Automatically install the necessary tools", podés dejarla sin marcar.
3. **Cerrá la terminal y abrila de nuevo** (para que reconozca lo recién instalado).
4. Verificá que quedó instalado:

```bash
node -v      # tiene que mostrar algo como v22.x.x o mayor
npm -v       # tiene que mostrar algo como 10.x.x o mayor
```

Si alguno responde `command not found` (Mac) o "no se reconoce como nombre de un cmdlet" (Windows), reiniciá
la computadora y volvé a probar. Si sigue igual, reinstalá Node.js.

> Alternativa en Mac para quien ya usa Homebrew: `brew install node`.

### 3. Ir a la carpeta del proyecto

La terminal siempre "está parada" en una carpeta, y los comandos se ejecutan ahí. Tenés que pararte en la
carpeta `cloudflare`, que está dentro de `prototipado-efigie2`.

La forma más fácil:

1. Escribí `cd ` en la terminal (**con un espacio al final**, sin presionar Enter).
2. Arrastrá la carpeta `cloudflare` desde el Finder (Mac) o el Explorador de archivos (Windows) y soltala sobre
   la terminal: se completa la ruta sola.
3. Presioná Enter.

Para comprobar que estás en el lugar correcto:

```bash
ls           # Mac (en Windows también funciona en PowerShell)
```

Tenés que ver, entre otros, `package.json`, `wrangler.jsonc`, `worker` y `public`. Si no los ves, estás en otra
carpeta: repetí el paso. **Cada vez que abras una terminal nueva, volvé a hacer este paso.**

### 4. Instalar las dependencias del proyecto

```bash
npm install
```

Descarga Wrangler y lo que necesita el proyecto en una carpeta `node_modules` (puede tardar un par de minutos
y mostrar avisos en amarillo: es normal). Se hace **una sola vez** (o de nuevo si alguien agrega
herramientas al proyecto).

### 5. (Recomendado) Probar el juego en tu computadora

```bash
npm run db:migrar:local     # crea la base de datos de prueba (solo la primera vez)
npm run dev                 # levanta el juego en tu compu
```

Abrí http://localhost:8787 en el navegador. Para frenar el juego, volvé a la terminal y presioná
`Ctrl + C`. Si esto funciona, tu computadora está lista para publicar.

---

## Publicar por primera vez

Todos los comandos se ejecutan en la terminal, parada en la carpeta `cloudflare` (ver "Antes de empezar", paso 3).

### 1. Crear la cuenta y el subdominio `workers.dev`

1. Creá una cuenta gratuita en https://dash.cloudflare.com/sign-up.
2. Ya con la sesión iniciada, abrí en el menú lateral **Compute (Workers) → Workers & Pages**. Según la
   versión del panel puede decir solo "Workers & Pages".
3. Abrir esa página por primera vez crea tu **subdominio `workers.dev`**. Si te pide elegirlo, inventá uno
   corto (por ejemplo `efigie-uca`). Es gratis; solo tiene que estar disponible.
   - Si el panel ofrece **comprar un dominio** o **pasar al plan pago**, no hace falta: ignoralo.

> Si te salteás este paso, `npm run deploy` falla con:
> `You need a workers.dev subdomain in order to proceed. [code: 10063]`

La dirección final del juego va a ser `https://efigie.<tu-subdominio>.workers.dev`
(`efigie` es el `"name"` de `wrangler.jsonc`; si querés otro, cambialo **antes** del primer deploy).

### 2. Conectar la terminal con tu cuenta

```bash
npx wrangler login
```

Se abre el navegador: tocá **Allow**. Para verificar con qué cuenta quedaste: `npx wrangler whoami`.

### 3. Crear la base de datos (D1)

```bash
npx wrangler d1 create efigie
```

Imprime un `database_id` (algo como `25c6e049-30f1-4538-a0ec-3ff34d6da445`). Abrí `wrangler.jsonc` y dejá
**una sola** entrada en `d1_databases`, con `"binding": "DB"` y ese id:

```jsonc
"d1_databases": [
    {
        "binding": "DB",
        "database_name": "efigie",
        "database_id": "<el id que te dio el comando>",
        "migrations_dir": "migrations"
    }
],
```

> ⚠️ Según la versión, `wrangler d1 create` puede **agregar sola** una segunda entrada con
> `"binding": "efigie"`. Borrala y pasá su id a la entrada `DB`. Si quedan las dos, el deploy falla con
> `D1 binding 'DB' references database '00000000-…' which was not found [code: 10181]`.

Al cambiar el id, la base **local** (la de `npm run dev`) arranca vacía. Para recrearla:

```bash
npm run db:migrar:local
npm run db:importar-csv
```

### 4. Crear las tablas en la nube y (opcional) subir respuestas

```bash
npm run db:migrar:remoto               # crea las tablas; confirmá con "y" si pregunta
npm run db:importar-csv -- --remoto    # opcional: sube ../respuestas_app.csv
```

### 5. Publicar

```bash
npm run deploy
```

Al final imprime la dirección, por ejemplo `https://efigie.efigie-uca.workers.dev`.

### 6. Configurar la contraseña de administración

La contraseña protege `/admin-sesion`, `/admin-efigies/` y todas las acciones de admin
(simular, avanzar, retroceder, expulsar, terminar sesiones y guardar la config de la efigie).
Los jugadores no la necesitan.

```bash
npx wrangler secret put ADMIN_KEY
```

- Te pide **`Enter a secret value:`**. Escribí la contraseña y Enter. **No se ve mientras escribís**: es normal.
- Elegí una **sin acentos, ñ ni espacios** (por ejemplo `efigie2026`), así no hay diferencias entre teclados.
- Se aplica sola en unos segundos; no hace falta volver a hacer deploy.
- Si el comando pregunta si querés crear el Worker porque todavía no existe, hacé primero el paso 5.

Comprobá que quedó cargada:

```bash
npx wrangler secret list      # tiene que aparecer "ADMIN_KEY"
```

> ⚠️ **No te saltees este paso.** Sin `ADMIN_KEY`, la administración queda abierta para cualquiera que
> tenga la dirección.

### 7. Probar

1. Abrí `https://efigie.<tu-subdominio>.workers.dev/admin-sesion` en una **ventana privada**: te tiene que
   llevar a la pantalla de ingreso. Escribí la contraseña y tocá **Entrar**.
2. Abrí `https://efigie.<tu-subdominio>.workers.dev/` en el celular y completá la encuesta: tenés que aparecer
   en el lobby y en el panel de admin.
3. Para probar una partida con bots, mirá "Simular una ronda" más abajo.

---

## Direcciones

| Para quién | Dirección |
|---|---|
| Jugadores (la que se comparte en clase) | `https://efigie.<tu-subdominio>.workers.dev/` |
| Admin de sesión | `https://efigie.<tu-subdominio>.workers.dev/admin-sesion` |
| Ajustes de la efigie | `https://efigie.<tu-subdominio>.workers.dev/admin-efigies/` |
| Espectador | `https://efigie.<tu-subdominio>.workers.dev/espectador` |

Atajos desde la encuesta: escribir **admin** como nombre lleva al panel de admin; **ESP**, al modo espectador.

Al copiar direcciones desde la terminal, cuidado con no arrastrar el borde de las tablas (`│`): esa
dirección ya no existe.

---

## Uso diario

### Actualizar después de un cambio

```bash
npm test          # opcional: verifica la lógica del juego
npm run deploy
```

La contraseña, la base de datos y las respuestas se mantienen.

### Cambiar la contraseña

```bash
npx wrangler secret put ADMIN_KEY     # misma instrucción: reemplaza la anterior
```

Quien ya había ingresado sigue adentro hasta que venza su sesión (12 horas). Para cerrar la tuya antes,
borrá las cookies del sitio o usá una ventana privada.

### Contraseña en local

Para `npm run dev`, la contraseña está en `cloudflare/.dev.vars`:

```
ADMIN_KEY=efigie-local
```

Ese archivo no se sube a git ni a Cloudflare; podés cambiarla ahí.

### Simular una ronda

En `/admin-sesion`: **Simular ronda** completa la sala con bots hasta 15. Después se avanza con
**Avanzar a memoria**, **Saltar tiempo** y **Forzar avance desde leaderboard**. Al terminar, usá
**Kill all active sessions** para no dejar la sesión activa.

### Ver el consumo

Panel de Cloudflare → **Workers & Pages → efigie → Metrics**, y **Storage & Databases → D1** para la base.
El plan gratuito se reinicia todos los días a las 00:00 UTC (21:00 en Argentina); una clase de una hora con
15 jugadores usa una fracción chica de los límites. Si un día se superan, el sitio deja de responder hasta el
reinicio, pero nunca genera cobros. Cerrá las pestañas del juego al terminar: abiertas siguen consultando.

---

## Problemas comunes

| Mensaje o síntoma | Causa | Solución |
|---|---|---|
| `node: command not found` / "node no se reconoce como nombre de un cmdlet" | Node.js no está instalado o la terminal se abrió antes de instalarlo | Instalar Node.js ("Antes de empezar", paso 2), cerrar y volver a abrir la terminal |
| Windows: "la ejecución de scripts está deshabilitada en este sistema" al usar `npm` o `npx` | PowerShell bloquea scripts por defecto | Ejecutar una vez `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`, responder `S` (o `Y`) y reintentar |
| `wrangler: command not found` | Se escribió `wrangler` sin `npx`, o falta `npm install` | Usar `npx wrangler …` desde la carpeta `cloudflare`, después de `npm install` |
| `Address already in use` o el puerto 8787 ocupado | Ya hay un `npm run dev` abierto en otra terminal | Cerrar esa terminal o presionar `Ctrl + C` en ella |
| `You need a workers.dev subdomain … [code: 10063]` | La cuenta todavía no tiene subdominio | Paso 1: abrir **Workers & Pages** en el panel una vez |
| `D1 binding 'DB' references database '00000000-…' … [code: 10181]` | Falta el `database_id` real en la entrada `DB` | Paso 3: una sola entrada `DB` con el id correcto |
| `no such table: respuestas` | Faltan las tablas en la nube | `npm run db:migrar:remoto` |
| El admin entra sin pedir contraseña | `ADMIN_KEY` no está cargada | Paso 6 y `npx wrangler secret list` |
| Pide la contraseña y después no pasa nada / "Contraseña incorrecta." | La contraseña escrita no coincide con la cargada | Volver a cargarla con `npx wrangler secret put ADMIN_KEY`, escribiéndola con cuidado, y probar en una ventana privada |
| `npm run dev` muestra la sala vacía | La base local quedó vacía tras cambiar el id | `npm run db:migrar:local` y `npm run db:importar-csv` |
| `npm error enoent Could not read package.json` | La terminal no está en la carpeta `cloudflare` | `cd prototipado-efigie2/cloudflare` |

Los registros detallados de cada comando quedan en `~/Library/Preferences/.wrangler/logs/`.
