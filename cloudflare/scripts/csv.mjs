// Lee respuestas_app.csv con el mismo criterio que AnalizarLineaCSV en Program.cs.
import fs from 'node:fs';

export const COLUMNAS = ['id', 'nombre', 'anio', 'facultad', 'etapa', 'estacion', 'grupal', 'formato', 'rol', 'device_id'];

export function leerRespuestasCsv(ruta) {
    const lineas = fs.readFileSync(ruta, 'utf8').replace(/^﻿/, '').split(/\r?\n/);
    return lineas.slice(1).filter(l => l.trim()).map(analizarLinea).filter(d => d.length >= 9)
        .map(d => Object.fromEntries(COLUMNAS.map((c, i) => [c, d[i] ?? ''])));
}

function analizarLinea(linea) {
    const partes = [];
    let entreComillas = false, actual = '';
    for (const c of linea) {
        if (c === '"') entreComillas = !entreComillas;
        else if (c === ',' && !entreComillas) { partes.push(actual.trim()); actual = ''; }
        else actual += c;
    }
    partes.push(actual.trim());
    return partes;
}
