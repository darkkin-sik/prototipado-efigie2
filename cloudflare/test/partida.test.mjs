// Simula una partida completa contra worker/juego.js: `node --test test/`
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Juego, estadoInicial } from '../worker/juego.js';

const respuestas = [
    { id: 'a1', nombre: 'Ana', anio: '2004', facultad: 'Derecho', etapa: '2', estacion: 'Verano', grupal: 'Solo', formato: 'Libro', rol: 'Proponer (Generar ideas iniciales)' },
    { id: 'b2', nombre: 'Beto', anio: '1999', facultad: 'Teología', etapa: '5', estacion: 'Invierno', grupal: 'Grupal', formato: 'Serie', rol: 'Mejorar (Revisar y pulir detalles)' },
    { id: 'c3', nombre: 'Caro', anio: '2006', facultad: 'Música', etapa: 'Docente', estacion: 'Otoño', grupal: 'Solo', formato: 'Teatro', rol: 'Organizar (Estructurar y planificar)' }
];
const juego = new Juego(estadoInicial(), {
    config: () => ({}),
    buscarRespuesta: async id => respuestas.find(r => r.id === id) ?? null,
    todasLasRespuestas: async () => respuestas
});
const sesion = i => String(i).repeat(32).slice(0, 32);
async function api(method, url, body) {
    const u = new URL(url, 'http://x');
    const r = await juego.manejar({ method, path: u.pathname, query: u.searchParams, body: body ?? null });
    return r;
}
const okJson = async (method, url, body) => {
    const r = await api(method, url, body);
    assert.equal(r.status, 200, `${method} ${url} → ${r.status} ${JSON.stringify(r.body)}`);
    return r.body;
};

test('partida completa con 3 jugadores reales y bots', async () => {
    for (const [i, r] of respuestas.entries())
        assert.equal((await okJson('POST', '/api/lobby/heartbeat', { sessionId: sesion(i + 1), responseId: r.id })).cantidad, i + 1);

    const simular = await okJson('POST', '/api/admin/sesion/simular');
    assert.equal(simular.participantes, 15);
    assert.equal(simular.equipos, 5);

    // Los reales quedan juntos en un equipo (se priorizan los dispositivos reales)
    const equipoAna = juego.equipoDe('a1').equipo;
    assert.deepEqual(equipoAna.ids.slice().sort(), ['a1', 'b2', 'c3']);

    // Elección del representante de la ronda 1
    for (const r of respuestas) await okJson('POST', '/api/preronda/representante', { responseId: r.id, representanteId: 'a1', ronda: 1 });
    const pre = await okJson('GET', '/api/preronda/estado?responseId=b2');
    assert.equal(pre.representanteId, 'a1');
    assert.equal((await okJson('GET', '/api/admin/sesion')).equipos.every(e => e.representanteId), true);

    await okJson('POST', '/api/admin/preronda/forzar-listo');
    assert.equal(juego.etapaNombre(), 'memoria');
    const estado1 = await okJson('GET', '/api/ronda-1/estado?responseId=a1&incluirEfigies=1');
    assert.equal(estado1.integrantes.length, 3);
    assert.match(estado1.integrantes[0].efigie, /^<svg/);
    assert.equal(estado1.opciones.length, 15);

    await okJson('POST', '/api/admin/ronda-1/saltar-tiempo');
    assert.equal(juego.etapaNombre(), 'adivinanza');
    assert.equal((await api('POST', '/api/ronda-1/adivinar', { responseId: 'b2', seleccionadas: ['a1', 'b2', 'c3'] })).status, 409, 'solo adivina el representante');
    const adivinanza = await okJson('POST', '/api/ronda-1/adivinar', { responseId: 'a1', seleccionadas: ['a1', 'b2', 'c3'] });
    assert.equal(adivinanza.puntos, 150);
    await okJson('POST', '/api/admin/ronda-1/saltar-tiempo');
    assert.equal(juego.etapaNombre(), 'leaderboard');
    for (const r of respuestas) await okJson('POST', '/api/ronda-1/listo-siguiente', { responseId: r.id });
    const lb1 = await okJson('GET', '/api/ronda-1/estado?responseId=a1');
    assert.equal(lb1.equipos.find(e => e.numero === lb1.equipoPropio).puntos, 150);

    // Ronda 2: representante e identificación de capas
    await okJson('POST', '/api/admin/ronda-1/forzar-siguiente');
    assert.equal(juego.etapaNombre(), 'ronda-2-representante');
    for (const r of respuestas) await okJson('POST', '/api/preronda/representante', { responseId: r.id, representanteId: 'b2', ronda: 2 });
    assert.equal(juego.etapaNombre(), 'ronda-2-identificacion');

    for (let i = 0; i < 6; i++) {
        const vista = await okJson('GET', '/api/ronda-3/identificacion?responseId=c3');
        assert.equal(vista.indiceActual, i);
        const correcta = juego.s.estados3.c3.questions[i].answer;
        if (i === 2) {
            assert.equal((await api('POST', '/api/ronda-3/navegar', { responseId: 'b2', accion: 'siguiente' })).status, 409, 'sin consenso no avanza');
            juego.s.ultimaActividad.a1 = Date.now();
            await okJson('POST', '/api/ronda-3/pista', { responseId: 'b2', indice: i });
            assert.ok((await okJson('GET', '/api/ronda-3/identificacion?responseId=a1')).pistaPopup);
        }
        for (const r of respuestas) await okJson('POST', '/api/ronda-3/responder', { responseId: r.id, indice: i, respuesta: correcta });
        const nav = await okJson('POST', '/api/ronda-3/navegar', { responseId: 'b2', accion: 'siguiente' });
        assert.equal(nav.finalizada, i === 5);
    }
    assert.equal(juego.etapaNombre(), 'ronda-2-leaderboard');
    const lb2 = await okJson('GET', '/api/ronda-3/leaderboard?responseId=a1');
    assert.equal(lb2.equipos.find(e => e.numero === lb2.equipoPropio).puntos, 550, '5 × 100 + 1 con pista × 50');

    // Ronda 3: encontrar personas
    for (const r of respuestas) await okJson('POST', '/api/ronda-3/listo-siguiente', { responseId: r.id });
    assert.equal(juego.etapaNombre(), 'ronda-3');
    for (const r of respuestas) {
        const j = await okJson('GET', `/api/ronda-4/juego?responseId=${r.id}`);
        assert.equal(j.consignas.length, 3);
        const ids = juego.s.estados4[r.id].tasks.map(t => Object.values(juego.s.lobby)
            .find(p => p.id !== r.id && String(p.datos[t.property]).trim().toUpperCase() === t.value.toUpperCase()).id);
        const res = await okJson('POST', '/api/ronda-4/responder', { responseId: r.id, participantIds: ids });
        assert.equal(res.correcto, true);
    }
    assert.equal(juego.etapaNombre(), 'ronda-3-leaderboard');
    const final = await okJson('GET', '/api/ronda-4/leaderboard?responseId=a1');
    const propio = final.equipos.find(e => e.numero === final.equipoPropio);
    assert.equal(propio.ronda3, 250 + 200 + 150);
    assert.equal(propio.ronda2, 550, 'el total final incluye la ronda 2 de identificación');
    assert.equal(propio.puntos, 150 + 550 + 600);
    assert.equal(final.rondaCompletada, true);

    // La navegación de admin llega una sola vez
    await okJson('POST', '/api/admin/sesion/retroceder');
    assert.equal(juego.etapaNombre(), 'ronda-3');
    assert.match((await okJson('GET', '/api/participante/navegacion?responseId=a1')).destino, /^\/ronda-3\?id=a1/);
    assert.equal((await api('GET', '/api/participante/navegacion?responseId=a1')).status, 204);

    // Terminar todas devuelve a los activos al inicio y vacía la sesión
    const fin = await okJson('POST', '/api/admin/sesion/terminar-todas');
    assert.ok(fin.participantes >= 1);
    assert.equal(juego.s.rondaIniciada, false);
    assert.equal((await okJson('POST', '/api/lobby/heartbeat', { sessionId: sesion(1), responseId: 'a1' })).regresarInicio, true);
});

