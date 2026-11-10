// @ts-check
// Port de la lógica de juego de Program.cs (lobby, equipos, rondas, leaderboards y admin de sesión).
// No depende de Cloudflare: el Durable Object (sesion.js) le pasa las solicitudes y guarda `estado`.
// Los nombres de los campos JSON son los mismos que devolvía el servidor C#, así los HTML no cambian.
import { generarSvg } from '../public/lib/efigie/svg.js';

/**
 * @typedef {{ id: string, nombre: string, anio: string, facultad: string, etapa: string, estacion: string,
 *   grupal: string, formato: string, rol: string }} Datos
 * @typedef {{ id: string, sessionId: string, nombre: string, lastSeen: number, esBot: boolean, datos: Datos }} Participante
 * @typedef {{ numero: number, color: string, colorTexto: string, ids: string[], posibilidades: number, puntaje: number }} Equipo
 * @typedef {{ method: string, path: string, query: URLSearchParams, body: any }} Solicitud
 * @typedef {{ status: number, body?: any }} Respuesta
 * @typedef {{
 *   config: () => any,
 *   buscarRespuesta: (id: string) => Promise<Datos | null>,
 *   todasLasRespuestas: () => Promise<Datos[]>
 * }} Dependencias
 */

const COLORES_GRUPO = ['#133463', '#7C171F', '#6064A3', '#84B444', '#E27646'];
const COLORES_TEXTO_GRUPO = ['#ffffff', '#ffffff', '#ffffff', '#101a15', '#101a15'];
const ETAPAS = ['pre-ronda', 'memoria', 'adivinanza', 'leaderboard', 'ronda-2-antigua', 'ronda-2-representante',
    'ronda-2-identificacion', 'ronda-2-leaderboard', 'ronda-3', 'ronda-3-leaderboard'];
const PREGUNTAS_RONDA_TRES = [
    { layer: 'anio', answer: 'Año de nacimiento', hint: 'No dice cuántos años tenés, pero permite calcularlos. Son cuatro números que señalan cuándo empezó tu historia.' },
    { layer: 'estaciones', answer: 'Estación favorita', hint: 'Somos cuatro y nos turnamos para transformar el año. Entre frío, flores, hojas y calor, una se lleva tu elección.' },
    { layer: 'carrera', answer: 'Etapa de la carrera', hint: 'Algunos recién empiezan, otros están por terminar.' },
    { layer: 'rol', answer: 'Rol en un trabajo grupal', hint: 'En un equipo hay quien propone, quien organiza, quien materializa y quien mejora.' },
    { layer: 'historia', answer: 'Formato de historia', hint: 'Una misma historia puede llegar de muchas maneras: con páginas, pantallas, escenarios o decisiones que la cambian.' },
    { layer: 'conexiones', answer: 'Forma de trabajar', hint: 'Una tarea por resolver, distintas formas de empezar.' }
];
const OPCIONES_RONDA_TRES = PREGUNTAS_RONDA_TRES.map(p => p.answer);
const CAPAS = ['anio', 'estaciones', 'carrera', 'rol', 'historia', 'conexiones'];
const NOMBRES_CAPAS = ['Año', 'Estaciones', 'Carrera', 'Rol', 'Historia', 'Conexiones'];
const RASGOS_RONDA_CUATRO = ['anio', 'facultad', 'etapa', 'estacion', 'grupal', 'formato', 'rol'];

const FACULTADES = ['Arquitectura y Diseño', 'Ciencias Económicas', 'Ciencias Médicas', 'Ciencias Sociales', 'Derecho', 'Filosofía y Letras', 'Ingeniería y Ciencias Agrarias', 'Música', 'Psicología y Psicopedagogía', 'Teología', 'Staff / Docentes'];
const ETAPAS_CARRERA = ['1 (Ingresante / Primer año)', '2', '3', '4', '5', '6 (Sexto año o más)', 'Docente', 'No Docente'];
const ESTACIONES = ['Verano', 'Otoño', 'Invierno', 'Primavera'];
const GRUPALES = ['Solo', 'Grupal'];
const FORMATOS = ['Película', 'Serie', 'Libro', 'Videojuego', 'Teatro'];
const ROLES = ['Proponer (Generar ideas iniciales)', 'Organizar (Estructurar y planificar)', 'Materializar (Llevar a la práctica)', 'Mejorar (Revisar y pulir detalles)'];

// --- Utilidades equivalentes a las de .NET ---
const ok = (/** @type {any} */ body) => ({ status: 200, body });
const conflict = (/** @type {any} */ body = undefined) => ({ status: 409, body });
const notFound = () => ({ status: 404 });
const badRequest = (/** @type {any} */ body = undefined) => ({ status: 400, body });
const forbid = () => ({ status: 403 });
const noContent = () => ({ status: 204 });
const azar = (/** @type {number} */ min, /** @type {number} */ max) => min + Math.floor(Math.random() * (max - min));
const elegir = (/** @type {any[]} */ lista) => lista[Math.floor(Math.random() * lista.length)];
/** Equivalente a OrderBy(_ => Random.Shared.Next()). @template T @param {T[]} lista */
const barajar = lista => {
    const copia = lista.slice();
    for (let i = copia.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [copia[i], copia[j]] = [copia[j], copia[i]];
    }
    return copia;
};
/** Math.Round(x, MidpointRounding.AwayFromZero) */
const redondear = (/** @type {number} */ x) => Math.sign(x) * Math.round(Math.abs(x));
const promedio = (/** @type {number[]} */ v) => v.reduce((a, b) => a + b, 0) / v.length;
/** StringComparer.OrdinalIgnoreCase para ordenar */
const compararSinMayusculas = (/** @type {string} */ a, /** @type {string} */ b) => {
    const x = a.toUpperCase(), y = b.toUpperCase();
    return x < y ? -1 : x > y ? 1 : 0;
};
const esIdSesion = (/** @type {unknown} */ v) => typeof v === 'string' && /^[0-9a-fA-F]{32}$/.test(v);
const texto = (/** @type {unknown} */ v) => (typeof v === 'string' ? v : '');
const vacio = (/** @type {unknown} */ v) => typeof v !== 'string' || v.trim() === '';

export function estadoInicial() {
    return {
        sesionesActivas: /** @type {Record<string, { responseId: string, lastSeen: number }>} */ ({}),
        lobby: /** @type {Record<string, Participante>} */ ({}),
        espectadores: /** @type {Record<string, Participante>} */ ({}),
        equiposEspectador: /** @type {Record<string, Equipo>} */ ({}),
        sesionesEspectador: /** @type {Record<string, { sessionId: string, responseId: string }>} */ ({}),
        expulsados: /** @type {Record<string, number>} */ ({}),
        vuelvenAEncuesta: /** @type {Record<string, 1>} */ ({}),
        vuelvenAInicio: /** @type {Record<string, 1>} */ ({}),
        listos: /** @type {Record<string, boolean>} */ ({}),
        listosSiguiente: /** @type {Record<string, boolean>} */ ({}),
        listosRondaTres: /** @type {Record<string, boolean>} */ ({}),
        listosRondaCuatro: /** @type {Record<string, boolean>} */ ({}),
        votos1: /** @type {Record<string, string>} */ ({}),
        votos2: /** @type {Record<string, string>} */ ({}),
        navegaciones: /** @type {Record<string, string>} */ ({}),
        ultimaActividad: /** @type {Record<string, number>} */ ({}),
        puntos1: /** @type {Record<string, number>} */ ({}),
        estados2: /** @type {Record<string, any>} */ ({}),
        puntos2: /** @type {Record<string, number>} */ ({}),
        estados3: /** @type {Record<string, any>} */ ({}),
        puntos3: /** @type {Record<string, number>} */ ({}),
        preguntas3: /** @type {Record<string, typeof PREGUNTAS_RONDA_TRES>} */ ({}),
        progreso3: /** @type {Record<string, any>} */ ({}),
        estados4: /** @type {Record<string, any>} */ ({}),
        puntos4: /** @type {Record<string, number>} */ ({}),
        grupoPorParticipante: /** @type {Record<string, number>} */ ({}),
        equipos: /** @type {Equipo[]} */ ([]),
        rondaIniciada: false,
        previewIniciada: false,
        etapa: 0,
        inicioEtapa: /** @type {number | null} */ (null),
        segundosExtra: 0
    };
}

export class Juego {
    /**
     * `estado` no se reemplaza nunca (las rutas lo referencian): para restaurarlo, usar Object.assign(juego.s, ...).
     * @param {ReturnType<typeof estadoInicial>} estado @param {Dependencias} deps
     */
    constructor(estado, deps) {
        this.s = estado;
        this.deps = deps;
        /** Caché de SVG por participante (no se persiste: se regenera a partir de los datos). */
        this.cacheSvg = new Map();
        this.rutas = this.crearRutas();
    }

    /** @param {Solicitud} req @returns {Promise<Respuesta>} */
    async manejar(req) {
        const ruta = this.rutas.get(`${req.method} ${req.path}`);
        if (!ruta) return notFound();
        return ruta(req);
    }

    // ---------------------------------------------------------------- ayudantes
    get ahora() { return Date.now(); }

    /** @param {Participante} p */
    claveSvg(p) { return `${p.id}|${JSON.stringify(p.datos)}|${JSON.stringify(this.deps.config())}`; }

    /** @param {Participante} p */
    svg(p) {
        const clave = this.claveSvg(p);
        let svg = this.cacheSvg.get(clave);
        if (!svg) {
            svg = generarSvg(p.datos, this.deps.config(), false);
            if (this.cacheSvg.size > 300) this.cacheSvg.clear();
            this.cacheSvg.set(clave, svg);
        }
        return svg;
    }

    /**
     * Genera como máximo un SVG pendiente por solicitud. Así, al simular con 12 bots, sus efigies se calculan
     * de a una durante la elección de representante y no todas juntas (el plan gratuito da 10 ms de CPU por solicitud).
     */
    precalentarSvg() {
        for (const p of [...Object.values(this.s.lobby), ...Object.values(this.s.espectadores)])
            if (!this.cacheSvg.has(this.claveSvg(p))) { this.svg(p); return; }
    }

    /** @param {Datos} datos */
    svgDatos(datos) {
        return this.svg({ id: datos.id, sessionId: '', nombre: datos.nombre, lastSeen: 0, esBot: false, datos });
    }

    /** @param {string} id @returns {Participante | undefined} */
    participante(id) { return this.s.lobby[id] ?? this.s.espectadores[id]; }

    /** @param {string} id */
    nombre(id) { return this.participante(id)?.nombre; }

    /** @param {string} id @returns {{ numero: number, equipo: Equipo } | null} */
    equipoDe(id) {
        const n = this.s.grupoPorParticipante[id];
        if (n > 0 && n <= this.s.equipos.length) return { numero: n, equipo: this.s.equipos[n - 1] };
        const e = this.s.equiposEspectador[id];
        return e ? { numero: e.numero, equipo: e } : null;
    }

    /** @param {number} ronda */
    votos(ronda) { return ronda === 2 ? this.s.votos2 : this.s.votos1; }

    /** @param {Equipo} equipo @returns {string | null} */
    representante(equipo, ronda = 1) {
        const votos = this.votos(ronda);
        if (equipo.ids.length === 0) return null;
        const candidato = votos[equipo.ids[0]];
        if (candidato === undefined) return null;
        return equipo.ids.every(id => votos[id] === candidato) ? candidato : null;
    }

