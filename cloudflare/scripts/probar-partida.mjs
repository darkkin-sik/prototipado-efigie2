// Juega una partida completa contra un servidor (por defecto `npm run dev`) usando el WebSocket del transporte.
// Uso: node scripts/probar-partida.mjs [http://localhost:8787]
import assert from 'node:assert/strict';
import fs from 'node:fs';

const base = process.argv[2] ?? 'http://localhost:8787';
// Contraseña de admin: variable ADMIN_KEY o la de .dev.vars
const clave = process.env.ADMIN_KEY ?? (fs.existsSync('.dev.vars') ? fs.readFileSync('.dev.vars', 'utf8').match(/^ADMIN_KEY=(.*)$/m)?.[1]?.trim() : '') ?? '';
const login = await fetch(`${base}/api/admin/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ clave }) });
assert.equal(login.status, 200, 'contraseña de admin incorrecta (ADMIN_KEY)');
const cookie = (login.headers.get('set-cookie') ?? '').split(';')[0];
const ws = new WebSocket(base.replace(/^http/, 'ws') + '/api/ws', { headers: { cookie } });
await new Promise((ok, error) => { ws.onopen = ok; ws.onerror = error; });
let siguiente = 0;
const pendientes = new Map();
ws.onmessage = e => { const m = JSON.parse(e.data); pendientes.get(m.id)?.(m); pendientes.delete(m.id); };
async function api(method, url, body) {
    const id = ++siguiente;
    const m = await new Promise(ok => { pendientes.set(id, ok); ws.send(JSON.stringify({ id, method, url, body: body ? JSON.stringify(body) : null })); });
    return { status: m.status, body: m.body ? JSON.parse(m.body) : null };
}
async function ok(method, url, body) {
    const r = await api(method, url, body);
    assert.equal(r.status, 200, `${method} ${url} → ${r.status} ${JSON.stringify(r.body)}`);
    return r.body;
}
const paso = texto => console.log('✔', texto);

await ok('POST', '/api/admin/sesion/terminar-todas');
const reales = (await (await fetch(`${base}/api/datos`)).json()).slice(0, 3);
assert.equal(reales.length, 3, 'hacen falta al menos 3 respuestas en D1 (npm run db:importar-csv)');
const sesion = i => String(i).repeat(32);
// Tras "terminar todas", el primer heartbeat devuelve regresarInicio (el celular vuelve a la bienvenida); se reintenta.
for (const [i, r] of reales.entries())
    while ((await ok('POST', '/api/lobby/heartbeat', { sessionId: sesion(i + 1), responseId: r.id })).cantidad === undefined);
paso(`3 jugadores en el lobby: ${reales.map(r => r.nombre).join(', ')}`);

let t = performance.now();
assert.equal((await ok('POST', '/api/admin/sesion/simular')).participantes, 15);
paso(`simular con bots y armar 5 equipos (${Math.round(performance.now() - t)} ms)`);
const admin = await ok('GET', '/api/admin/sesion');
const equipo = admin.equipos.find(e => e.integrantes.some(i => i.id === reales[0].id));
assert.deepEqual(equipo.integrantes.map(i => i.id).sort(), reales.map(r => r.id).sort());
paso('los 3 reales quedaron en el mismo equipo');

const [a, b, c] = reales.map(r => r.id);
for (const id of [a, b, c]) await ok('POST', '/api/preronda/representante', { responseId: id, representanteId: a, ronda: 1 });
await ok('POST', '/api/admin/preronda/forzar-listo');
t = performance.now();
const r1 = await ok('GET', `/api/ronda-1/estado?responseId=${b}&incluirEfigies=1`);
assert.equal(r1.opciones.length, 15);
paso(`ronda 1 – memoria con 15 efigies (${Math.round(performance.now() - t)} ms)`);
await ok('POST', '/api/admin/ronda-1/saltar-tiempo');
assert.equal((await ok('POST', '/api/ronda-1/adivinar', { responseId: a, seleccionadas: [a, b, c] })).puntos, 150);
await ok('POST', '/api/admin/ronda-1/saltar-tiempo');
for (const id of [a, b, c]) await ok('POST', '/api/ronda-1/listo-siguiente', { responseId: id });
paso(`ronda 1 – adivinanza: 3 aciertos = 150 puntos; equipos: ${(await ok('GET', `/api/ronda-1/estado?responseId=${a}`)).equipos.map(e => e.puntos).join(', ')}`);

await ok('POST', '/api/admin/ronda-1/forzar-siguiente');
for (const id of [a, b, c]) await ok('POST', '/api/preronda/representante', { responseId: id, representanteId: b, ronda: 2 });
for (let i = 0; i < 6; i++) {
    const vista = await ok('GET', `/api/ronda-3/identificacion?responseId=${c}`);
    assert.equal(vista.indiceActual, i);
    // Sin acceso al estado interno: se prueban opciones hasta que el representante puede avanzar
    for (const opcion of vista.pregunta.opciones) {
        for (const id of [a, b, c]) await ok('POST', '/api/ronda-3/responder', { responseId: id, indice: i, respuesta: opcion });
        const nav = await api('POST', '/api/ronda-3/navegar', { responseId: b, accion: 'siguiente' });
        if (nav.status === 200) break;
    }
}
const lb2 = await ok('GET', `/api/ronda-3/leaderboard?responseId=${a}`);
paso(`ronda 2 – identificación terminada; puntos del equipo: ${lb2.equipos.find(e => e.numero === lb2.equipoPropio).puntos}`);

for (const id of [a, b, c]) await ok('POST', '/api/ronda-3/listo-siguiente', { responseId: id });
for (const id of [a, b, c]) {
    const juego = await ok('GET', `/api/ronda-4/juego?responseId=${id}`);
    assert.equal(juego.consignas.length, 3);
}
await ok('POST', '/api/admin/ronda-1/forzar-siguiente');
const final = await ok('GET', `/api/ronda-4/leaderboard?responseId=${a}`);
assert.equal(final.etapaRondaUno, 'ronda-3-leaderboard');
paso(`ronda 3 y leaderboard final: ${final.equipos.map(e => `equipo ${e.numero} = ${e.puntos}`).join(', ')}`);
assert.match((await ok('GET', `/api/participante/navegacion?responseId=${a}`)).destino, /ronda-3-leaderboard/);
paso('la navegación enviada por admin llega al jugador');

const http = await fetch(`${base}/api/lobby/estado`).then(r => r.json());
assert.equal(http.etapaRondaUno, 'ronda-3-leaderboard');
paso('el mismo estado se ve por HTTP (sin WebSocket)');
await ok('POST', '/api/admin/sesion/terminar-todas');
ws.close();
console.log('Partida completa OK');
