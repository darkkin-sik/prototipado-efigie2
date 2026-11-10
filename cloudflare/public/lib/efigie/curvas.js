// @ts-check
// Reemplazos mínimos de RhinoCommon para la efigie. Una curva es una polilínea: [[x, y], ...].
// Las curvas cerradas repiten el primer punto al final, igual que en el generador original.

/** @typedef {[number, number]} Punto */
/** @typedef {Punto[]} Curva */

export const TAU = Math.PI * 2;
const PASO_ANGULAR = 0.05; // mismo criterio que crv.ToPolyline(0.05, ...) en GeneradorEfigie.cs

/** @param {Punto} c @param {number} r @returns {Curva} */
export function circulo(c, r) {
    const n = Math.max(32, Math.ceil(TAU / PASO_ANGULAR));
    const pts = [];
    for (let i = 0; i <= n; i++) {
        const t = -Math.PI / 2 + (TAU * (i % n)) / n;
        pts.push(/** @type {Punto} */ ([c[0] + r * Math.cos(t), c[1] + r * Math.sin(t)]));
    }
    return pts;
}

/** Arco que pasa por A, M y B (equivalente a new Arc(A, M, B)). @param {Punto} a @param {Punto} m @param {Punto} b @returns {Curva} */
export function arcoTresPuntos(a, m, b) {
    const d = 2 * (a[0] * (m[1] - b[1]) + m[0] * (b[1] - a[1]) + b[0] * (a[1] - m[1]));
    if (Math.abs(d) < 1e-12) return [a, b];
    const a2 = a[0] ** 2 + a[1] ** 2, m2 = m[0] ** 2 + m[1] ** 2, b2 = b[0] ** 2 + b[1] ** 2;
    const cx = (a2 * (m[1] - b[1]) + m2 * (b[1] - a[1]) + b2 * (a[1] - m[1])) / d;
    const cy = (a2 * (b[0] - m[0]) + m2 * (a[0] - b[0]) + b2 * (m[0] - a[0])) / d;
    const r = Math.hypot(a[0] - cx, a[1] - cy);
    const ang = (/** @type {Punto} */ p) => Math.atan2(p[1] - cy, p[0] - cx);
    const norm = (/** @type {number} */ x) => ((x % TAU) + TAU) % TAU;
    const aA = ang(a);
    const haciaB = norm(ang(b) - aA);
    const barrido = norm(ang(m) - aA) < haciaB ? haciaB : haciaB - TAU;
    const n = Math.max(4, Math.ceil(Math.abs(barrido) / PASO_ANGULAR));
    const pts = [];
    for (let i = 0; i <= n; i++) {
        const t = aA + (barrido * i) / n;
        pts.push(/** @type {Punto} */ ([cx + r * Math.cos(t), cy + r * Math.sin(t)]));
    }
    pts[0] = [a[0], a[1]];
    pts[n] = [b[0], b[1]];
    return pts;
}

/**
 * Curva B-spline periódica uniforme que interpola los puntos
 * (equivalente a Curve.CreateInterpolatedCurve(pts, grado, CurveKnotStyle.UniformPeriodic)).
 * @param {Curva} puntos curva cerrada (último punto = primero)
 * @param {2 | 3} grado
 * @returns {Curva}
 */
export function interpolarPeriodica(puntos, grado) {
    const q = puntos.slice(0, -1);
    const n = q.length;
    if (n < 3) return puntos.slice();
    // Grado 3: los puntos se interpolan en los nudos (1,4,1)/6. Grado 2: en el centro de cada tramo (1,6,1)/8.
    const [lado, centro] = grado === 3 ? [1 / 6, 4 / 6] : [1 / 8, 6 / 8];
    const cx = resolverCiclico(n, lado, centro, q.map(p => p[0]));
    const cy = resolverCiclico(n, lado, centro, q.map(p => p[1]));
    const P = (/** @type {number} */ i) => /** @type {Punto} */ ([cx[((i % n) + n) % n], cy[((i % n) + n) % n]]);

    /** @param {number} u parámetro global en [0, n) */
    const evaluar = u => {
        const j = Math.floor(u), t = u - j;
        let pesos, ctrl;
        if (grado === 3) {
            const t2 = t * t, t3 = t2 * t;
            pesos = [(1 - t) ** 3 / 6, (3 * t3 - 6 * t2 + 4) / 6, (-3 * t3 + 3 * t2 + 3 * t + 1) / 6, t3 / 6];
            ctrl = [P(j - 1), P(j), P(j + 1), P(j + 2)];
        } else {
            pesos = [(1 - t) ** 2 / 2, (-2 * t * t + 2 * t + 1) / 2, (t * t) / 2];
            ctrl = [P(j - 1), P(j), P(j + 1)];
        }
        let x = 0, y = 0;
        pesos.forEach((w, k) => { x += w * ctrl[k][0]; y += w * ctrl[k][1]; });
        return /** @type {Punto} */ ([x, y]);
    };

    const inicio = grado === 3 ? 0 : 0.5; // así la curva empieza en el primer punto, como en Rhino
    const muestrasPorTramo = 16;
    const salida = [];
    for (let k = 0; k < n * muestrasPorTramo; k++) salida.push(evaluar(inicio + k / muestrasPorTramo));
    salida.push(salida[0]);
    return salida;
}