    /** @param {Equipo} equipo */
    sembrarVotosBots(equipo, ronda = 1) {
        if (equipo.ids.length === 0) return;
        if (equipo.ids.some(id => { const p = this.participante(id); return p && !p.esBot; })) return;
        const votos = this.votos(ronda);
        for (const id of equipo.ids) if (this.participante(id)?.esBot) votos[id] = equipo.ids[0];
    }

    /** @param {Equipo} equipo */
    firma(equipo) { return equipo.ids.slice().sort().join('|'); }

    equiposEspectadorDistintos() {
        const vistos = new Map();
        for (const e of Object.values(this.s.equiposEspectador)) if (!vistos.has(this.firma(e))) vistos.set(this.firma(e), e);
        return [...vistos.values()];
    }

    todosRepresentantesRondaDos() { return this.s.equipos.every(e => this.representante(e, 2) !== null); }

    etapaNombre() { return ETAPAS[Math.min(Math.max(this.s.etapa, 0), ETAPAS.length - 1)]; }

    segundosRestantes() {
        const s = this.s;
        if (s.inicioEtapa === null || ![1, 2, 5].includes(s.etapa)) return 0;
        const duracion = s.etapa === 1 ? 30 + s.segundosExtra : s.etapa === 2 ? 120 + s.segundosExtra : 30;
        const transcurridos = Math.ceil((this.ahora - s.inicioEtapa) / 1000);
        return Math.max(0, duracion - transcurridos);
    }

    limpiarLobby() {
        const ahora = this.ahora;
        if (!this.s.rondaIniciada)
            for (const [id, p] of Object.entries(this.s.lobby))
                if (!p.esBot && ahora - p.lastSeen > 90_000) delete this.s.lobby[id];
        for (const [id, hasta] of Object.entries(this.s.expulsados)) if (hasta <= ahora) delete this.s.expulsados[id];
    }

    /** @param {string} nombreBot @returns {Datos} */
    crearDatosBot(nombreBot) {
        return {
            id: `bot:${nombreBot}`, nombre: nombreBot, anio: String(azar(1990, 2009)),
            facultad: elegir(FACULTADES), etapa: elegir(ETAPAS_CARRERA), estacion: elegir(ESTACIONES),
            grupal: elegir(GRUPALES), formato: elegir(FORMATOS), rol: elegir(ROLES)
        };
    }

    /** @param {string} id @param {string} nombre @param {boolean} esBot @param {Datos} datos @returns {Participante} */
    nuevoParticipante(id, nombre, esBot, datos, sessionId = '') {
        return { id, sessionId, nombre, lastSeen: this.ahora, esBot, datos };
    }

    /** Equivalente a FirmaAnio: distingue pétalos (<2000) o polígono y los dos últimos dígitos. @param {string | undefined} anio */
    firmaAnio(anio) {
        const n = parseInt(String(anio ?? '').replace(/\D/g, ''), 10) || 0;
        return `${n < 2000 ? 'petalos' : 'poligono'}-${String(Math.abs(n % 100)).padStart(2, '0')}`;
    }

    /** @param {Participante[]} trio */
    evaluarTrio(trio) {
        const variables = trio.map(p => [this.firmaAnio(p.datos.anio), p.datos.facultad, p.datos.etapa,
            p.datos.estacion, p.datos.grupal, p.datos.formato, p.datos.rol].map(v => String(v ?? '').toUpperCase()));
        let puntaje = 0, posibilidades = 0;
        for (let v = 0; v < 7; v++) {
            const distintos = new Set(variables.map(r => r[v])).size;
            if (distintos === 2) { puntaje += 2; posibilidades++; }
            else if (distintos === 3) puntaje++;
        }
        return { puntaje, posibilidades };
    }

    /** Recocido simulado de ConstruirEquiposOptimos (con evaluación incremental para no exceder la CPU). @param {Participante[]} participantes */
    construirEquiposOptimos(participantes, priorizarReales = false) {
        const reinicios = 8, iteraciones = 5000;
        const cantidadReales = participantes.filter(p => !p.esBot).length;
        // Códigos enteros por variable para comparar rápido (mismo criterio que EvaluarTrio).
        const tablas = Array.from({ length: 7 }, () => new Map());
        const codigos = new Map(participantes.map(p => {
            const valores = [this.firmaAnio(p.datos.anio), p.datos.facultad, p.datos.etapa, p.datos.estacion, p.datos.grupal, p.datos.formato, p.datos.rol]
                .map(v => String(v ?? '').toUpperCase());
            return [p, valores.map((v, i) => { if (!tablas[i].has(v)) tablas[i].set(v, tablas[i].size); return tablas[i].get(v); })];
        }));
        /** @param {Participante[]} orden @param {number} g */
        const puntajeGrupo = (orden, g) => {
            const a = codigos.get(orden[g * 3]), b = codigos.get(orden[g * 3 + 1]), c = codigos.get(orden[g * 3 + 2]);
            let puntaje = 0;
            for (let v = 0; v < 7; v++) {
                const distintos = 1 + (a[v] !== b[v] ? 1 : 0) + (c[v] !== a[v] && c[v] !== b[v] ? 1 : 0);
                puntaje += distintos === 2 ? 2 : distintos === 3 ? 1 : 0;
            }
            const peso = priorizarReales && [0, 1, 2].some(k => !orden[g * 3 + k].esBot) ? 100 : 1;
            return puntaje * peso;
        };
        const evaluarOrden = (/** @type {Participante[]} */ orden) => {
            let total = 0;
            for (let g = 0; g < orden.length / 3; g++) total += puntajeGrupo(orden, g);
            return total;
        };

        let mejorOrden = participantes.slice(), mejorPuntaje = -Infinity;
        for (let r = 0; r < reinicios; r++) {
            const actual = priorizarReales
                ? [...barajar(participantes.filter(p => !p.esBot)), ...barajar(participantes.filter(p => p.esBot))]
                : barajar(participantes);
            let puntajeActual = evaluarOrden(actual);
            if (puntajeActual > mejorPuntaje) { mejorPuntaje = puntajeActual; mejorOrden = actual.slice(); }
            for (let it = 0; it < iteraciones; it++) {
                const primero = Math.floor(Math.random() * actual.length), segundo = Math.floor(Math.random() * actual.length);
                const g1 = Math.floor(primero / 3), g2 = Math.floor(segundo / 3);
                if (g1 === g2) continue;
                if (priorizarReales && (primero < cantidadReales) !== (segundo < cantidadReales)) continue;
                const antes = puntajeGrupo(actual, g1) + puntajeGrupo(actual, g2);
                [actual[primero], actual[segundo]] = [actual[segundo], actual[primero]];
                const puntajeNuevo = puntajeActual - antes + puntajeGrupo(actual, g1) + puntajeGrupo(actual, g2);
                const temperatura = (priorizarReales ? 100 : 1) * (1.5 * (1 - it / iteraciones) + 0.05);
                if (puntajeNuevo >= puntajeActual || Math.random() < Math.exp((puntajeNuevo - puntajeActual) / temperatura)) {
                    puntajeActual = puntajeNuevo;
                    if (puntajeNuevo > mejorPuntaje) { mejorPuntaje = puntajeNuevo; mejorOrden = actual.slice(); }
                } else {
                    [actual[primero], actual[segundo]] = [actual[segundo], actual[primero]];
                }
            }
        }
        /** @type {Equipo[]} */
        const equipos = [];
        for (let g = 0; g < mejorOrden.length / 3; g++) {
            const trio = mejorOrden.slice(g * 3, g * 3 + 3);
            const { puntaje, posibilidades } = this.evaluarTrio(trio);
            equipos.push({ numero: g + 1, color: COLORES_GRUPO[g], colorTexto: COLORES_TEXTO_GRUPO[g], ids: trio.map(p => p.id), posibilidades, puntaje });
        }
        return equipos;
    }

    /** @param {string} sessionId */
    obtenerOCrearEspectador(sessionId) {
        const s = this.s;
        const existente = s.sesionesEspectador[sessionId];
        if (existente) return existente;

        const equiposActivos = [...new Map(Object.values(s.sesionesEspectador)
            .map(sesion => s.equiposEspectador[sesion.responseId])
            .map(e => [e.numero, e])).values()].sort((a, b) => a.numero - b.numero);
        const contarSesiones = (/** @type {number} */ numero) => Object.values(s.sesionesEspectador)
            .filter(sesion => s.equiposEspectador[sesion.responseId].numero === numero).length;
        const equipo = equiposActivos.find(e => contarSesiones(e.numero) < 3) ?? null;
        const numeroEquipo = equipo?.numero ?? (Math.max(5, ...equiposActivos.map(e => e.numero)) + 1);
        const idJugador = `esp:${sessionId}:player`;
        const sesionesEquipo = Object.values(s.sesionesEspectador)
            .filter(sesion => equipo !== null && s.equiposEspectador[sesion.responseId].numero === numeroEquipo);
        sesionesEquipo.push({ sessionId, responseId: idJugador });

        /** @type {Participante[]} */
        const integrantes = sesionesEquipo.map(sesion => {
            const datos = { ...this.crearDatosBot('ESP'), id: sesion.responseId, nombre: 'ESP' };
            return this.nuevoParticipante(sesion.responseId, 'ESP', false, datos);
        });
        const cantidadBots = 3 - integrantes.length;
        for (let i = 1; i <= cantidadBots; i++) {
            const idBot = `esp-bot:${numeroEquipo}:${i}`;
            const datos = { ...this.crearDatosBot(`Bot ${i}`), id: idBot, nombre: `Bot ${i}` };
            integrantes.push(this.nuevoParticipante(idBot, datos.nombre, true, datos));
        }

        const ids = integrantes.map(p => p.id);
        const { puntaje, posibilidades } = this.evaluarTrio(integrantes);
        /** @type {Equipo} */
        const nuevoEquipo = {
            numero: numeroEquipo, color: COLORES_GRUPO[(numeroEquipo - 1) % COLORES_GRUPO.length],
            colorTexto: COLORES_TEXTO_GRUPO[(numeroEquipo - 1) % COLORES_TEXTO_GRUPO.length], ids, posibilidades, puntaje
        };
        if (equipo)
            for (const idAnterior of equipo.ids.filter(id => id.startsWith('esp-bot:'))) {
                if (ids.includes(idAnterior)) continue;
                delete s.espectadores[idAnterior]; delete s.equiposEspectador[idAnterior];
                delete s.estados2[idAnterior]; delete s.estados3[idAnterior];
                delete s.puntos2[idAnterior]; delete s.puntos3[idAnterior];
            }
        for (const integrante of integrantes) {
            s.espectadores[integrante.id] = integrante;
            s.equiposEspectador[integrante.id] = nuevoEquipo;
            if (integrante.esBot) {
                s.listos[integrante.id] = true; s.listosSiguiente[integrante.id] = true;
                s.listosRondaTres[integrante.id] = true; s.listosRondaCuatro[integrante.id] = true;
            }
        }
        if (equipo) for (const id of equipo.ids) delete s.votos1[id];
        this.sembrarVotosBots(nuevoEquipo);
        if (equipo && s.preguntas3[this.firma(equipo)] && !s.preguntas3[this.firma(nuevoEquipo)])
            s.preguntas3[this.firma(nuevoEquipo)] = s.preguntas3[this.firma(equipo)];
        s.sesionesEspectador[sessionId] = { sessionId, responseId: idJugador };
        return s.sesionesEspectador[sessionId];
    }

