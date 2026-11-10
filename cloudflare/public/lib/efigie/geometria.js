// @ts-check
// Port de GeneradorEfigie.cs sin Rhino: respuestas + config → capas de polilíneas.
// No depende de p5 ni del DOM, así lo usan el navegador (p5 / SVG) y el Worker.
import { TAU, circulo, arcoTresPuntos, interpolarPeriodica, interseccionSegmento, dividirPorCantidad, diagonalCaja, escalar } from './curvas.js';

/** @typedef {import('./curvas.js').Punto} Punto */
/** @typedef {import('./curvas.js').Curva} Curva */
/**
 * @typedef {{ id?: string, nombre?: string, anio?: string, facultad?: string, etapa?: string,
 *   estacion?: string, grupal?: string, formato?: string, rol?: string }} DatosRespuesta
 * @typedef {typeof CONFIG_POR_DEFECTO} ConfigEfigie
 * @typedef {{ anio: Curva[], estaciones: Curva[], carrera: Curva[], rol: Curva[], historia: Curva[], conexiones: Curva[] }} Capas
 * @typedef {{ capas: Capas, marco: Curva, puntoTexto: Punto, texto: string, facultad: string }} Efigie
 */

export const CONFIG_POR_DEFECTO = {
    RadioBaseTexto: 34.0,
    RadioBaseAnio: 27.0,
    RadioBaseEstaciones: 14.0,
    EscalaCopia: 50.0,
    CantidadRayos: 15,
    OffsetGrupal: 4,
    RadioBaseCarrera: 36.79,
    EscalaAnilloMinimo: 75.0,
    RadioBaseRol: 25.0,
    RadioBaseHistoria: 21.85,
    MultiplicadorDistancia: 0.80,
    TamanoIcono: 1.70,
    EscalaGlobal: 100.0
};

/** Rangos sugeridos para los sliders de la vista admin. */
export const RANGOS_CONFIG = {
    RadioBaseTexto: [5, 60, 0.5],
    RadioBaseAnio: [5, 45, 0.5],
    RadioBaseEstaciones: [3, 30, 0.5],
    EscalaCopia: [0, 100, 1],
    CantidadRayos: [3, 60, 1],
    OffsetGrupal: [0, 20, 1],
    RadioBaseCarrera: [5, 48, 0.01],
    EscalaAnilloMinimo: [10, 100, 1],
    RadioBaseRol: [5, 60, 0.5],
    RadioBaseHistoria: [5, 45, 0.05],
    MultiplicadorDistancia: [0.1, 2, 0.01],
    TamanoIcono: [0.2, 5, 0.05],
    EscalaGlobal: [10, 100, 1]
};

export const NOMBRES_CAPAS = /** @type {const} */ (['anio', 'estaciones', 'carrera', 'rol', 'historia', 'conexiones']);

/** @param {Partial<ConfigEfigie> | null | undefined} cfg @returns {ConfigEfigie} */
export function normalizarConfig(cfg) {
    const salida = { ...CONFIG_POR_DEFECTO };
    for (const clave of Object.keys(CONFIG_POR_DEFECTO)) {
        const valor = Number(/** @type {any} */ (cfg ?? {})[clave]);
        if (Number.isFinite(valor)) /** @type {any} */ (salida)[clave] = valor;
    }
    return salida;
}

/** @param {DatosRespuesta} datos @param {Partial<ConfigEfigie>} [configParcial] @returns {Efigie} */
export function generarEfigie(datos, configParcial) {
    const cfg = normalizarConfig(configParcial);
    const anioNum = parseInt(String(datos.anio ?? '').replace(/\D/g, ''), 10) || 0;
    const etapa = mapearEtapa(datos.etapa ?? '');
    const estacion = mapearEstacion(datos.estacion ?? '');
    const esGrupal = (datos.grupal ?? '').toLowerCase().includes('grupal');
    const formato = mapearFormato(datos.formato ?? '');
    const rol = mapearRol(datos.rol ?? '');
    /** @type {Punto} */
    const centro = [0, 0];

    const { marco, puntoTexto } = generarMarco(centro, cfg.RadioBaseTexto > 0 ? cfg.RadioBaseTexto : 15.0);
    const anio = generarAnio(anioNum, centro, cfg.RadioBaseAnio > 0 ? cfg.RadioBaseAnio : 17.0);
    const estaciones = generarEstaciones(centro, estacion, cfg.RadioBaseEstaciones > 0 ? cfg.RadioBaseEstaciones : 15.0, cfg.EscalaCopia);
    const conexiones = estaciones.length >= 2
        ? generarConexiones(centro, estaciones, esGrupal, Math.round(cfg.CantidadRayos), Math.round(cfg.OffsetGrupal))
        : [];
    const carrera = generarCarrera(centro, etapa, cfg.RadioBaseCarrera > 0 ? cfg.RadioBaseCarrera : 45.0, cfg.EscalaAnilloMinimo);
    const curvaRol = generarRol(centro, rol, cfg.RadioBaseRol > 0 ? cfg.RadioBaseRol : 18.0);
    const historia = estaciones.length >= 1
        ? generarHistoria(centro, estaciones, formato, cfg.RadioBaseHistoria > 0 ? cfg.RadioBaseHistoria : 24.85, cfg.MultiplicadorDistancia, cfg.TamanoIcono)
        : [];

    // Escala global (el marco no se escala, igual que en el original)
    const factor = cfg.EscalaGlobal > 0 && cfg.EscalaGlobal <= 100 ? cfg.EscalaGlobal / 100 : 1;
    const esc = (/** @type {Curva[]} */ curvas) => curvas.map(c => escalar(c, centro, factor));

    return {
        capas: {
            anio: esc(anio ? [anio] : []),
            estaciones: esc(estaciones),
            carrera: esc(carrera),
            rol: esc([curvaRol]),
            historia: esc(historia),
            conexiones: esc(conexiones)
        },
        marco,
        puntoTexto,
        texto: datos.nombre ?? '',
        facultad: datos.facultad ?? ''
    };
}

