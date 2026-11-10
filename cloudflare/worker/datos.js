// @ts-check
// Acceso a D1: respuestas de la encuesta y configuración de la efigie.
import { normalizarConfig } from '../public/lib/efigie/geometria.js';

/** @typedef {{ DB: D1Database, ASSETS: Fetcher, SESION: DurableObjectNamespace, ADMIN_KEY?: string }} Env */

export const CAMPOS = ['nombre', 'anio', 'facultad', 'etapa', 'estacion', 'grupal', 'formato', 'rol'];

/** @param {Env} env */
export async function leerRespuestas(env) {
    const { results } = await env.DB.prepare(`SELECT id, ${CAMPOS.join(', ')} FROM respuestas ORDER BY creado, rowid`).all();
    return /** @type {any[]} */ (results);
}

/** @param {Env} env @param {string} id */
export async function buscarRespuesta(env, id) {
    return /** @type {any} */ (await env.DB.prepare(`SELECT id, ${CAMPOS.join(', ')} FROM respuestas WHERE id = ?`).bind(id).first());
}

/** @param {Env} env */
export async function leerConfig(env) {
    const fila = await env.DB.prepare('SELECT config FROM config_versiones ORDER BY id DESC LIMIT 1').first();
    return normalizarConfig(fila ? JSON.parse(/** @type {string} */ (fila.config)) : null);
}