    iniciarEleccionRepresentanteRondaDos() {
        this.s.votos2 = {};
        this.s.etapa = 5;
        for (const e of this.s.equipos) this.sembrarVotosBots(e, 2);
        for (const e of this.equiposEspectadorDistintos()) this.sembrarVotosBots(e, 2);
        if (!this.todosRepresentantesRondaDos()) return;
        this.s.etapa = 6;
        this.inicializarEstadosRondaTres();
        this.inicializarRespuestasBotsRondaTres();
    }

    /** @param {boolean} completarConBots @param {boolean} botsListos */
    iniciarPreRonda(completarConBots, botsListos) {
        const s = this.s;
        if (s.rondaIniciada) return conflict();
        this.limpiarLobby();
        let numeroBot = 1;
        while (completarConBots && Object.keys(s.lobby).length < 15) {
            const nombre = `bot${numeroBot++}`;
            const idBot = `bot:${nombre}`;
            if (s.lobby[idBot]) continue;
            s.lobby[idBot] = this.nuevoParticipante(idBot, nombre, true, this.crearDatosBot(nombre));
        }
        const cantidad = Object.keys(s.lobby).length;
        if (cantidad < 3 || cantidad > 15 || cantidad % 3 !== 0) return conflict();

        s.equipos = this.construirEquiposOptimos(Object.values(s.lobby), completarConBots);
        s.grupoPorParticipante = {}; s.listos = {}; s.votos1 = {}; s.votos2 = {};
        for (const e of s.equipos) {
            for (const id of e.ids) {
                s.grupoPorParticipante[id] = e.numero;
                if (botsListos && s.lobby[id]?.esBot) s.listos[id] = true;
            }
            this.sembrarVotosBots(e);
        }
        for (const e of this.equiposEspectadorDistintos()) this.sembrarVotosBots(e);
        for (const p of Object.values(s.espectadores)) if (p.esBot) s.listos[p.id] = true;

        s.previewIniciada = false; s.etapa = 0; s.inicioEtapa = null; s.segundosExtra = 0;
        s.listosSiguiente = {}; s.listosRondaTres = {}; s.listosRondaCuatro = {}; s.navegaciones = {};
        s.puntos1 = {}; s.puntos2 = {}; s.puntos3 = {}; s.estados4 = {}; s.puntos4 = {}; s.estados2 = {}; s.estados3 = {};
        s.vuelvenAEncuesta = {};
        s.rondaIniciada = true;
        return ok({ estado: 'ok', rondaIniciada: true, equipos: s.equipos.length, participantes: Object.keys(s.lobby).length });
    }

    iniciarRondaUno() {
        if (this.s.previewIniciada) return;
        this.s.inicioEtapa = this.ahora; this.s.etapa = 1; this.s.segundosExtra = 0; this.s.previewIniciada = true;
    }

    simularRespuestasBots() {
        const s = this.s;
        const simular = (/** @type {Equipo} */ equipo) => {
            const rep = this.representante(equipo);
            if (!rep || !this.participante(rep)?.esBot || equipo.ids.some(id => id in s.puntos1)) return;
            const puntos = azar(0, 4) * 50;
            for (const id of equipo.ids) if (!(id in s.puntos1)) s.puntos1[id] = puntos;
        };
        for (const e of s.equipos) simular(e);
        for (const p of Object.values(s.espectadores)) {
            if (!p.esBot) continue;
            const e = this.equipoDe(p.id);
            if (e) simular(e.equipo);
        }
    }

    // ---------------------------------------------------------------- ronda 2 (identificar capas)
    /** @param {string} id @param {boolean} adminPreview */
    claveVista(id, adminPreview) { return adminPreview ? `admin-preview:${id}` : id; }

    /** @param {string} responseId */
    crearEstadoRondaDos(responseId) {
        const e = this.equipoDe(responseId);
        if (!e) throw new Error(`No se encontró el equipo del integrante ${responseId}.`);
        const companeros = barajar(e.equipo.ids.filter(id => id !== responseId)).map(id => {
            const p = this.participante(id);
            if (!p) throw new Error(`No se encontró al integrante ${id}.`);
            return p;
        });
        if (companeros.length !== 2) throw new Error(`El equipo ${e.numero} no tiene dos compañeros para la ronda 2.`);
        return {
            challenges: companeros.map(companero => {
                const capaCambiada = elegir(CAPAS);
                const opciones = barajar([...barajar(CAPAS.filter(c => c !== capaCambiada)).slice(0, 3), capaCambiada]);
                return {
                    targetName: companero.nombre, datosOriginales: companero.datos,
                    datosCambiados: this.datosConCapaCambiada(companero.datos, capaCambiada),
                    changedLayer: capaCambiada, opciones,
                    startedUtc: /** @type {number | null} */ (null), answered: false, correct: false, points: 0
                };
            })
        };
    }

    /** SVG original, SVG con la capa cambiada y opciones; se calculan al vuelo y se guardan en caché. @param {any} desafio */
    vistaDesafio(desafio) {
        const clave = `r2|${JSON.stringify([desafio.datosOriginales, desafio.datosCambiados, desafio.changedLayer, desafio.opciones, this.deps.config()])}`;
        let vista = this.cacheSvg.get(clave);
        if (!vista) {
            const original = this.svgDatos(desafio.datosOriginales);
            const motor = this.svgDatos(desafio.datosCambiados);
            const cambiado = reemplazarGrupoSvg(original, desafio.changedLayer, obtenerGrupoSvg(motor, desafio.changedLayer));
            vista = {
                original, cambiado,
                choices: desafio.opciones.map((/** @type {string} */ capa) => ({
                    capa, nombre: NOMBRES_CAPAS[CAPAS.indexOf(capa)], efigie: svgSoloGrupo(cambiado, capa)
                }))
            };
            this.cacheSvg.set(clave, vista);
        }
        return vista;
    }

    /** @param {Datos} original @param {string} capa @returns {Datos} */
    datosConCapaCambiada(original, capa) {
        const c = { ...original };
        const otro = (/** @type {string[]} */ lista, /** @type {string} */ actual) => elegir(lista.filter(v => v !== actual));
        if (capa === 'anio') c.anio = otro(['1995', '1998', '2001', '2004', '2007', '2010', '2013'], original.anio);
        else if (capa === 'estaciones') c.estacion = otro(ESTACIONES, original.estacion);
        else if (capa === 'carrera') c.etapa = otro(ETAPAS_CARRERA, original.etapa);
        else if (capa === 'rol') c.rol = otro(ROLES, original.rol);
        else if (capa === 'historia') c.formato = otro(FORMATOS, original.formato);
        else if (capa === 'conexiones') c.grupal = original.grupal.toLowerCase().includes('grupal') ? 'Solo' : 'Grupal';
        else throw new Error(`La capa ${capa} no se puede modificar en la ronda 2.`);
        return c;
    }

    // ---------------------------------------------------------------- ronda 2 de juego (consignas de capas, "ronda 3" en el código)
    /** @param {Equipo} equipo @param {boolean} adminPreview */
    progresoRondaTres(equipo, adminPreview) {
        const clave = adminPreview ? `admin-preview:${this.firma(equipo)}` : this.firma(equipo);
        return this.s.progreso3[clave] ??= { currentQuestion: 0, hintRecipientId: null, hintQuestionIndex: -1, hintText: null, hintDelivered: false };
    }

    /** @param {string} responseId @param {boolean} adminPreview */
    estadoRondaTres(responseId, adminPreview) {
        const clave = this.claveVista(responseId, adminPreview);
        if (!this.s.estados3[clave]) {
            const e = this.equipoDe(responseId);
            if (!e) throw new Error(`No se encontró el equipo del integrante ${responseId}.`);
            const preguntas = this.s.preguntas3[this.firma(e.equipo)] ??= barajar(PREGUNTAS_RONDA_TRES);
            this.s.estados3[clave] = {
                notes: [], questions: preguntas, answers: preguntas.map(() => ''),
                hintsUsed: preguntas.map(() => false), submitted: false, points: 0
            };
        }
        return this.s.estados3[clave];
    }

    inicializarEstadosRondaTres() {
        const s = this.s;
        s.estados3 = {}; s.puntos3 = {}; s.preguntas3 = {}; s.progreso3 = {}; s.listosRondaCuatro = {};
        for (const p of Object.values(s.lobby)) if (!p.esBot) this.estadoRondaTres(p.id, false);
    }

    /** @param {any} estado */
    puntajeRondaTres(estado) {
        return estado.answers.reduce((/** @type {number} */ total, /** @type {string} */ r, /** @type {number} */ i) =>
            total + (r === estado.questions[i].answer ? (estado.hintsUsed[i] ? 50 : 100) : 0), 0);
    }

    /** @param {string} id @param {any} estado @param {boolean} adminPreview */
    finalizarRespuestasRondaTres(id, estado, adminPreview) {
        estado.submitted = true;
        estado.points = this.puntajeRondaTres(estado);
        if (!adminPreview) this.s.puntos3[id] = estado.points;
    }

    inicializarRespuestasBotsRondaTres() {
        for (const equipo of [...this.s.equipos, ...this.equiposEspectadorDistintos()]) {
            const integrantes = equipo.ids.map(id => this.participante(id)).filter(p => p !== undefined);
            if (integrantes.some(p => !p.esBot)) continue;
            for (const p of integrantes) {
                const estado = this.estadoRondaTres(p.id, false);
                if (estado.submitted) continue;
                estado.answers = estado.questions.map((/** @type {any} */ q) => q.answer);
                estado.hintsUsed = estado.questions.map(() => false);
                this.finalizarRespuestasRondaTres(p.id, estado, false);
            }
        }
    }

    /** @param {Equipo} equipo @param {number} indice @param {boolean} adminPreview */
    contarRespuestasPregunta(equipo, indice, adminPreview) {
        return equipo.ids.filter(id => {
            const p = this.participante(id);
            return p && !p.esBot && this.estadoRondaTres(id, adminPreview).answers[indice].length > 0;
        }).length;
    }

    /** @param {Equipo} equipo @param {number} indice @param {boolean} adminPreview */
    consensoPregunta(equipo, indice, adminPreview) {
        const reales = equipo.ids.map(id => this.participante(id)).filter(p => p && !p.esBot);
        if (reales.length === 0) return false;
        const respuestas = reales.map(p => this.estadoRondaTres(/** @type {Participante} */ (p).id, adminPreview).answers[indice]);
        if (respuestas.some(vacio) || new Set(respuestas).size !== 1) return false;
        for (const id of equipo.ids)
            if (!adminPreview && this.participante(id)?.esBot) this.estadoRondaTres(id, adminPreview).answers[indice] = respuestas[0];
        return true;
    }

    /** @param {Equipo} equipo @param {boolean} adminPreview */
    finalizarEquipoRondaTres(equipo, adminPreview) {
        if (!PREGUNTAS_RONDA_TRES.every((_, i) => this.consensoPregunta(equipo, i, adminPreview))) return false;
        for (const id of equipo.ids) {
            if (!this.participante(id)) continue;
            this.finalizarRespuestasRondaTres(id, this.estadoRondaTres(id, adminPreview), adminPreview);
        }
        return true;
    }

    todosLosEquiposFinalizados() {
        const equipos = [...new Map([...this.s.equipos, ...Object.values(this.s.equiposEspectador)].map(e => [this.firma(e), e])).values()];
        return equipos.length > 0 && equipos.every(e => e.ids.filter(id => this.participante(id))
            .every(id => this.s.estados3[id]?.submitted));
    }

