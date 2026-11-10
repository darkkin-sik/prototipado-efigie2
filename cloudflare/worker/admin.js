// @ts-check
// Contraseña de administración (secreto ADMIN_KEY). Protege /admin-sesion, /admin-efigies, /api/admin/* y POST /api/config.
// Al ingresar la clave se guarda una cookie httpOnly con un hash de la clave (no la clave), válida 12 horas.
// Si ADMIN_KEY no está definida, la administración queda abierta como en el servidor C#.

/** @typedef {import('./datos.js').Env} Env */

const COOKIE_ADMIN = 'efigie_admin';
const DURACION_SEGUNDOS = 12 * 60 * 60;

/** @param {string} clave */
async function tokenDeClave(clave) {
    const hash = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`efigie-admin:${clave}`));
    return [...new Uint8Array(hash)].map(b => b.toString(16).padStart(2, '0')).join('');
}

/** Comparación en tiempo constante. @param {string} a @param {string} b */
function iguales(a, b) {
    const x = new TextEncoder().encode(a), y = new TextEncoder().encode(b);
    return x.byteLength === y.byteLength && crypto.subtle.timingSafeEqual(x, y);
}

/** @param {Request} request @param {Env} env */
export async function esAdmin(request, env) {
    if (!env.ADMIN_KEY) return true;
    const cookie = (request.headers.get('cookie') ?? '').match(new RegExp(`(?:^|;\\s*)${COOKIE_ADMIN}=([0-9a-f]{64})`));
    if (cookie && iguales(cookie[1], await tokenDeClave(env.ADMIN_KEY))) return true;
    const encabezado = request.headers.get('x-admin-key');
    return encabezado !== null && iguales(encabezado, env.ADMIN_KEY);
}

/** POST /api/admin/login { clave } → cookie de sesión de admin. @param {Request} request @param {Env} env */
export async function ingresar(request, env) {
    const cuerpo = /** @type {any} */ (await request.json().catch(() => null));
    if (env.ADMIN_KEY && (typeof cuerpo?.clave !== 'string' || !iguales(cuerpo.clave, env.ADMIN_KEY)))
        return Response.json({ error: 'Clave incorrecta' }, { status: 401 });
    const token = env.ADMIN_KEY ? await tokenDeClave(env.ADMIN_KEY) : '';
    return Response.json({ estado: 'ok' }, { headers: { 'set-cookie': cookieAdmin(request, token, DURACION_SEGUNDOS) } });
}

/** POST /api/admin/logout @param {Request} request */
export function salir(request) {
    return Response.json({ estado: 'ok' }, { headers: { 'set-cookie': cookieAdmin(request, '', 0) } });
}

/** Redirige a la pantalla de ingreso, volviendo después a la página pedida. @param {Request} request */
export function pedirIngreso(request) {
    const url = new URL(request.url);
    const volver = url.pathname + url.search;
    return Response.redirect(new URL(`/admin-login?volver=${encodeURIComponent(volver)}`, url), 302);
}

/** @param {Request} request @param {string} valor @param {number} maxAge */
function cookieAdmin(request, valor, maxAge) {
    const segura = new URL(request.url).protocol === 'https:' ? '; Secure' : '';
    return `${COOKIE_ADMIN}=${valor}; Path=/; Max-Age=${maxAge}; HttpOnly; SameSite=Strict${segura}`;
}