test('ronda 2 antigua (vista de admin) genera desafíos con capa cambiada', async () => {
    const j = new Juego(estadoInicial(), { config: () => ({}), buscarRespuesta: async id => respuestas.find(r => r.id === id), todasLasRespuestas: async () => respuestas });
    for (const [i, r] of respuestas.entries())
        await j.manejar({ method: 'POST', path: '/api/lobby/heartbeat', query: new URLSearchParams(), body: { sessionId: sesion(i + 1), responseId: r.id } });
    await j.manejar({ method: 'POST', path: '/api/admin/sesion/simular', query: new URLSearchParams(), body: null });
    const r = await j.manejar({ method: 'GET', path: '/api/ronda-2/estado', query: new URLSearchParams('responseId=a1&adminPreview=1'), body: null });
    assert.equal(r.status, 200);
    const d = r.body.desafios[0];
    assert.equal(d.opciones.length, 4);
    assert.match(d.opciones[0].efigie, /data-capa=/);
    assert.notEqual(d.efigieOriginal, d.efigieCambiada);
});

test('espectador ESP se suma a un equipo con bots', async () => {
    const j = new Juego(estadoInicial(), { config: () => ({}), buscarRespuesta: async () => null, todasLasRespuestas: async () => [] });
    const r = await j.manejar({ method: 'GET', path: '/api/espectador/estado', query: new URLSearchParams(`sessionId=${'f'.repeat(32)}`), body: null });
    assert.equal(r.status, 200);
    assert.equal(r.body.responseId, `esp:${'f'.repeat(32)}:player`);
    assert.equal(j.s.equiposEspectador[r.body.responseId].numero, 6);
    assert.equal(j.s.equiposEspectador[r.body.responseId].ids.length, 3);
});

test('el estado se puede serializar y restaurar (persistencia del Durable Object)', async () => {
    const copia = JSON.parse(JSON.stringify(juego.s));
    assert.deepEqual(copia, juego.s);
});