    completarRespuestasRondaTresPorForzarAvance() {
        for (const equipo of [...this.s.equipos, ...this.equiposEspectadorDistintos()])
            for (const id of equipo.ids) {
                if (!this.participante(id)) continue;
                const estado = this.estadoRondaTres(id, false);
                if (!estado.submitted) this.finalizarRespuestasRondaTres(id, estado, false);
            }
    }

    // ---------------------------------------------------------------- ronda 3 de juego (encontrar personas, "ronda 4" en el código)
    inicializarRondaCuatro() {
        const s = this.s;
        s.estados4 = {}; s.puntos4 = {};
        const reales = Object.values(s.lobby).filter(p => !p.esBot).sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
        for (const participante of reales) {
            const e = this.equipoDe(participante.id);
            if (!e) continue;
            const opcionesPorRasgo = RASGOS_RONDA_CUATRO.map(rasgo => {
                /** @type {Map<string, { valor: string, integrantes: Participante[] }>} */
                const grupos = new Map();
                for (const persona of reales) {
                    const valor = texto(/** @type {any} */ (persona.datos)[rasgo]).trim();
                    if (!valor) continue;
                    const clave = valor.toUpperCase();
                    if (!grupos.has(clave)) grupos.set(clave, { valor, integrantes: [] });
                    /** @type {any} */ (grupos.get(clave)).integrantes.push(persona);
                }
                const lista = [...grupos.values()];
                const externos = lista.filter(g => g.integrantes.some(p => !e.equipo.ids.includes(p.id)));
                const deCompaneros = lista.filter(g => g.integrantes.some(p => p.id !== participante.id));
                const propios = lista.filter(g => g.integrantes.some(p => p.id === participante.id));
                return {
                    rasgo, prioridad: externos.length > 0 ? 0 : deCompaneros.length > 0 ? 1 : 2,
                    valores: externos.length > 0 ? externos : deCompaneros.length > 0 ? deCompaneros : propios,
                    orden: Math.random()
                };
            }).filter(item => item.valores.length > 0)
                .sort((a, b) => a.prioridad - b.prioridad || a.orden - b.orden)
                .slice(0, 3);
            if (opcionesPorRasgo.length !== 3)
                throw new Error(`No se pudieron crear tres consignas válidas para ${participante.nombre}.`);
            s.estados4[participante.id] = {
                tasks: opcionesPorRasgo.map(item => {
                    const valor = elegir(item.valores).valor;
                    return { property: item.rasgo, prompt: formatearConsignaRondaCuatro(item.rasgo, valor), value: valor, selectedParticipantId: '' };
                }),
                submitted: false, position: 0, points: 0
            };
        }
    }

