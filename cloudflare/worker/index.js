// @ts-check
// Worker de Efigie: rutas de páginas (mismas URLs que Program.cs), respuestas y config en D1,
// y el resto de /api/* reenviado al Durable Object de la sesión de juego.
import { CAMPOS, leerRespuestas, buscarRespuesta, leerConfig } from './datos.js';
import { normalizarConfig } from '../public/lib/efigie/geometria.js';
import { generarSvg, inyectarAnimacionRotacion } from '../public/lib/efigie/svg.js';
import { esAdmin, ingresar, salir, pedirIngreso } from './admin.js';

export { SesionEfigie } from './sesion.js';

/** @typedef {import('./datos.js').Env} Env */

const COOKIE_DISPOSITIVO = 'efigie_device_id';

/** Páginas del juego: ruta pública → archivo en public/juego/ (sin .html). */
const PAGINAS = {
    '/encuesta': 'index',
    '/admin-login': 'admin-login',
    '/pre-ronda-1': 'pre-ronda-1',
    '/ronda-2-representante': 'pre-ronda-1',
    '/ronda-1-preview': 'ronda-en-curso',
    '/ronda-en-curso': 'ronda-en-curso',
    '/ronda-1-adivinar': 'ronda-1-adivinar',
    '/ronda-1-leaderboard': 'ronda-1-leaderboard',
    '/ronda-2': 'ronda-3-identificacion',
    '/ronda-2-equipo': 'ronda-3',
    '/ronda-3-equipo': 'ronda-3',
    '/ronda-2-leaderboard': 'ronda-3-leaderboard',
    '/ronda-3': 'ronda-4',
    '/ronda-3-leaderboard': 'ronda-3-final-leaderboard',
    '/espectador': 'espectador'
};
const REDIRECCIONES = { '/ronda-3-identificacion': '/ronda-2', '/ronda-4': '/ronda-3' };

export default {
    /** @param {Request} request @param {Env} env */
    async fetch(request, env) {
        const url = new URL(request.url);
        const { pathname } = url;
        const ruta = `${request.method} ${pathname}`;
        try {
            // Respuestas y configuración (D1)
            if (ruta === 'GET /api/datos') return Response.json(await leerRespuestas(env));
            if (ruta === 'GET /api/config') return Response.json(await leerConfig(env));
            if (ruta === 'GET /api/config/versiones') return listarVersiones(env);
            if (ruta === 'POST /api/config') return guardarConfig(request, env);
            if (ruta === 'POST /guardar') return guardarRespuesta(request, env);
            if (ruta === 'POST /api/iniciar') return iniciar(request, env, url.searchParams.get('id') ?? '');
            if (ruta === 'POST /api/admin/login') return ingresar(request, env);
            if (ruta === 'POST /api/admin/logout') return salir(request);
            const svg = pathname.match(/^\/efigie\/([\w:-]+)\.svg$/);
            if (request.method === 'GET' && svg) return svgEfigie(env, svg[1], url);
            const datosEfigie = pathname.match(/^\/api\/efigie\/([\w:-]+)$/);
            if (request.method === 'GET' && datosEfigie) return jsonEfigie(env, datosEfigie[1], url);

            // Sesión de juego (Durable Object). El Worker valida la cookie de admin y se lo indica al objeto.
            if (pathname.startsWith('/api/')) {
                const reenvio = new Request(request);
                reenvio.headers.set('x-efigie-admin', (await esAdmin(request, env)) ? '1' : '0');
                return sesion(env).fetch(reenvio);
            }

            // Páginas
            if (request.method === 'GET') {
                if (pathname === '/') return bienvenida(request, env, url);
                if (pathname === '/admin-sesion')
                    return (await esAdmin(request, env)) ? pagina(request, env, 'admin-sesion') : pedirIngreso(request);
                if (pathname === '/admin-efigies' || pathname.startsWith('/admin-efigies/'))
                    return (await esAdmin(request, env)) ? env.ASSETS.fetch(request) : pedirIngreso(request);
                if (pathname in PAGINAS) return pagina(request, env, /** @type {Record<string, string>} */ (PAGINAS)[pathname]);
                if (pathname in REDIRECCIONES)
                    return Response.redirect(new URL(/** @type {Record<string, string>} */ (REDIRECCIONES)[pathname] + url.search, url), 302);
                const lobby = pathname.match(/^\/(lobby|efigie)\/([^/]+)$/);
                if (lobby) return paginaDeEfigie(request, env, lobby[1], decodeURIComponent(lobby[2]));
            }
            return env.ASSETS.fetch(request);
        } catch (error) {
            console.error(error);
            return Response.json({ error: 'Error interno' }, { status: 500 });
        }
    }
};

/** @param {Env} env */
function sesion(env) {
    return env.SESION.get(env.SESION.idFromName('principal'));
}

/** @param {Request} request @param {Env} env @param {string} archivo */
function pagina(request, env, archivo) {
    return env.ASSETS.fetch(new Request(new URL(`/juego/${archivo}`, request.url), { headers: request.headers }));
}

/** "/" de Program.cs: borra las respuestas anteriores del dispositivo (salvo ?reinicioSesion=1) y muestra la bienvenida. */
async function bienvenida(/** @type {Request} */ request, /** @type {Env} */ env, /** @type {URL} */ url) {
    const { deviceId, setCookie } = obtenerDispositivo(request);
    if (url.searchParams.get('reinicioSesion') !== '1')
        await env.DB.prepare('DELETE FROM respuestas WHERE device_id = ?').bind(deviceId).run();
    const respuesta = await pagina(request, env, 'bienvenida');
    return conCookie(respuesta, setCookie);
}