// --- Mapeos de respuestas ---
/** @param {string} txt */
function mapearEtapa(txt) {
    for (let i = 1; i <= 6; i++) if (txt.includes(String(i))) return i;
    if (txt.includes('No Docente')) return 8;
    if (txt.includes('Docente')) return 7;
    return 1;
}
/** @param {string} txt */
function mapearEstacion(txt) {
    const t = txt.toLowerCase();
    if (t.includes('primavera')) return 1; if (t.includes('verano')) return 2;
    if (t.includes('otoño')) return 3; if (t.includes('invierno')) return 4;
    return 1;
}
/** @param {string} txt */
function mapearFormato(txt) {
    const t = txt.toLowerCase();
    if (t.includes('película') || t.includes('pelicula')) return 1;
    if (t.includes('serie')) return 2; if (t.includes('libro')) return 3;
    if (t.includes('videojuego')) return 4; if (t.includes('teatro')) return 5;
    return 1;
}
/** @param {string} txt */
function mapearRol(txt) {
    const t = txt.toLowerCase();
    if (t.includes('proponer')) return 1; if (t.includes('organizar')) return 2;
    if (t.includes('materializar')) return 3; if (t.includes('mejorar')) return 4;
    return 1;
}

// --- Módulos geométricos (mismos nombres y lógica que GeneradorEfigie.cs) ---
const polar = (/** @type {Punto} */ c, /** @type {number} */ r, /** @type {number} */ t) => /** @type {Punto} */ ([c[0] + r * Math.cos(t), c[1] + r * Math.sin(t)]);
const sumar = (/** @type {Punto} */ a, /** @type {Punto} */ v, k = 1) => /** @type {Punto} */ ([a[0] + v[0] * k, a[1] + v[1] * k]);

/** @param {Punto} c @param {number} radioBase */
function generarMarco(c, radioBase) {
    const m = radioBase * 1.3;
    /** @type {Curva} */
    const marco = [[c[0] - m, c[1] - m], [c[0] + m, c[1] - m], [c[0] + m, c[1] + m], [c[0] - m, c[1] + m], [c[0] - m, c[1] - m]];
    /** @type {Punto} */
    const puntoTexto = [c[0] - m + radioBase * 0.1, c[1] - m + radioBase * 0.1];
    return { marco, puntoTexto };
}

