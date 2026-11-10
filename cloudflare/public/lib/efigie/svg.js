// @ts-check
// Port de ExportarASvg (GeneradorEfigie.cs) e InyectarAnimacionRotacionSvg (Program.cs).
import { generarEfigie } from './geometria.js';

/** @typedef {import('./geometria.js').Efigie} Efigie */
/** @typedef {import('./curvas.js').Curva} Curva */

const COLORES_FACULTAD = {
    'Ciencias Económicas': '#6064A3',
    'Derecho': '#7C171F',
    'Filosofía y Letras': '#459BDA',
    'Ingeniería y Ciencias Agrarias': '#84B444',
    'Psicología y Psicopedagogía': '#CE2D7B',
    'Música': '#F9DB52',
    'Ciencias Médicas': '#6EB392',
    'Ciencias Sociales': '#E27646',
    'Teología': '#85A8C8',
    'Staff / Docentes': '#133463',
    'Arquitectura y Diseño': '#8B1C31'
};

/** @param {string} facultad */
export function colorFacultad(facultad) {
    return /** @type {Record<string, string>} */ (COLORES_FACULTAD)[facultad] ?? '#000000';
}

/** Redondea a 3 decimales sin ceros de relleno (más liviano que toFixed para SVG grandes). */
const f3 = (/** @type {number} */ n) => String(Math.round(n * 1000) / 1000 || 0);

/** En la efigie +Y va hacia arriba; en SVG hacia abajo, por eso se invierte Y. */
function curvaAPath(/** @type {Curva} */ curva) {
    let d = '';
    for (let i = 0; i < curva.length; i++) d += (i === 0 ? 'M ' : ' L ') + f3(curva[i][0]) + ' ' + f3(-curva[i][1]);
    return d;
}

/** @param {string} texto */
function escaparHtml(texto) {
    return texto.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] ?? c);
}

/** @param {Efigie} efigie @param {{ incluirMarcoYNombre?: boolean }} [opciones] */
export function efigieASvg(efigie, { incluirMarcoYNombre = true } = {}) {
    const { capas } = efigie;
    const viewBox = incluirMarcoYNombre ? '-75 -75 150 150' : '-50 -50 100 100';
    const lineas = [
        `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox}" width="100%" height="100%" style="background:#ffffff;">`,
        '<g fill="none" stroke="#000000" stroke-width="0.4" stroke-linecap="round" stroke-linejoin="round">'
    ];
    if (incluirMarcoYNombre) lineas.push(`  <path d="${curvaAPath(efigie.marco)}" stroke-width="0.3" />`);

    const grupo = (/** @type {string} */ nombre, /** @type {Curva[]} */ curvas, /** @type {string | null} */ stroke = null) => {
        lineas.push(`  <g data-capa="${nombre}"${stroke ? ` stroke="${stroke}"` : ''}>`);
        for (const c of curvas) lineas.push(`    <path d="${curvaAPath(c)}" />`);
        lineas.push('  </g>');
    };
    grupo('anio', capas.anio);
    grupo('estaciones', capas.estaciones);
    grupo('carrera', capas.carrera, colorFacultad(efigie.facultad));
    grupo('rol', capas.rol);
    grupo('historia', capas.historia);

    lineas.push('  <g data-capa="conexiones">');
    for (const [a, b] of capas.conexiones)
        lineas.push(`    <line x1="${f3(a[0])}" y1="${f3(-a[1])}" x2="${f3(b[0])}" y2="${f3(-b[1])}" />`);
    lineas.push('  </g>');
    lineas.push('</g>');

    if (incluirMarcoYNombre && efigie.texto)
        lineas.push(`<text x="${f3(efigie.puntoTexto[0])}" y="${f3(-efigie.puntoTexto[1])}" font-family="sans-serif" font-size="3" fill="#000000">${escaparHtml(efigie.texto)}</text>`);
    lineas.push('</svg>');
    return lineas.join('\n') + '\n';
}

/** Equivalente a GeneradorEfigie.GenerarSVG(datos, cfg, incluirMarcoYNombre). */
export function generarSvg(/** @type {import('./geometria.js').DatosRespuesta} */ datos, /** @type {any} */ cfg, incluirMarcoYNombre = true) {
    return efigieASvg(generarEfigie(datos, cfg), { incluirMarcoYNombre });
}

/** Velocidad de rotación de cada capa (1: 18 s horario, 2: 12 s antihorario, 3: 8 s horario). */
export const ROTACION_CAPAS = { anio: 1, estaciones: 2, carrera: 3, rol: 1, historia: 2, conexiones: 3 };

/**
 * Port de InyectarAnimacionRotacionSvg. Igual que en C#, solo reemplaza `<g data-capa="x">` sin otros
 * atributos, por lo que la capa "carrera" (que lleva stroke) queda sin rotar.
 * @param {string} svg
 */
export function inyectarAnimacionRotacion(svg) {
    return svg.replace(/<g data-capa="([^"]+)">/g, (coincidencia, nombre) => {
        const capa = /** @type {Record<string, number>} */ (ROTACION_CAPAS)[nombre];
        return capa ? `<g class="rotar-capa-${capa} capa-${nombre}">` : coincidencia;
    });
}