/** /lobby/<id> y /efigie/<id>: verifican que la efigie exista, como en Program.cs. */
async function paginaDeEfigie(/** @type {Request} */ request, /** @type {Env} */ env, /** @type {string} */ tipo, /** @type {string} */ id) {
    if (!(await buscarRespuesta(env, id)))
        return new Response('<h2>No se encontró la efigie solicitada.</h2>', { headers: { 'content-type': 'text/html; charset=utf-8' } });
    return pagina(request, env, tipo);
}

/** @param {Env} env */
async function listarVersiones(env) {
    const { results } = await env.DB.prepare('SELECT id, config, nota, creado FROM config_versiones ORDER BY id DESC LIMIT 50').all();
    return Response.json(results.map(v => ({ ...v, config: JSON.parse(/** @type {string} */ (v.config)) })));
}

/** @param {Request} request @param {Env} env */
async function guardarConfig(request, env) {
    if (!(await esAdmin(request, env)))
        return Response.json({ error: 'Clave de administración incorrecta' }, { status: 401 });
    const cuerpo = /** @type {any} */ (await request.json().catch(() => null));
    if (!cuerpo || typeof cuerpo !== 'object') return Response.json({ error: 'Config inválida' }, { status: 400 });
    const config = normalizarConfig(cuerpo.config ?? cuerpo);
    const nota = typeof cuerpo.nota === 'string' ? cuerpo.nota.slice(0, 200) : '';
    const fila = await env.DB.prepare('INSERT INTO config_versiones (config, nota) VALUES (?, ?) RETURNING id')
        .bind(JSON.stringify(config), nota).first();
    return Response.json({ estado: 'ok', version: fila?.id, config });
}

/** @param {Request} request @param {Env} env */
async function guardarRespuesta(request, env) {
    const datos = /** @type {any} */ (await request.json().catch(() => null));
    if (!datos || typeof datos !== 'object') return new Response(null, { status: 400 });
    const { deviceId, setCookie } = obtenerDispositivo(request);
    const id = typeof datos.id === 'string' && /^[\w-]{1,64}$/.test(datos.id) ? datos.id : crypto.randomUUID().replaceAll('-', '');
    const valores = CAMPOS.map(c => String(datos[c] ?? '').slice(0, 200));
    await env.DB.batch([
        env.DB.prepare('DELETE FROM respuestas WHERE device_id = ? OR id = ?').bind(deviceId, id),
        env.DB.prepare(`INSERT INTO respuestas (id, ${CAMPOS.join(', ')}, device_id) VALUES (${'?, '.repeat(CAMPOS.length + 1)}?)`)
            .bind(id, ...valores, deviceId)
    ]);
    return Response.json({ estado: 'ok', id, url: `/lobby/${id}` }, { headers: setCookie });
}

/** Equivalente a /api/iniciar: borra las respuestas anteriores de este dispositivo. @param {Request} request @param {Env} env @param {string} idAnterior */
async function iniciar(request, env, idAnterior) {
    const { deviceId, setCookie } = obtenerDispositivo(request);
    await env.DB.prepare('DELETE FROM respuestas WHERE device_id = ? OR (? <> \'\' AND id = ?)').bind(deviceId, idAnterior, idAnterior).run();
    return new Response(null, { status: 204, headers: setCookie });
}

/** @param {Env} env @param {string} id @param {URL} url */
async function svgEfigie(env, id, url) {
    const datos = await buscarRespuesta(env, id);
    if (!datos) return new Response('No se encontró la efigie', { status: 404 });
    let svg = generarSvg(datos, await leerConfig(env), url.searchParams.get('marco') === '1');
    if (url.searchParams.get('animada') === '1') svg = inyectarAnimacionRotacion(svg);
    return new Response(svg, { headers: { 'content-type': 'image/svg+xml; charset=utf-8', 'cache-control': 'no-store' } });
}

/** Nombre y SVG animado de una efigie, para las páginas /lobby/<id> y /efigie/<id>. @param {Env} env @param {string} id @param {URL} url */
async function jsonEfigie(env, id, url) {
    const datos = await buscarRespuesta(env, id);
    if (!datos) return Response.json({ error: 'No se encontró la efigie solicitada.' }, { status: 404 });
    const config = await leerConfig(env);
    const marco = url.searchParams.get('marco') === '1';
    const radioTexto = config.RadioBaseTexto > 0 ? config.RadioBaseTexto : 15;
    return Response.json({
        id: datos.id,
        nombre: datos.nombre,
        svg: inyectarAnimacionRotacion(generarSvg(datos, config, marco)),
        insetMarco: ((75 - radioTexto * 1.3) / 150) * 100
    });
}

/** Port de EnsureDeviceId: cookie httpOnly con un id de 32 hex. @param {Request} request */
function obtenerDispositivo(request) {
    const cookie = (request.headers.get('cookie') ?? '').match(new RegExp(`${COOKIE_DISPOSITIVO}=([0-9a-f]{32})`));
    if (cookie) return { deviceId: cookie[1], setCookie: {} };
    const deviceId = crypto.randomUUID().replaceAll('-', '');
    return {
        deviceId,
        setCookie: { 'set-cookie': `${COOKIE_DISPOSITIVO}=${deviceId}; Path=/; Max-Age=31536000; HttpOnly; SameSite=Lax` }
    };
}

/** @param {Response} respuesta @param {Record<string, string>} setCookie */
function conCookie(respuesta, setCookie) {
    if (!setCookie['set-cookie']) return respuesta;
    const copia = new Response(respuesta.body, respuesta);
    copia.headers.append('set-cookie', setCookie['set-cookie']);
    return copia;
}