/** @param {number} anio @param {Punto} c @param {number} radioBase @returns {Curva | null} */
function generarAnio(anio, c, radioBase) {
    const N = anio % 100;
    if (anio < 2000) {
        if (N === 0) return circulo(c, radioBase);
        const offset = -Math.PI / 2;
        const base = Array.from({ length: N }, (_, i) => polar(c, radioBase, offset + (TAU * i) / N));
        /** @type {Curva} */
        const unida = [];
        for (let i = 0; i < N; i++) {
            const A = base[i], B = base[(i + 1) % N];
            /** @type {Punto} */
            const M = [(A[0] + B[0]) / 2, (A[1] + B[1]) / 2];
            const lm = Math.hypot(M[0] - c[0], M[1] - c[1]) || 1;
            const dir = /** @type {Punto} */ ([(M[0] - c[0]) / lm, (M[1] - c[1]) / lm]);
            const petalo = arcoTresPuntos(A, sumar(M, dir, Math.hypot(B[0] - A[0], B[1] - A[1]) / 2), B);
            unida.push(...(i === 0 ? petalo : petalo.slice(1)));
        }
        return unida;
    }
    if (N === 0) return circulo(c, radioBase);
    /** @type {Curva} */
    const pts = [];
    if (N === 1) {
        const inicio = -Math.PI / 2 - Math.PI / 4, fin = -Math.PI / 2 + (5 * Math.PI) / 4;
        for (let i = 0; i <= 50; i++) pts.push(polar(c, radioBase, inicio + (fin - inicio) * (i / 50)));
        pts.push(pts[0]);
    } else if (N === 2) {
        for (let i = 0; i <= 25; i++) pts.push(polar(c, radioBase, -Math.PI / 4 + (Math.PI / 2) * (i / 25)));
        for (let i = 0; i <= 25; i++) pts.push(polar(c, radioBase, (3 * Math.PI) / 4 + (Math.PI / 2) * (i / 25)));
        pts.push(pts[0]);
    } else {
        for (let i = 0; i <= N; i++) pts.push(polar(c, radioBase, -Math.PI / 2 + (TAU * i) / N));
    }
    return pts;
}

/** @param {Punto} c @param {number} estacion @param {number} radioBase @param {number} escalaCopia @returns {Curva[]} */
function generarEstaciones(c, estacion, radioBase, escalaCopia) {
    let activacion = 1.0, subdivisiones = 20, grado = 1;
    if (estacion === 1) { activacion = 0.15; subdivisiones = 18; grado = 2; }
    else if (estacion === 2) { activacion = 1.0; subdivisiones = 20; grado = 3; }
    else if (estacion === 3) { activacion = 0.5; subdivisiones = 10; grado = 1; }
    else if (estacion === 4) { activacion = 0.33; subdivisiones = 16; grado = 1; }

    const mov = radioBase * activacion;
    /** @type {Curva} */
    const pts = [];
    for (let i = 0; i < subdivisiones; i++) {
        const r = i % 2 === 0 ? radioBase + mov : radioBase;
        pts.push(polar(c, r, -Math.PI / 2 + (TAU * i) / subdivisiones));
    }
    pts.push(pts[0]);
    const curva = grado === 1 ? pts : interpolarPeriodica(pts, /** @type {2 | 3} */ (grado));
    const resultado = [curva];
    if (escalaCopia > 0.001) resultado.push(escalar(curva, c, escalaCopia / 100));
    return resultado;
}

/** @param {Punto} c @param {Curva[]} estaciones @param {boolean} grupal @param {number} cantidadRayos @param {number} offsetGrupal @returns {Curva[]} */
function generarConexiones(c, estaciones, grupal, cantidadRayos, offsetGrupal) {
    const [ext, int] = estaciones;
    if (cantidadRayos < 1) return [];
    const maxRadio = diagonalCaja(ext) + 10;
    /** @type {Punto[]} */
    const ptsExt = [], ptsInt = [];
    for (let i = 0; i < cantidadRayos; i++) {
        const t = -Math.PI / 2 + (TAU * i) / cantidadRayos;
        const fin = polar(c, maxRadio, t);
        ptsExt.push(interseccionSegmento(ext, c, fin) ?? ext[0]);
        ptsInt.push(interseccionSegmento(int, c, fin) ?? int[0]);
    }
    const n = ptsExt.length;
    const salto = offsetGrupal % n === 0 ? 1 : ((offsetGrupal % n) + n) % n;
    return ptsInt.map((p, i) => [p, grupal ? ptsExt[(i + salto) % n] : ptsExt[i]]);
}

/** @param {Punto} c @param {number} etapa @param {number} radioBase @param {number} escalaAnilloMinimo @returns {Curva[]} */
function generarCarrera(c, etapa, radioBase, escalaAnilloMinimo) {
    /** @type {Curva[]} */
    const salida = [];
    if (etapa >= 1 && etapa <= 6) {
        const radioMinimo = radioBase * (escalaAnilloMinimo / 100);
        if (etapa === 1) salida.push(circulo(c, radioBase));
        else {
            const espaciado = (radioBase - radioMinimo) / (etapa - 1);
            for (let i = 0; i < etapa; i++) salida.push(circulo(c, Math.max(0.1, radioBase - i * espaciado)));
        }
    } else if (etapa === 7 || etapa === 8) {
        const amplitud = radioBase * 0.15;
        /** @type {Curva} */
        const pts = [];
        for (let i = 0; i < 36; i++) {
            const r = radioBase + (i % 2 === 0 ? amplitud : etapa === 7 ? -(amplitud * 0.5) : 0);
            pts.push(polar(c, r, -Math.PI / 2 + (TAU * i) / 36));
        }
        pts.push(pts[0]);
        salida.push(etapa === 8 ? pts : interpolarPeriodica(pts, 3));
    }
    return salida;
}