    // ---------------------------------------------------------------- rutas HTTP
    crearRutas() {
        /** @type {Map<string, (req: Solicitud) => Respuesta | Promise<Respuesta>>} */
        const rutas = new Map();
        const get = (/** @type {string} */ p, /** @type {(req: Solicitud) => any} */ f) => rutas.set(`GET ${p}`, f);
        const post = (/** @type {string} */ p, /** @type {(req: Solicitud) => any} */ f) => rutas.set(`POST ${p}`, f);
        const s = this.s;
        const q = (/** @type {Solicitud} */ req, /** @type {string} */ k) => req.query.get(k) ?? '';
        const preview = (/** @type {Solicitud} */ req) => req.query.get('adminPreview') === '1';
        const cuerpo = (/** @type {Solicitud} */ req) => (req.body && typeof req.body === 'object' ? req.body : null);
        const realesSinBots = () => Object.values(this.s.lobby).filter(p => !p.esBot);

        get('/api/espectador/estado', req => {
            const sessionId = q(req, 'sessionId'), responseId = q(req, 'responseId');
            let espectador;
            if (!vacio(sessionId)) {
                if (!esIdSesion(sessionId)) return badRequest();
                espectador = this.obtenerOCrearEspectador(sessionId);
            } else if (!vacio(responseId)) {
                const partes = responseId.split(':');
                if (partes.length !== 3 || partes[0] !== 'esp' || !esIdSesion(partes[1]) ||
                    !this.s.espectadores[responseId] || !this.s.sesionesEspectador[partes[1]]) return notFound();
                espectador = this.s.sesionesEspectador[partes[1]];
            } else return badRequest();
            return ok({
                responseId: espectador.responseId,
                rondaIniciada: this.s.rondaIniciada,
                etapaRondaUno: this.s.rondaIniciada ? this.etapaNombre() : 'pre-ronda',
                segundosRestantes: this.s.rondaIniciada ? this.segundosRestantes() : 0
            });
        });

        get('/api/ronda-2/equipo', req => {
            const responseId = q(req, 'responseId');
            const e = this.equipoDe(responseId);
            if (!e) return notFound();
            if (!preview(req) && this.s.etapa !== 6 && req.query.get('consulta') !== '1') return conflict();
            return ok({
                efigies: e.equipo.ids.filter(id => this.participante(id)).map(id => {
                    const p = /** @type {Participante} */ (this.participante(id));
                    return { id, nombre: p.nombre, svg: this.svg(p), propia: id === responseId };
                })
            });
        });

        post('/api/ronda-3/notas', req => {
            const b = cuerpo(req), adminPreview = preview(req);
            if (!b || vacio(b.responseId) || !Array.isArray(b.notes)) return badRequest();
            if (!adminPreview && this.s.etapa !== 5) return conflict();
            if (!this.equipoDe(b.responseId)) return notFound();
            if (b.notes.some((/** @type {any} */ n) => typeof n !== 'string' || n.length > 15 || /[\r\n]/.test(n)))
                return badRequest({ error: 'Cada nota debe ocupar una sola línea de hasta 15 caracteres.' });
            if (req.query.get('consulta') === '1') return conflict();
            const estado = this.estadoRondaTres(b.responseId, adminPreview);
            estado.notes = b.notes.filter((/** @type {string} */ n) => n.length > 0);
            return ok({ estado: 'ok', notas: estado.notes });
        });

        get('/api/ronda-3/identificacion', req => {
            const responseId = q(req, 'responseId'), adminPreview = preview(req);
            if (!this.equipoDe(responseId)) return notFound();
            if (!adminPreview && this.s.etapa !== 6) return conflict({ error: 'La ronda ya no está activa.' });
            const estado = this.estadoRondaTres(responseId, adminPreview);
            if (!adminPreview || this.s.espectadores[responseId]) this.inicializarRespuestasBotsRondaTres();
            const e = this.equipoDe(responseId);
            if (!e) return notFound();
            const progreso = this.progresoRondaTres(e.equipo, adminPreview);
            const indice = progreso.currentQuestion;
            const consenso = adminPreview || this.consensoPregunta(e.equipo, indice, adminPreview);
            const representanteId = this.representante(e.equipo, 2);
            let pistaPopup = null;
            if (progreso.hintRecipientId === responseId && progreso.hintQuestionIndex === indice && !progreso.hintDelivered) {
                pistaPopup = progreso.hintText;
                progreso.hintDelivered = true;
            }
            const nombreRepresentante = representanteId ? this.nombre(representanteId) ?? null : null;
            const pistaEnviadaA = representanteId && responseId === representanteId && progreso.hintQuestionIndex === indice &&
                progreso.hintRecipientId !== null ? this.nombre(progreso.hintRecipientId) ?? null : null;
            const pregunta = estado.questions[indice];
            return ok({
                etapa: 'identificacion',
                efigie: this.svg(/** @type {Participante} */ (this.participante(responseId))),
                indiceActual: indice,
                cantidadPreguntas: estado.questions.length,
                esRepresentante: adminPreview || representanteId === responseId,
                pregunta: {
                    layer: pregunta.layer,
                    opciones: barajar(OPCIONES_RONDA_TRES),
                    respuesta: estado.answers[indice],
                    pistaUsada: estado.hintsUsed[indice]
                },
                pistaPopup, consenso, pistaEnviadaA,
                cantidadRespuestas: this.contarRespuestasPregunta(e.equipo, indice, adminPreview),
                totalParticipantes: e.equipo.ids.filter(id => { const p = this.participante(id); return p && !p.esBot; }).length,
                nombreRepresentante,
                respuestasPrevias: estado.answers.slice(0, indice),
                respondido: estado.submitted,
                puntos: estado.points,
                notas: estado.notes
            });
        });

        post('/api/ronda-3/responder', req => {
            const b = cuerpo(req), adminPreview = preview(req);
            if (!b || vacio(b.responseId) || !Number.isInteger(b.indice) || b.indice < 0 || b.indice >= 6 ||
                vacio(b.respuesta) || !OPCIONES_RONDA_TRES.includes(b.respuesta)) return badRequest();
            if (!adminPreview && this.s.etapa !== 6) return conflict({ error: 'La ronda ya no está activa.' });
            const e = this.equipoDe(b.responseId);
            if (!e) return notFound();
            const estado = this.estadoRondaTres(b.responseId, adminPreview);
            const progreso = this.progresoRondaTres(e.equipo, adminPreview);
            if (estado.submitted || b.indice !== progreso.currentQuestion) return conflict();
            estado.answers[b.indice] = b.respuesta;
            const consenso = adminPreview || this.consensoPregunta(e.equipo, progreso.currentQuestion, adminPreview);
            return ok({
                estado: 'ok', consenso,
                cantidadRespuestas: this.contarRespuestasPregunta(e.equipo, progreso.currentQuestion, adminPreview),
                totalParticipantes: e.equipo.ids.filter(id => { const p = this.participante(id); return p && !p.esBot; }).length
            });
        });

        post('/api/ronda-3/pista', req => {
            const b = cuerpo(req), adminPreview = preview(req);
            if (!b || vacio(b.responseId) || !Number.isInteger(b.indice) || b.indice < 0 || b.indice >= 6) return badRequest();
            if (!adminPreview && this.s.etapa !== 6) return conflict({ error: 'La ronda ya no está activa.' });
            const e = this.equipoDe(b.responseId);
            if (!e) return notFound();
            const representanteId = this.representante(e.equipo, 2);
            if (!representanteId || (!adminPreview && representanteId !== b.responseId)) return forbid();
            const progreso = this.progresoRondaTres(e.equipo, adminPreview);
            if (b.indice !== progreso.currentQuestion || (!adminPreview && this.consensoPregunta(e.equipo, progreso.currentQuestion, adminPreview)))
                return conflict({ error: 'La pista no está disponible en esta pregunta.' });
            const actividadMinima = this.ahora - 30_000;
            const destinatarios = adminPreview ? [b.responseId] : e.equipo.ids.filter(id => {
                const p = this.participante(id);
                return id !== representanteId && p && !p.esBot && (this.s.ultimaActividad[id] ?? 0) >= actividadMinima;
            });
            if (destinatarios.length === 0)
                return conflict({ error: 'No hay otro dispositivo activo en el equipo para mostrar la pista.' });
            const estadosEquipo = e.equipo.ids.map(id => this.estadoRondaTres(id, adminPreview));
            if (estadosEquipo.some(est => est.submitted || est.hintsUsed[b.indice]))
                return conflict({ error: 'La pista de esta pregunta ya fue utilizada.' });
            for (const est of estadosEquipo) est.hintsUsed[b.indice] = true;
            progreso.hintRecipientId = elegir(destinatarios);
            progreso.hintQuestionIndex = b.indice;
            progreso.hintText = estadosEquipo[0].questions[b.indice].hint;
            progreso.hintDelivered = false;
            return ok({ estado: 'ok', pistaUsada: true });
        });

        post('/api/ronda-3/navegar', req => {
            const b = cuerpo(req), adminPreview = preview(req);
            if (!b || vacio(b.responseId) || !['anterior', 'siguiente'].includes(b.accion)) return badRequest();
            if (!adminPreview && this.s.etapa !== 6) return conflict();
            const e = this.equipoDe(b.responseId);
            if (!e) return notFound();
            if (!adminPreview && this.representante(e.equipo, 2) !== b.responseId) return forbid();
            const progreso = this.progresoRondaTres(e.equipo, adminPreview);
            if (b.accion === 'siguiente' && !adminPreview && !this.consensoPregunta(e.equipo, progreso.currentQuestion, adminPreview))
                return conflict({ error: 'Esperá a que todos los integrantes respondan lo mismo antes de avanzar.' });
            if (b.accion === 'siguiente' && progreso.currentQuestion === PREGUNTAS_RONDA_TRES.length - 1) {
                if (!adminPreview && !this.finalizarEquipoRondaTres(e.equipo, adminPreview)) return conflict();
                if (!adminPreview && this.todosLosEquiposFinalizados()) { this.s.etapa = 7; this.s.inicioEtapa = null; }
                return ok({ estado: 'ok', finalizada: true, etapaRondaUno: this.etapaNombre(), resultadosDisponibles: !adminPreview });
            }
            const siguiente = b.accion === 'anterior' ? progreso.currentQuestion - 1 : progreso.currentQuestion + 1;
            if (siguiente < 0 || siguiente >= PREGUNTAS_RONDA_TRES.length) return conflict();
            Object.assign(progreso, { currentQuestion: siguiente, hintRecipientId: null, hintQuestionIndex: -1, hintText: null, hintDelivered: true });
            return ok({ estado: 'ok', finalizada: false, indiceActual: progreso.currentQuestion });
        });

        /** Promedio de puntajes de un equipo (como en los leaderboards de C#). @param {Equipo} equipo @param {Record<string, number>} puntajes */
        const promedioEquipo = (equipo, puntajes) => {
            const valores = equipo.ids.filter(id => id in puntajes).map(id => puntajes[id]);
            return { cantidad: valores.length, puntos: valores.length === 0 ? 0 : redondear(promedio(valores)) };
        };
        const ordenarEquipos = (/** @type {any[]} */ equipos) => equipos.sort((a, b) => b.puntos - a.puntos || a.numero - b.numero);

        get('/api/ronda-3/leaderboard', req => {
            const responseId = q(req, 'responseId'), adminPreview = preview(req);
            const e = this.equipoDe(responseId);
            if (!e) return notFound();
            if (!adminPreview && ![6, 7, 8].includes(this.s.etapa)) return conflict();
            if (!adminPreview && this.s.etapa === 6 && !this.s.estados3[responseId]?.submitted) return conflict();
            const equipos = this.s.equipos.map(eq => ({ numero: eq.numero, color: eq.color, colorTexto: eq.colorTexto, puntos: promedioEquipo(eq, this.s.puntos3).puntos }));
            if (this.s.espectadores[responseId]) {
                const eq = this.s.equiposEspectador[responseId];
                equipos.push({ numero: eq.numero, color: eq.color, colorTexto: eq.colorTexto, puntos: promedioEquipo(eq, this.s.puntos3).puntos });
            }
            const reales = realesSinBots();
            return ok({
                equipoPropio: e.numero,
                equipos: ordenarEquipos(equipos),
                listoContinuar: responseId in this.s.listosRondaCuatro,
                listosContinuar: reales.filter(p => p.id in this.s.listosRondaCuatro).length,
                totalParticipantes: reales.length,
                participantesRespondidos: reales.filter(p => this.s.estados3[p.id]?.submitted).length,
                puedeContinuar: [7, 8].includes(this.s.etapa),
                rondaTresIniciada: this.etapaNombre() === 'ronda-3'
            });
        });

        post('/api/ronda-3/listo-siguiente', req => {
            const b = cuerpo(req);
            if (!b || vacio(b.responseId)) return badRequest();
            if (!this.equipoDe(b.responseId)) return notFound();
            if (this.s.etapa !== 7) return conflict();
            this.s.listosRondaCuatro[b.responseId] = true;
            for (const p of Object.values(this.s.lobby)) if (p.esBot) this.s.listosRondaCuatro[p.id] = true;
            const todosListos = realesSinBots().every(p => p.id in this.s.listosRondaCuatro);
            if (todosListos) {
                this.s.etapa = 8; this.s.inicioEtapa = null; this.s.segundosExtra = 0;
                this.inicializarRondaCuatro();
            }
            return ok({ estado: 'ok', listo: true, rondaLista: todosListos });
        });

        get('/api/ronda-4/juego', req => {
            const responseId = q(req, 'responseId'), adminPreview = preview(req);
            if (![8, 9].includes(this.s.etapa)) return conflict();
            const participante = this.s.lobby[responseId];
            if (!participante || participante.esBot) return ok({ participa: false, esBot: participante?.esBot ?? false });
            const estado = this.s.estados4[responseId];
            if (!estado) return conflict();
            return ok({
                participa: true,
                enviado: estado.submitted,
                equipo: this.equipoDe(responseId)?.numero ?? 0,
                nombre: participante.nombre,
                opciones: Object.values(this.s.lobby).filter(o => o.id !== responseId)
                    .sort((a, b) => compararSinMayusculas(a.nombre, b.nombre))
                    .map(o => ({ id: o.id, nombre: o.nombre, esBot: o.esBot })),
                consignas: estado.tasks.map((/** @type {any} */ t, /** @type {number} */ indice) => ({ indice, consigna: t.prompt, seleccionado: t.selectedParticipantId })),
                puntos: estado.points,
                posicion: estado.position,
                adminPreview
            });
        });

        /** Registra el envío de la ronda 3 y calcula el puesto. @param {string} id @param {any} estado */
        const registrarEnvioRondaCuatro = (id, estado) => {
            estado.position = Object.values(this.s.estados4).filter(item => item.submitted).length + 1;
            estado.points = puntajeRondaCuatro(estado.position);
            estado.submitted = true;
            this.s.puntos4[id] = estado.points;
            const todosTerminaron = realesSinBots().every(p => this.s.estados4[p.id]?.submitted);
            if (todosTerminaron) this.s.etapa = 9;
            return todosTerminaron;
        };

        post('/api/ronda-4/responder', req => {
            const b = cuerpo(req);
            if (!b || vacio(b.responseId) || !Array.isArray(b.participantIds) || b.participantIds.length !== 3 ||
                b.participantIds.some(vacio)) return badRequest();
            if (this.s.etapa !== 8) return conflict();
            const participante = this.s.lobby[b.responseId];
            if (!participante || participante.esBot) return notFound();
            const respuestas = [];
            for (const id of b.participantIds) {
                if (id === b.responseId || !this.s.lobby[id]) return badRequest();
                respuestas.push(this.s.lobby[id]);
            }
            const estado = this.s.estados4[b.responseId];
            if (!estado) return conflict();
            if (estado.submitted) return conflict();
            const correcto = estado.tasks.every((/** @type {any} */ t, /** @type {number} */ i) =>
                texto(/** @type {any} */ (respuestas[i].datos)[t.property]).trim().toUpperCase() === String(t.value).toUpperCase());
            if (!correcto) return ok({ correcto: false });
            estado.tasks.forEach((/** @type {any} */ t, /** @type {number} */ i) => { t.selectedParticipantId = respuestas[i].id; });
            const todosTerminaron = registrarEnvioRondaCuatro(b.responseId, estado);
            return ok({ correcto: true, posicion: estado.position, puntos: estado.points, todosTerminaron });
        });

        post('/api/ronda-4/enviar', req => {
            const b = cuerpo(req);
            if (!b || vacio(b.responseId)) return badRequest();
            if (this.s.etapa !== 8) return conflict();
            const participante = this.s.lobby[b.responseId];
            if (!participante || participante.esBot) return notFound();
            const estado = this.s.estados4[b.responseId];
            if (!estado) return conflict();
            if (estado.submitted || estado.tasks.some((/** @type {any} */ t) => !t.selectedParticipantId)) return conflict();
            const todosTerminaron = registrarEnvioRondaCuatro(b.responseId, estado);
            return ok({ estado: 'ok', posicion: estado.position, puntos: estado.points, todosTerminaron });
        });

        get('/api/ronda-4/leaderboard', req => {
            const responseId = q(req, 'responseId'), adminPreview = preview(req);
            if (![8, 9].includes(this.s.etapa)) return conflict();
            const e = this.equipoDe(responseId);
            if (!e) return notFound();
            const jugador = this.s.lobby[responseId];
            if (!adminPreview && this.s.etapa === 8 && jugador && !jugador.esBot && !this.s.estados4[responseId]?.submitted) return conflict();
            const equipos = ordenarEquipos(this.s.equipos.map(eq => {
                const ronda1 = eq.ids.length === 0 ? 0 : redondear(promedio(eq.ids.map(id => this.s.puntos1[id] ?? 0)));
                // En Program.cs se sumaba PuntajesRondaDos (la ronda 2 antigua, que ya no se juega) y la columna
                // quedaba siempre en 0. La ronda 2 actual (identificación de capas) guarda sus puntos en puntos3.
                const ronda2 = promedioEquipo(eq, this.s.puntos3).puntos;
                const ronda3 = eq.ids.filter(id => { const p = this.s.lobby[id]; return p && !p.esBot; })
                    .reduce((total, id) => total + (this.s.puntos4[id] ?? 0), 0);
                return {
                    numero: eq.numero, color: eq.color, colorTexto: eq.colorTexto, ronda1, ronda2, ronda3,
                    puntos: ronda1 + ronda2 + ronda3,
                    terminaron: eq.ids.filter(id => this.s.estados4[id]?.submitted).length
                };
            }));
            return ok({
                equipoPropio: this.s.espectadores[responseId] ? 0 : e.numero,
                equipos,
                participantesRespondidos: Object.values(this.s.estados4).filter(item => item.submitted).length,
                totalParticipantes: realesSinBots().length,
                etapaRondaUno: this.etapaNombre(),
                rondaCompletada: this.s.etapa === 9
            });
        });

        get('/api/ronda-2/estado', req => {
            const responseId = q(req, 'responseId'), adminPreview = preview(req);
            if (!adminPreview && this.etapaNombre() !== 'ronda-2') return conflict();
            if (!this.equipoDe(responseId)) return notFound();
            const estado = this.s.estados2[this.claveVista(responseId, adminPreview)] ??= this.crearEstadoRondaDos(responseId);
            return ok({
                completados: estado.challenges.filter((/** @type {any} */ d) => d.answered).length,
                total: estado.challenges.length,
                desafios: estado.challenges.map((/** @type {any} */ d, /** @type {number} */ indice) => {
                    const vista = this.vistaDesafio(d);
                    return {
                        indice, targetName: d.targetName, efigieOriginal: vista.original, efigieCambiada: vista.cambiado,
                        opciones: vista.choices, iniciado: d.startedUtc !== null,
                        segundosRestantes: d.startedUtc !== null ? Math.max(0, 30 - Math.floor((this.ahora - d.startedUtc) / 1000)) : 30,
                        respondido: d.answered, points: d.points
                    };
                })
            });
        });

        post('/api/ronda-2/iniciar', req => {
            const b = cuerpo(req), adminPreview = preview(req);
            if (!b || vacio(b.responseId) || !Number.isInteger(b.indice ?? 0) || (b.indice ?? 0) < 0 || (b.indice ?? 0) > 1) return badRequest();
            const indice = b.indice ?? 0;
            if (!adminPreview && this.etapaNombre() !== 'ronda-2') return conflict();
            if (!this.equipoDe(b.responseId)) return notFound();
            const estado = this.s.estados2[this.claveVista(b.responseId, adminPreview)];
            if (!estado) return conflict();
            if (estado.challenges.slice(0, indice).some((/** @type {any} */ d) => !d.answered)) return conflict();
            const desafio = estado.challenges[indice];
            if (desafio.answered) return conflict();
            desafio.startedUtc ??= this.ahora;
            return ok({ estado: 'ok', segundosRestantes: Math.max(0, 30 - Math.floor((this.ahora - desafio.startedUtc) / 1000)) });
        });

        post('/api/ronda-2/responder', req => {
            const b = cuerpo(req), adminPreview = preview(req);
            const indice = b?.indice ?? 0;
            if (!b || vacio(b.responseId) || !Number.isInteger(indice) || indice < 0 || indice > 1) return badRequest();
            if (!adminPreview && this.etapaNombre() !== 'ronda-2') return conflict();
            if (!this.equipoDe(b.responseId)) return notFound();
            const estado = this.s.estados2[this.claveVista(b.responseId, adminPreview)];
            if (!estado) return conflict();
            if (estado.challenges.slice(0, indice).some((/** @type {any} */ d) => !d.answered)) return conflict();
            const desafio = estado.challenges[indice];
            if (desafio.answered || desafio.startedUtc === null || !desafio.opciones.includes(b.capa)) return conflict();
            const transcurrido = (this.ahora - desafio.startedUtc) / 1000;
            desafio.correct = transcurrido <= 30 && b.capa === desafio.changedLayer;
            desafio.points = desafio.correct ? 100 + redondear((100 * Math.max(0, 30 - transcurrido)) / 30) : 0;
            desafio.answered = true;
            const completados = estado.challenges.filter((/** @type {any} */ d) => d.answered).length;
            if (completados === estado.challenges.length && !adminPreview)
                this.s.puntos2[b.responseId] = estado.challenges.reduce((/** @type {number} */ t, /** @type {any} */ d) => t + d.points, 0);
            return ok({
                estado: 'ok', correcto: desafio.correct, puntos: desafio.points,
                puntosTotales: estado.challenges.filter((/** @type {any} */ d) => d.answered).reduce((/** @type {number} */ t, /** @type {any} */ d) => t + d.points, 0),
                completados
            });
        });

        get('/api/ronda-2/leaderboard', req => {
            const responseId = q(req, 'responseId'), adminPreview = preview(req);
            const e = this.equipoDe(responseId);
            if (!e) return notFound();
            const clave = this.claveVista(responseId, adminPreview);
            const estado = adminPreview ? (this.s.estados2[clave] ??= this.crearEstadoRondaDos(responseId)) : this.s.estados2[clave];
            if (!estado) return conflict();
            if (!adminPreview && estado.challenges.some((/** @type {any} */ d) => !d.answered)) return conflict();
            const equipos = this.s.equipos.map(eq => {
                const { cantidad, puntos } = promedioEquipo(eq, this.s.puntos2);
                return { numero: eq.numero, color: eq.color, colorTexto: eq.colorTexto, cantidadPuntajes: cantidad, puntos };
            });
            if (this.s.espectadores[responseId]) {
                const eq = this.s.equiposEspectador[responseId];
                const { cantidad, puntos } = promedioEquipo(eq, this.s.puntos2);
                equipos.push({ numero: eq.numero, color: eq.color, colorTexto: eq.colorTexto, cantidadPuntajes: cantidad, puntos });
            }
            return ok({
                equipoPropio: e.numero,
                equipos: ordenarEquipos(equipos),
                listoContinuar: responseId in this.s.listosRondaTres,
                listosContinuar: this.s.equipos.reduce((t, eq) => t + eq.ids.filter(id => {
                    const p = this.s.lobby[id];
                    return p && (p.esBot || id in this.s.listosRondaTres);
                }).length, 0),
                totalParticipantes: this.s.equipos.reduce((t, eq) => t + eq.ids.length, 0),
                etapaRondaUno: this.etapaNombre(),
                rondaTresIniciada: this.etapaNombre() === 'ronda-3'
            });
        });

        post('/api/admin/participante/navegar', req => {
            const b = cuerpo(req);
            if (!b || vacio(b.responseId) || vacio(b.etapa)) return badRequest();
            const participante = this.s.lobby[b.responseId];
            if (!participante) return notFound();
            const id = encodeURIComponent(b.responseId);
            /** @type {Record<string, string>} */
            const destinos = {
                'encuesta': '/encuesta?nueva=1',
                'efigie': `/efigie/${id}?adminPreview=1`,
                'lobby': `/lobby/${id}?adminPreview=1`,
                'pre-ronda-1': `/pre-ronda-1?id=${id}&adminPreview=1`,
                'ronda-1-memoria': `/ronda-1-preview?id=${id}&adminPreview=1`,
                'ronda-1-adivinanza': `/ronda-1-adivinar?id=${id}&adminPreview=1`,
                'ronda-1-resultados': `/ronda-1-leaderboard?id=${id}&adminPreview=1`,
                'ronda-2': `/ronda-2?id=${id}&adminPreview=1`,
                'ronda-2-representante': `/ronda-2-representante?ronda=2&id=${id}&adminPreview=1`,
                'ronda-2-equipo': `/ronda-2-equipo?id=${id}&adminPreview=1&consulta=1`,
                'ronda-2-resultados': `/ronda-2-leaderboard?id=${id}&adminPreview=1`,
                'ronda-3': `/ronda-3?id=${id}&adminPreview=1`
            };
            const destino = destinos[b.etapa];
            if (!destino) return badRequest();
            if (participante.esBot)
                return ok({ estado: 'preview', destino: destino.includes('adminPreview=1') ? destino : `${destino}${destino.includes('?') ? '&' : '?'}adminPreview=1` });
            this.s.navegaciones[b.responseId] = destino;
            return ok({ estado: 'enviado', nombre: participante.nombre });
        });

        get('/api/participante/navegacion', req => {
            const responseId = q(req, 'responseId');
            if (vacio(responseId)) return badRequest();
            this.s.ultimaActividad[responseId] = this.ahora;
            const destino = this.s.navegaciones[responseId];
            if (destino !== undefined) {
                delete this.s.navegaciones[responseId];
                delete this.s.vuelvenAInicio[responseId];
                if (destino.includes('reinicioSesion=1')) delete this.s.ultimaActividad[responseId];
                return ok({ destino });
            }
            return noContent();
        });

        get('/api/admin/sesion', () => {
            this.limpiarLobby();
            const participantes = Object.values(s.lobby).sort((a, b) => compararSinMayusculas(a.nombre, b.nombre))
                .map(p => ({ id: p.id, nombre: p.nombre, esBot: p.esBot }));
            const reales = [...new Map([...Object.values(s.lobby), ...Object.values(s.espectadores)]
                .filter(p => !p.esBot).map(p => [p.id, p])).values()];
            const rondaRepresentante = s.etapa === 5 ? 2 : 1;
            const votosActuales = this.votos(rondaRepresentante);
            const equipos = s.equipos.map(e => {
                const representanteId = this.representante(e, rondaRepresentante);
                return {
                    numero: e.numero, color: e.color, posibilidades: e.posibilidades, puntaje: e.puntaje,
                    representanteId,
                    representanteNombre: representanteId ? this.nombre(representanteId) ?? null : null,
                    integrantes: e.ids.map(id => {
                        const integrante = s.lobby[id];
                        if (!integrante) return { id, nombre: id, esBot: false, listo: false, votoRepresentanteNombre: null };
                        const voto = votosActuales[id];
                        return { id, nombre: integrante.nombre, esBot: integrante.esBot, listo: s.listos[id] === true,
                            votoRepresentanteNombre: voto !== undefined ? this.nombre(voto) ?? null : null };
                    })
                };
            });
            const contar = (/** @type {(p: Participante) => boolean} */ f) => reales.filter(f).length;
            /** @type {Record<number, () => number>} */
            const listosPorEtapa = {
                0: () => contar(p => p.id in s.votos1), 5: () => contar(p => p.id in s.votos2), 1: () => contar(p => p.id in s.listos),
                3: () => contar(p => p.id in s.listosSiguiente), 4: () => contar(p => p.id in s.listosRondaTres), 7: () => contar(p => p.id in s.listosRondaCuatro)
            };
            /** @type {Record<number, () => number>} */
            const respondieronPorEtapa = {
                2: () => contar(p => p.id in s.puntos1),
                6: () => contar(p => s.estados3[p.id]?.answers.some((/** @type {string} */ r) => !vacio(r)) ?? false),
                8: () => contar(p => s.estados4[p.id]?.submitted ?? false)
            };
            return ok({
                cantidad: Object.keys(s.lobby).length, capacidad: 15,
                rondaIniciada: s.rondaIniciada, preRondaActiva: s.rondaIniciada && !s.previewIniciada, previewIniciada: s.previewIniciada,
                etapaRondaUno: this.etapaNombre(), segundosRestantesRondaUno: this.segundosRestantes(),
                totalParticipantesReales: reales.length,
                listosEtapa: listosPorEtapa[s.etapa]?.() ?? 0,
                respondieronEtapa: respondieronPorEtapa[s.etapa]?.() ?? 0,
                participantes, equipos
            });
        });

        get('/api/lobby/estado', () => ok({ rondaIniciada: s.rondaIniciada, previewIniciada: s.previewIniciada, etapaRondaUno: this.etapaNombre() }));

        get('/api/preronda/estado', req => {
            const responseId = q(req, 'responseId'), adminPreview = preview(req);
            if (!s.rondaIniciada) return ok({ rondaIniciada: false, previewIniciada: false });
            const e = this.equipoDe(responseId);
            if (!e) return notFound();
            const ronda = req.query.get('ronda') === '2' ? 2 : 1;
            if (!adminPreview && ((ronda === 1 && s.etapa !== 0) || (ronda === 2 && s.etapa !== 5))) return conflict();
            const votos = this.votos(ronda);
            const miVoto = votos[responseId];
            const representanteId = this.representante(e.equipo, ronda);
            /** @type {Map<string, number>} */
            const conteo = new Map();
            for (const id of e.equipo.ids) if (id in votos) conteo.set(votos[id], (conteo.get(votos[id]) ?? 0) + 1);
            return ok({
                rondaIniciada: true, previewIniciada: s.previewIniciada, etapaRondaUno: this.etapaNombre(), ronda,
                grupo: e.numero, color: e.equipo.color, colorTexto: e.equipo.colorTexto,
                candidatosRepresentante: e.equipo.ids.filter(id => this.participante(id)).map(id => ({ id, nombre: this.nombre(id) })),
                tieneVoto: miVoto !== undefined, miVoto: miVoto ?? null,
                conteoVotos: [...conteo].map(([id, cantidad]) => ({ id, nombre: this.nombre(id) ?? id, cantidad })),
                representanteAsignado: representanteId !== null, representanteId,
                representanteNombre: representanteId ? this.nombre(representanteId) : null,
                todosLosRepresentantesAsignados: ronda === 2 && this.todosRepresentantesRondaDos(),
                total: e.equipo.ids.length
            });
        });

        post('/api/preronda/representante', req => {
            const b = cuerpo(req);
            if (!b || vacio(b.responseId) || vacio(b.representanteId)) return badRequest();
            const ronda = b.ronda ?? 1;
            if (!s.rondaIniciada || (ronda === 1 && s.etapa !== 0) || (ronda === 2 && s.etapa !== 5) || ronda < 1 || ronda > 2) return conflict();
            const e = this.equipoDe(b.responseId);
            if (!e) return notFound();
            const participante = this.participante(b.responseId);
            if (!e.equipo.ids.includes(b.representanteId) || !participante || participante.esBot) return badRequest();
            const votos = this.votos(ronda);
            votos[b.responseId] = b.representanteId;
            for (const id of e.equipo.ids) if (this.participante(id)?.esBot) votos[id] = b.representanteId;
            const representanteId = this.representante(e.equipo, ronda);
            const todosAsignados = ronda === 2 && this.todosRepresentantesRondaDos();
            if (todosAsignados) {
                s.etapa = 6;
                this.inicializarEstadosRondaTres();
                this.inicializarRespuestasBotsRondaTres();
            }
            return ok({
                estado: 'ok', representanteAsignado: representanteId !== null, representanteId,
                representanteNombre: representanteId ? this.nombre(representanteId) : null,
                todosLosRepresentantesAsignados: todosAsignados, etapaRondaUno: this.etapaNombre()
            });
        });

        get('/api/ronda-1/estado', req => {
            const responseId = q(req, 'responseId');
            if (!s.rondaIniciada) return ok({ rondaIniciada: false, previewIniciada: false, etapaRondaUno: 'pre-ronda' });
            const e = this.equipoDe(responseId);
            if (!e) return notFound();
            const incluirEfigies = req.query.get('incluirEfigies') === '1';
            const efigie = (/** @type {Participante} */ p) => (incluirEfigies ? this.svg(p) : '');
            const integrantes = e.equipo.ids.map(id => {
                const p = this.participante(id);
                return p ? { id, nombre: p.nombre, efigie: efigie(p) } : { id, nombre: id, efigie: '' };
            });
            const disponibles = s.espectadores[responseId]
                ? s.equiposEspectador[responseId].ids.map(id => /** @type {Participante} */ (this.participante(id)))
                : Object.values(s.lobby);
            const opciones = disponibles.slice().sort((a, b) => compararSinMayusculas(a.nombre, b.nombre))
                .map(p => ({ id: p.id, nombre: p.nombre, efigie: efigie(p) }));
            const representanteId = this.representante(e.equipo);
            const equipoPromedio = (/** @type {Equipo} */ eq) => ({
                numero: eq.numero, color: eq.color, colorTexto: eq.colorTexto,
                puntos: redondear(promedio(eq.ids.map(id => s.puntos1[id] ?? 0)))
            });
            const equipos = s.equipos.map(equipoPromedio);
            if (s.espectadores[responseId]) equipos.push(equipoPromedio(s.equiposEspectador[responseId]));
            const listoSiguienteDe = (/** @type {string} */ id) => { const p = this.participante(id); return p !== undefined && (p.esBot || id in s.listosSiguiente); };
            return ok({
                rondaIniciada: true, previewIniciada: s.previewIniciada, etapaRondaUno: this.etapaNombre(),
                equipoPropio: e.numero, color: e.equipo.color, colorTexto: e.equipo.colorTexto,
                esRepresentante: req.query.get('adminPreview') === '1' || (representanteId !== null && representanteId === responseId),
                representanteAsignado: representanteId !== null, representanteId,
                representanteNombre: representanteId ? this.nombre(representanteId) : null,
                segundosRestantes: this.segundosRestantes(),
                integrantes, opciones,
                yaRespondio: e.equipo.ids.some(id => id in s.puntos1),
                listoSiguiente: responseId in s.listosSiguiente,
                equipoListoSiguiente: e.equipo.ids.every(listoSiguienteDe),
                cantidadListosSiguiente: e.equipo.ids.filter(listoSiguienteDe).length,
                totalParticipantes: e.equipo.ids.length,
                equipos: ordenarEquipos(equipos)
            });
        });

        post('/api/ronda-1/adivinar', req => {
            const b = cuerpo(req);
            if (!b || vacio(b.responseId) || !Array.isArray(b.seleccionadas) || b.seleccionadas.length !== 3 ||
                b.seleccionadas.some(vacio) || new Set(b.seleccionadas).size !== 3) return badRequest();
            if (s.etapa !== 2) return conflict();
            const e = this.equipoDe(b.responseId);
            if (!e) return notFound();
            if (this.representante(e.equipo) !== b.responseId) return conflict();
            if (b.seleccionadas.some((/** @type {string} */ id) => !this.participante(id))) return badRequest();
            if (e.equipo.ids.some(id => id in s.puntos1)) return conflict();
            const aciertos = b.seleccionadas.filter((/** @type {string} */ id) => e.equipo.ids.includes(id)).length;
            const puntos = aciertos * 50;
            for (const id of e.equipo.ids) s.puntos1[id] = puntos;
            return ok({ estado: 'ok', aciertos, puntos, etapaRondaUno: this.etapaNombre() });
        });

        /** listo-siguiente de ronda 1 (etapa 3) y ronda 2 antigua (etapa 4) comparten la lógica. */
        const listoSiguiente = (/** @type {number} */ etapa, /** @type {'listosSiguiente' | 'listosRondaTres'} */ campo) => (/** @type {Solicitud} */ req) => {
            const b = cuerpo(req);
            if (!b || vacio(b.responseId)) return badRequest();
            if (s.etapa !== etapa) return conflict();
            const e = this.equipoDe(b.responseId);
            if (!e) return notFound();
            s[campo][b.responseId] = true;
            for (const id of e.equipo.ids) if (this.participante(id)?.esBot) s[campo][id] = true;
            const listoDe = (/** @type {string} */ id, /** @type {Participante | undefined} */ p) => p !== undefined && (p.esBot || id in s[campo]);
            return ok({
                estado: 'ok', listo: true,
                rondaLista: s.equipos.every(eq => eq.ids.every(id => listoDe(id, s.lobby[id]))),
                equipoListo: e.equipo.ids.every(id => listoDe(id, this.participante(id))),
                etapaRondaUno: this.etapaNombre()
            });
        };
        post('/api/ronda-1/listo-siguiente', listoSiguiente(3, 'listosSiguiente'));
        post('/api/ronda-2/listo-siguiente', listoSiguiente(4, 'listosRondaTres'));

        post('/api/preronda/listo', req => {
            const b = cuerpo(req);
            if (!b || vacio(b.responseId) || !s.rondaIniciada) return badRequest();
            if (!this.equipoDe(b.responseId)) return notFound();
            s.listos[b.responseId] = true;
            return ok({ estado: 'ok', previewIniciada: s.previewIniciada });
        });

        post('/api/admin/sesion/expulsar', req => {
            const b = cuerpo(req);
            if (!b || vacio(b.responseId)) return badRequest();
            if (s.rondaIniciada) return conflict();
            const participante = s.lobby[b.responseId];
            if (participante) {
                delete s.lobby[b.responseId];
                if (!participante.esBot) s.expulsados[b.responseId] = this.ahora + 30 * 60_000;
            }
            return ok({ estado: 'ok' });
        });

        post('/api/admin/sesion/simular', () => this.iniciarPreRonda(true, false));
        post('/api/admin/sesion/iniciar', () => this.iniciarPreRonda(false, false));
        post('/api/admin/sesion/forzar-inicio', () => this.iniciarPreRonda(false, true));

        post('/api/admin/preronda/forzar-listo', () => {
            if (!s.rondaIniciada) return conflict();
            if (s.equipos.some(e => this.representante(e) === null)) return conflict();
            for (const e of s.equipos) for (const id of e.ids) s.listos[id] = true;
            this.iniciarRondaUno();
            return ok({ estado: 'ok', previewIniciada: true });
        });

        post('/api/admin/ronda-1/agregar-tiempo', () => {
            if (![1, 2].includes(s.etapa)) return conflict();
            s.segundosExtra += 20;
            return ok({ estado: 'ok', segundosRestantes: this.segundosRestantes() });
        });

        const pasarAEtapa = (/** @type {number} */ etapa, inicio = /** @type {number | null} */ (null)) => {
            s.etapa = etapa; s.inicioEtapa = inicio; s.segundosExtra = 0;
        };
        const iniciarRondaDosIdentificacion = () => {
            pasarAEtapa(6);
            this.inicializarEstadosRondaTres();
            this.inicializarRespuestasBotsRondaTres();
            return ok({ estado: 'ok', etapaRondaUno: 'ronda-2-identificacion' });
        };
        const cerrarRondaDosIdentificacion = () => {
            this.completarRespuestasRondaTresPorForzarAvance();
            pasarAEtapa(7);
            return ok({ estado: 'ok', etapaRondaUno: 'ronda-2-leaderboard' });
        };

        post('/api/admin/ronda-1/saltar-tiempo', () => {
            if (s.etapa === 1) { pasarAEtapa(2, this.ahora); return ok({ estado: 'ok', etapaRondaUno: 'adivinanza' }); }
            if (s.etapa === 2) { this.simularRespuestasBots(); pasarAEtapa(3); return ok({ estado: 'ok', etapaRondaUno: 'leaderboard' }); }
            if (s.etapa === 5) return iniciarRondaDosIdentificacion();
            if (s.etapa === 6) return cerrarRondaDosIdentificacion();
            return conflict();
        });

        post('/api/admin/ronda-1/forzar-leaderboard', () => {
            if (s.etapa !== 2) return conflict();
            this.simularRespuestasBots();
            pasarAEtapa(3);
            return ok({ estado: 'ok', etapaRondaUno: 'leaderboard' });
        });

        post('/api/admin/ronda-1/forzar-siguiente', () => {
            if (s.etapa === 3) {
                for (const e of s.equipos) for (const id of e.ids) s.listosSiguiente[id] = true;
                s.inicioEtapa = this.ahora; s.segundosExtra = 0;
                this.iniciarEleccionRepresentanteRondaDos();
                return ok({ estado: 'ok', etapaRondaUno: this.etapaNombre() });
            }
            if (s.etapa === 5) return iniciarRondaDosIdentificacion();
            if (s.etapa === 6) return cerrarRondaDosIdentificacion();
            if (s.etapa === 7) {
                for (const p of [...Object.values(s.lobby), ...Object.values(s.espectadores)]) s.listosRondaCuatro[p.id] = true;
                pasarAEtapa(8);
                this.inicializarRondaCuatro();
                return ok({ estado: 'ok', etapaRondaUno: 'ronda-3' });
            }
            if (s.etapa === 8) {
                pasarAEtapa(9);
                for (const p of realesSinBots()) s.navegaciones[p.id] = `/ronda-3-leaderboard?id=${encodeURIComponent(p.id)}`;
                return ok({ estado: 'ok', etapaRondaUno: 'ronda-3-leaderboard' });
            }
            return conflict();
        });

        const enviarNavegacionATodos = (/** @type {string} */ rutaBase) => {
            for (const p of realesSinBots()) s.navegaciones[p.id] = `${rutaBase}${encodeURIComponent(p.id)}`;
        };
        const limpiarRondas = () => {
            s.listos = {}; s.listosSiguiente = {}; s.listosRondaTres = {}; s.listosRondaCuatro = {};
            s.puntos1 = {}; s.votos1 = {}; s.puntos2 = {}; s.puntos3 = {}; s.estados4 = {}; s.puntos4 = {}; s.estados2 = {}; s.estados3 = {};
        };
        const retroceder = () => {
            if (s.rondaIniciada && s.etapa > 0) {
                /** @type {Record<number, string>} */
                const rutasAnteriores = {
                    1: '/pre-ronda-1?id=', 2: '/ronda-1-preview?id=', 3: '/ronda-1-adivinar?id=', 4: '/ronda-1-leaderboard?id=',
                    5: '/ronda-1-leaderboard?id=', 6: '/ronda-2-representante?ronda=2&id=', 7: '/ronda-2?id=', 8: '/ronda-2-leaderboard?id=', 9: '/ronda-3?id='
                };
                const rutaAnterior = rutasAnteriores[s.etapa];
                if (!rutaAnterior) return conflict();
                const etapaAnterior = s.etapa === 5 ? 3 : s.etapa - 1;
                enviarNavegacionATodos(rutaAnterior);
                s.etapa = etapaAnterior;
                s.previewIniciada = etapaAnterior > 0;
                s.inicioEtapa = [1, 2, 5].includes(etapaAnterior) ? this.ahora : null;
                s.segundosExtra = 0;
                if (etapaAnterior === 8) {
                    s.puntos4 = {};
                    for (const estado of Object.values(s.estados4)) Object.assign(estado, { submitted: false, position: 0, points: 0 });
                }
                if (etapaAnterior === 5 || etapaAnterior === 6) {
                    for (const p of [...Object.values(s.lobby), ...Object.values(s.espectadores)]) {
                        const estado = s.estados3[p.id];
                        if (!estado) continue;
                        estado.submitted = false;
                        estado.points = 0;
                        if (etapaAnterior === 5 || p.esBot) {
                            estado.answers = estado.questions.map(() => '');
                            estado.hintsUsed = estado.questions.map(() => false);
                        }
                    }
                    if (etapaAnterior === 5) s.progreso3 = {};
                    s.puntos3 = {};
                }
                return ok({ estado: 'ok', etapaAnterior: ETAPAS[etapaAnterior], cambio: true });
            }
            if (s.previewIniciada) {
                enviarNavegacionATodos('/pre-ronda-1?id=');
                s.previewIniciada = false; s.etapa = 0; s.inicioEtapa = null; s.segundosExtra = 0;
                limpiarRondas();
                return ok({ estado: 'ok', etapaAnterior: 'pre-ronda-1', cambio: true });
            }
            if (s.rondaIniciada) {
                enviarNavegacionATodos('/lobby/');
                s.rondaIniciada = false; s.previewIniciada = false; s.etapa = 0; s.inicioEtapa = null; s.segundosExtra = 0;
                s.equipos = []; s.grupoPorParticipante = {};
                limpiarRondas();
                return ok({ estado: 'ok', etapaAnterior: 'lobby', cambio: true });
            }
            const idsEncuesta = realesSinBots().map(p => p.id);
            for (const id of idsEncuesta) { s.vuelvenAEncuesta[id] = 1; s.navegaciones[id] = '/encuesta?nueva=1'; }
            s.lobby = {};
            return ok({ estado: 'ok', etapaAnterior: 'encuesta', cambio: idsEncuesta.length > 0 });
        };
        post('/api/admin/sesion/retroceder', retroceder);
        post('/api/admin/sesion/volver-lobby', retroceder);

        post('/api/admin/sesion/terminar-todas', () => {
            const ahora = this.ahora;
            const activos = [...new Set([
                ...Object.entries(s.ultimaActividad).filter(([, t]) => t >= ahora - 30_000).map(([id]) => id),
                ...Object.values(s.lobby).filter(p => !p.esBot && ahora - p.lastSeen <= 90_000).map(p => p.id),
                ...Object.values(s.sesionesActivas).filter(x => ahora - x.lastSeen <= 90_000).map(x => x.responseId)
            ])];
            const nuevo = estadoInicial();
            nuevo.ultimaActividad = s.ultimaActividad;
            for (const id of activos) { nuevo.navegaciones[id] = '/?reinicioSesion=1'; nuevo.vuelvenAInicio[id] = 1; }
            Object.assign(s, nuevo);
            return ok({ estado: 'ok', participantes: activos.length });
        });

        post('/api/lobby/heartbeat', async req => {
            const b = cuerpo(req);
            if (!b || !esIdSesion(b.sessionId) || vacio(b.responseId)) return badRequest();
            const ahora = this.ahora;
            if (b.responseId in s.vuelvenAInicio) {
                delete s.vuelvenAInicio[b.responseId]; delete s.navegaciones[b.responseId]; delete s.ultimaActividad[b.responseId];
                return ok({ regresarInicio: true });
            }
            if (b.responseId in s.vuelvenAEncuesta) return ok({ regresarEncuesta: true });
            const expulsion = s.expulsados[b.responseId];
            if (expulsion !== undefined) {
                if (expulsion > ahora) return ok({ expulsado: true, cantidad: Object.keys(s.lobby).length });
                delete s.expulsados[b.responseId];
            }
            if (s.rondaIniciada) return ok({ rondaIniciada: true, previewIniciada: s.previewIniciada, etapaRondaUno: this.etapaNombre(), cantidad: Object.keys(s.lobby).length });
            const usuario = s.lobby[b.responseId]?.datos ?? await this.deps.buscarRespuesta(b.responseId);
            if (!usuario) return notFound();
            this.limpiarLobby();
            if (!s.lobby[usuario.id] && Object.keys(s.lobby).length >= 15)
                return ok({ salaCompleta: true, cantidad: Object.keys(s.lobby).length, rondaIniciada: false, expulsado: false });
            const existente = s.lobby[usuario.id];
            if (existente) { existente.sessionId = b.sessionId; existente.lastSeen = ahora; }
            else {
                s.lobby[usuario.id] = this.nuevoParticipante(usuario.id, usuario.nombre, false, usuario, b.sessionId);
                this.svg(s.lobby[usuario.id]); // precalcula el SVG para no hacerlo todo junto al iniciar
            }
            return ok({ cantidad: Object.keys(s.lobby).length, rondaIniciada: false, expulsado: false, salaCompleta: false });
        });

        post('/api/lobby/desconectar', req => {
            const b = cuerpo(req);
            if (!b || !esIdSesion(b.sessionId)) return badRequest();
            if (s.rondaIniciada) return noContent();
            if (s.lobby[b.responseId]?.sessionId === b.sessionId) delete s.lobby[b.responseId];
            return noContent();
        });

        post('/api/sesion/heartbeat', async req => {
            const b = cuerpo(req);
            if (!b || !esIdSesion(b.sessionId) || vacio(b.responseId)) return badRequest();
            const ahora = this.ahora;
            const respuestas = await this.deps.todasLasRespuestas();
            const actual = respuestas.find(r => r.id === b.responseId);
            if (!actual) { delete s.sesionesActivas[b.sessionId]; return notFound(); }
            s.sesionesActivas[b.sessionId] = { responseId: actual.id, lastSeen: ahora };
            for (const [id, x] of Object.entries(s.sesionesActivas)) if (ahora - x.lastSeen > 90_000) delete s.sesionesActivas[id];
            const activos = new Set(Object.values(s.sesionesActivas).map(x => x.responseId).filter(id => id !== actual.id));
            return ok({
                count: Object.keys(s.sesionesActivas).length,
                efigies: respuestas.filter(r => activos.has(r.id)).map(r => ({ id: r.id, nombre: r.nombre, svg: this.svgConMarco(r) }))
            });
        });

        post('/api/sesion/desconectar', req => {
            const b = cuerpo(req);
            if (!b || !esIdSesion(b.sessionId)) return badRequest();
            delete s.sesionesActivas[b.sessionId];
            return noContent();
        });

        return rutas;
    }

