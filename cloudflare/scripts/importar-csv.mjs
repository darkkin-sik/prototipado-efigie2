// Importa respuestas_app.csv (y config.json si existe) a D1.
// Uso: npm run db:importar-csv [-- ruta.csv] [-- --remoto]
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { leerRespuestasCsv, COLUMNAS } from './csv.mjs';

const raiz = path.resolve(import.meta.dirname, '..');
const args = process.argv.slice(2);
const remoto = args.includes('--remoto');
const rutaCsv = args.find(a => !a.startsWith('--')) ?? path.join(raiz, '..', 'respuestas_app.csv');
const rutaConfig = path.join(path.dirname(rutaCsv), 'config.json');

const sql = (v) => `'${String(v).replaceAll("'", "''")}'`;
const filas = leerRespuestasCsv(rutaCsv);
const sentencias = filas.map(f => `INSERT OR REPLACE INTO respuestas (${COLUMNAS.join(', ')}) VALUES (${COLUMNAS.map(c => sql(f[c])).join(', ')});`);
if (fs.existsSync(rutaConfig))
    sentencias.push(`INSERT INTO config_versiones (config, nota) VALUES (${sql(fs.readFileSync(rutaConfig, 'utf8'))}, 'importada de config.json');`);

const archivo = path.join(raiz, '.wrangler', 'importar.sql');
fs.mkdirSync(path.dirname(archivo), { recursive: true });
fs.writeFileSync(archivo, sentencias.join('\n'));
// En Windows npx es un .cmd y necesita shell; la ruta va entre comillas por si tiene espacios.
const windows = process.platform === 'win32';
execFileSync(windows ? 'npx.cmd' : 'npx', ['wrangler', 'd1', 'execute', 'efigie', remoto ? '--remote' : '--local', '--file', windows ? `"${archivo}"` : archivo],
    { stdio: 'inherit', cwd: raiz, shell: windows });
console.log(`${filas.length} respuestas importadas (${remoto ? 'remoto' : 'local'})`);
