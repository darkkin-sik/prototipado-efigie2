// Genera comparacion.html: cada respuesta del CSV con la efigie del port JS y, si existe,
// la referencia exportada desde Rhino (rhino/referencias/<id>.svg).
import fs from 'node:fs';
import path from 'node:path';
import { leerRespuestasCsv } from './csv.mjs';
import { generarSvg } from '../public/lib/efigie/svg.js';

const raiz = path.resolve(import.meta.dirname, '..');
const rutaCsv = process.argv[2] ?? path.join(raiz, '..', 'respuestas_app.csv');
const rutaConfig = path.join(raiz, '..', 'config.json');
const config = fs.existsSync(rutaConfig) ? JSON.parse(fs.readFileSync(rutaConfig, 'utf8')) : {};
const dirReferencias = path.join(raiz, 'rhino', 'referencias');

const filas = leerRespuestasCsv(rutaCsv).map(r => {
    const rutaRef = path.join(dirReferencias, `${r.id}.svg`);
    const referencia = fs.existsSync(rutaRef) ? fs.readFileSync(rutaRef, 'utf8') : '<p class="vacio">sin referencia de Rhino</p>';
    return `<section><h2>${r.nombre} · ${r.anio} · ${r.estacion} · ${r.etapa} · ${r.formato} · ${r.grupal}</h2>
<div class="par"><figure>${generarSvg(r, config, false)}<figcaption>JS (p5 / Cloudflare)</figcaption></figure>
<figure>${referencia}<figcaption>Rhino</figcaption></figure></div></section>`;
});

const html = `<!doctype html><meta charset="utf-8"><title>Comparación de efigies</title>
<style>body{font:14px system-ui;margin:16px;display:grid;grid-template-columns:repeat(auto-fill,minmax(420px,1fr));gap:16px}
h2{font-size:12px;font-weight:600}.par{display:grid;grid-template-columns:1fr 1fr;gap:8px}figure{margin:0}
svg{width:100%;height:auto;aspect-ratio:1;border:1px solid #ddd}.vacio{aspect-ratio:1;display:grid;place-items:center;border:1px dashed #ccc;color:#999;margin:0}</style>
${filas.join('\n')}`;
const salida = path.join(raiz, 'comparacion.html');
fs.writeFileSync(salida, html);
console.log(`${filas.length} efigies → ${salida}`);