    /** SVG con marco y nombre, con las clases de animación (para /efigie/<id>). @param {Datos} datos */
    svgConMarco(datos) {
        const cfg = this.deps.config();
        const clave = `marco|${JSON.stringify(datos)}|${JSON.stringify(cfg)}`;
        let svg = this.cacheSvg.get(clave);
        if (!svg) {
            svg = inyectarAnimacion(generarSvg(datos, cfg, true));
            this.cacheSvg.set(clave, svg);
        }
        return svg;
    }
}

// --- Funciones de SVG por capas (ObtenerGrupoSvg, ReemplazarGrupoSvg, SvgSoloGrupo en Program.cs) ---
const REGEX_GRUPO = /<g\s+data-capa="([^"]+)"[^>]*>[\s\S]*?<\/g>/g;

/** @param {string} svg @param {string} capa */
export function obtenerGrupoSvg(svg, capa) {
    for (const m of svg.matchAll(REGEX_GRUPO)) if (m[1] === capa) return m[0];
    throw new Error(`El SVG generado no contiene la capa '${capa}'.`);
}

/** @param {string} svg @param {string} capa @param {string} grupoNuevo */
function reemplazarGrupoSvg(svg, capa, grupoNuevo) {
    let reemplazado = false;
    const resultado = svg.replace(REGEX_GRUPO, (m, nombre) => {
        if (nombre !== capa) return m;
        reemplazado = true;
        return grupoNuevo;
    });
    if (!reemplazado) throw new Error(`No se pudo reemplazar la capa '${capa}' del SVG.`);
    return resultado;
}

