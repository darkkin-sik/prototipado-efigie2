// @ts-check
// Durable Object con la sesión de juego. Reemplaza el estado en memoria del servidor C#:
// recibe las llamadas /api/* por HTTP o por WebSocket (ver public/lib/transporte.js) y guarda el estado
// en el almacenamiento del objeto para que sobreviva si Cloudflare lo reinicia.
import { DurableObject } from 'cloudflare:workers';
import { Juego, estadoInicial } from './juego.js';
import { leerConfig, leerRespuestas, buscarRespuesta } from './datos.js';
import { CONFIG_POR_DEFECTO } from '../public/lib/efigie/geometria.js';

/** @typedef {import('./datos.js').Env} Env */

const INTERVALO_GUARDADO_MS = 3000; // límite de escrituras: el plan gratuito permite 100.000 filas por día
const VIGENCIA_CONFIG_MS = 5000;
const VIGENCIA_RESPUESTAS_MS = 3000;

export class SesionEfigie extends DurableObject {
    /** @param {DurableObjectState} ctx @param {Env} env */
    constructor(ctx, env) {
        super(ctx, env);
        this.config = { ...CONFIG_POR_DEFECTO };
        this.configLeida = 0;
        /** @type {any[] | null} */
        this.respuestas = null;
        this.respuestasLeidas = 0;
        this.ultimoGuardado = 0;
        this.ultimoEstadoGuardado = '';
        /** @type {ReturnType<typeof setTimeout> | null} */
        this.guardadoPendiente = null;
        this.juego = new Juego(estadoInicial(), this.dependencias());
        ctx.blockConcurrencyWhile(async () => {
            const guardado = await ctx.storage.get('estado');
            if (guardado) {
                // Se restaura sobre el mismo objeto: las rutas de Juego lo tienen referenciado.
                Object.assign(this.juego.s, estadoInicial(), guardado);
                this.ultimoEstadoGuardado = JSON.stringify(this.juego.s);
            }
        });
    }

    /** @returns {import('./juego.js').Dependencias} */
    dependencias() {
        return {
            config: () => this.config,
            buscarRespuesta: id => buscarRespuesta(this.env, id),
            todasLasRespuestas: async () => {
                if (!this.respuestas || Date.now() - this.respuestasLeidas > VIGENCIA_RESPUESTAS_MS) {
                    this.respuestas = await leerRespuestas(this.env);
                    this.respuestasLeidas = Date.now();
                }
                return this.respuestas;
            }
        };
    }

    /** @param {Request} request */
    async fetch(request) {
        // x-efigie-admin lo pone el Worker después de validar la cookie de administración.
        const admin = request.headers.get('x-efigie-admin') === '1';
        if (request.headers.get('Upgrade') === 'websocket') {
            const par = new WebSocketPair();
            this.ctx.acceptWebSocket(par[1]);
            par[1].serializeAttachment({ admin });
            return new Response(null, { status: 101, webSocket: par[0] });
        }
        const url = new URL(request.url);
        const texto = request.method === 'GET' ? '' : await request.text();
        const r = await this.procesar(request.method, url.pathname + url.search, texto, admin);
        if (r.status === 204 || r.body === undefined) return new Response(null, { status: r.status });
        return Response.json(r.body, { status: r.status });
    }

    /** Mensaje del transporte: { id, method, url, body } → { id, status, body }. @param {WebSocket} ws @param {string | ArrayBuffer} mensaje */
    async webSocketMessage(ws, mensaje) {
        let m;
        try { m = JSON.parse(typeof mensaje === 'string' ? mensaje : new TextDecoder().decode(mensaje)); } catch { return; }
        if (m?.tipo === 'ping') return ws.send('{"tipo":"pong"}');
        const admin = ws.deserializeAttachment()?.admin === true;
        const r = await this.procesar(String(m.method ?? 'GET'), String(m.url ?? ''), typeof m.body === 'string' ? m.body : '', admin);
        ws.send(JSON.stringify({ id: m.id, status: r.status, body: r.body === undefined ? null : JSON.stringify(r.body) }));
    }

    /** @param {WebSocket} ws */
    async webSocketClose(ws) { try { ws.close(); } catch { /* ya cerrado */ } }

    /** @param {string} method @param {string} ruta @param {string} texto @param {boolean} admin @returns {Promise<import('./juego.js').Respuesta>} */
    async procesar(method, ruta, texto, admin) {
        if (Date.now() - this.configLeida > VIGENCIA_CONFIG_MS) {
            this.config = await leerConfig(this.env);
            this.configLeida = Date.now();
        }
        const url = new URL(ruta, 'http://sesion');
        if (url.pathname.startsWith('/api/admin/') && !admin)
            return { status: 401, body: { error: 'Ingresá la contraseña de administración.' } };
        let body = null;
        if (texto) { try { body = JSON.parse(texto); } catch { return { status: 400 }; } }
        let r;
        try {
            r = await this.juego.manejar({ method: method.toUpperCase(), path: url.pathname, query: url.searchParams, body });
            this.juego.precalentarSvg();
        } catch (error) {
            console.error(error);
            r = { status: 500, body: { error: 'Error interno' } };
        }
        this.programarGuardado(url.pathname.startsWith('/api/admin/'));
        return r;
    }

    /** Guarda el estado como máximo cada INTERVALO_GUARDADO_MS (o enseguida tras una acción de admin). @param {boolean} urgente */
    programarGuardado(urgente) {
        const espera = urgente ? 0 : Math.max(0, INTERVALO_GUARDADO_MS - (Date.now() - this.ultimoGuardado));
        if (this.guardadoPendiente !== null) {
            if (!urgente) return;
            clearTimeout(this.guardadoPendiente);
        }
        this.guardadoPendiente = setTimeout(() => {
            this.guardadoPendiente = null;
            this.ultimoGuardado = Date.now();
            const json = JSON.stringify(this.juego.s);
            if (json === this.ultimoEstadoGuardado) return;
            this.ultimoEstadoGuardado = json;
            this.ctx.storage.put('estado', this.juego.s).catch(error => console.error('No se pudo guardar el estado', error));
        }, espera);
    }
}