/** @param {Punto} c @param {number} rol @param {number} radioBase @returns {Curva} */
function generarRol(c, rol, radioBase) {
    const r = radioBase * 0.2;
    const rot = -Math.PI / 2;
    /** @type {Curva} */
    const pts = [];
    if (rol === 1) for (let i = 0; i <= 100; i++) {
        const t = rot + (TAU * i) / 100;
        pts.push([c[0] + r * Math.cos(t) ** 3, c[1] + r * Math.sin(t) ** 3]);
    }
    else if (rol === 2) for (let i = 0; i <= 6; i++) pts.push(polar(c, r, rot + Math.PI / 6 + (TAU * i) / 6));
    else if (rol === 3) for (let i = 0; i <= 3; i++) pts.push(polar(c, r, rot + (TAU * i) / 3));
    else if (rol === 4) for (let i = 0; i <= 100; i++) {
        const s = (TAU * i) / 100;
        pts.push(polar(c, r * Math.cos(2 * s), rot + s));
    }
    return pts;
}

/** @param {Punto} c @param {Curva[]} estaciones @param {number} formato @param {number} radioBase @param {number} multiplicador @param {number} tam @returns {Curva[]} */
function generarHistoria(c, estaciones, formato, radioBase, multiplicador, tam) {
    const mayor = estaciones.reduce((a, b) => (diagonalCaja(b) > diagonalCaja(a) ? b : a));
    const res = 200;
    const pts = dividirPorCantidad(mayor, res);
    /** @type {Punto[]} */
    const direcciones = [];
    const dists = pts.map(p => Math.hypot(p[0] - c[0], p[1] - c[1]));
    const d = (/** @type {number} */ i) => dists[((i % res) + res) % res];
    for (let i = 0; i < pts.length; i++) {
        if (d(i) <= d(i - 1) && d(i) <= d(i - 2) && d(i) <= d(i + 1) && d(i) <= d(i + 2) && (d(i) < d(i - 2) || d(i) < d(i + 2))) {
            const l = dists[i] || 1;
            /** @type {Punto} */
            const dir = [(pts[i][0] - c[0]) / l, (pts[i][1] - c[1]) / l];
            const angulo = (/** @type {Punto} */ v) => Math.acos(Math.max(-1, Math.min(1, v[0] * dir[0] + v[1] * dir[1])));
            if (!direcciones.some(v => angulo(v) < 0.1)) direcciones.push(dir);
        }
    }
    if (direcciones.length === 0)
        for (let i = 0; i < 8; i++) direcciones.push(polar([0, 0], 1, -Math.PI / 2 + (TAU * i) / 8));

    /** @type {Curva[]} */
    const iconos = [];
    const distancia = radioBase * multiplicador;
    for (const dir of direcciones) {
        /** @type {Punto} */
        const perp = [-dir[1], dir[0]];
        const a = sumar(c, dir, distancia);
        const p = (/** @type {Punto} */ base, kp = 0, kd = 0) => sumar(sumar(base, perp, kp), dir, kd);
        if (formato === 1) {
            iconos.push([a, p(a, tam, tam * 0.6), p(a, tam, -tam * 0.6), a]);
            iconos.push([a, p(a, -tam, tam * 0.6), p(a, -tam, -tam * 0.6), a]);
        } else if (formato === 2) {
            for (const c1 of [p(a, tam * 0.4), p(a, -tam * 0.4)])
                iconos.push([p(c1, 0, tam), p(c1, tam * 0.6), p(c1, 0, -tam), p(c1, -tam * 0.6), p(c1, 0, tam)]);
        } else if (formato === 3) {
            iconos.push([p(a, tam, tam), p(a, 0, -tam * 0.5), p(a, -tam, tam)]);
        } else if (formato === 4) {
            iconos.push([p(a, 0, tam), p(a, 0, -tam)]);
            iconos.push([p(a, tam), p(a, -tam)]);
        } else if (formato === 5) {
            const escenario = p(a, 0, tam * 0.5);
            iconos.push([p(escenario, tam), p(escenario, -tam)]);
            /** @type {Curva} */
            const arco1 = [], arco2 = [];
            for (let j = 0; j <= 12; j++) {
                const kd = -Math.sin((Math.PI * j) / 12), kp = Math.cos((Math.PI * j) / 12);
                arco1.push(p(escenario, kp * tam, kd * tam));
                arco2.push(p(escenario, kp * tam * 0.5, kd * tam * 0.5));
            }
            iconos.push(arco1, arco2);
        }
    }
    return iconos;
}