/** @param {string} svg @param {string} capa */
function svgSoloGrupo(svg, capa) {
    const viewBox = svg.match(/viewBox="([^"]+)"/);
    if (!viewBox) throw new Error('El SVG generado no contiene un viewBox.');
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox[1]}" width="100%" height="100%" fill="none" stroke="#000000" stroke-width="0.4" stroke-linecap="round" stroke-linejoin="round">${obtenerGrupoSvg(svg, capa)}</svg>`;
}

/** @param {string} svg */
function inyectarAnimacion(svg) {
    /** @type {Record<string, number>} */
    const capas = { anio: 1, estaciones: 2, carrera: 3, rol: 1, historia: 2, conexiones: 3 };
    return svg.replace(/<g data-capa="([^"]+)">/g, (m, nombre) => (capas[nombre] ? `<g class="rotar-capa-${capas[nombre]} capa-${nombre}">` : m));
}

/** @param {string} propiedad @param {string} valor */
function formatearConsignaRondaCuatro(propiedad, valor) {
    /** @type {Record<string, string>} */
    const consignas = {
        anio: `Encontrá a alguien que haya nacido en ${valor}.`,
        facultad: `Encontrá a alguien que pertenezca a la facultad «${valor}».`,
        etapa: `Encontrá a alguien cuya etapa de carrera o rol sea «${valor}».`,
        estacion: `Encontrá a alguien cuya estación favorita sea «${valor}».`,
        grupal: `Encontrá a alguien que prefiera trabajar «${valor}».`,
        formato: `Encontrá a alguien que prefiera disfrutar historias en formato «${valor}».`,
        rol: `Encontrá a alguien cuyo rol en un trabajo grupal sea «${valor}».`
    };
    const consigna = consignas[propiedad];
    if (!consigna) throw new Error(`Rasgo de ronda 3 desconocido: ${propiedad}.`);
    return consigna;
}

/** @param {number} posicion */
function puntajeRondaCuatro(posicion) {
    return posicion === 1 ? 250 : posicion === 2 ? 200 : posicion === 3 ? 150 : Math.max(0, 50 - (posicion - 4) * 5);
}