/** Resuelve el sistema cíclico lado·x[i-1] + centro·x[i] + lado·x[i+1] = d[i]. */
function resolverCiclico(/** @type {number} */ n, /** @type {number} */ lado, /** @type {number} */ centro, /** @type {number[]} */ d) {
    const A = Array.from({ length: n }, (_, i) => {
        const fila = new Array(n + 1).fill(0);
        fila[i] += centro;
        fila[(i - 1 + n) % n] += lado;
        fila[(i + 1) % n] += lado;
        fila[n] = d[i];
        return fila;
    });
    for (let c = 0; c < n; c++) {
        let piv = c;
        for (let f = c + 1; f < n; f++) if (Math.abs(A[f][c]) > Math.abs(A[piv][c])) piv = f;
        [A[c], A[piv]] = [A[piv], A[c]];
        for (let f = 0; f < n; f++) {
            if (f === c) continue;
            const k = A[f][c] / A[c][c];
            if (k !== 0) for (let j = c; j <= n; j++) A[f][j] -= k * A[c][j];
        }
    }
    return A.map((fila, i) => fila[n] / fila[i]);
}

/** Primera intersección (según el orden de la curva) entre la curva y el segmento o-p. @returns {Punto | null} */
export function interseccionSegmento(/** @type {Curva} */ curva, /** @type {Punto} */ o, /** @type {Punto} */ p) {
    const rx = p[0] - o[0], ry = p[1] - o[1];
    for (let i = 0; i < curva.length - 1; i++) {
        const a = curva[i], b = curva[i + 1];
        const sx = b[0] - a[0], sy = b[1] - a[1];
        const den = rx * sy - ry * sx;
        if (Math.abs(den) < 1e-12) continue;
        const qx = a[0] - o[0], qy = a[1] - o[1];
        const t = (qx * sy - qy * sx) / den;
        const u = (qx * ry - qy * rx) / den;
        if (t >= -1e-9 && t <= 1 + 1e-9 && u >= -1e-9 && u <= 1 + 1e-9)
            return [a[0] + sx * u, a[1] + sy * u];
    }
    return null;
}

/** Divide una curva cerrada en `cantidad` puntos equidistantes por longitud de arco (Curve.DivideByCount). */
export function dividirPorCantidad(/** @type {Curva} */ curva, /** @type {number} */ cantidad) {
    const acum = [0];
    for (let i = 1; i < curva.length; i++)
        acum.push(acum[i - 1] + Math.hypot(curva[i][0] - curva[i - 1][0], curva[i][1] - curva[i - 1][1]));
    const total = acum[acum.length - 1];
    /** @type {Curva} */
    const pts = [];
    let seg = 0;
    for (let k = 0; k < cantidad; k++) {
        const s = (total * k) / cantidad;
        while (seg < curva.length - 2 && acum[seg + 1] < s) seg++;
        const largo = acum[seg + 1] - acum[seg] || 1;
        const t = (s - acum[seg]) / largo;
        const a = curva[seg], b = curva[seg + 1];
        pts.push([a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t]);
    }
    return pts;
}

/** Largo de la diagonal del bounding box (GetBoundingBox(true).Diagonal.Length). */
export function diagonalCaja(/** @type {Curva} */ curva) {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const [x, y] of curva) {
        if (x < minX) minX = x; if (x > maxX) maxX = x;
        if (y < minY) minY = y; if (y > maxY) maxY = y;
    }
    return Math.hypot(maxX - minX, maxY - minY);
}

/** @param {Curva} curva @param {Punto} c @param {number} factor */
export function escalar(curva, c, factor) {
    return curva.map(([x, y]) => /** @type {Punto} */ ([c[0] + (x - c[0]) * factor, c[1] + (y - c[1]) * factor]));
}
