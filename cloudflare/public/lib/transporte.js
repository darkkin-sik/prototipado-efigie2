// Transporte por WebSocket para las llamadas /api/* del juego.
// Las páginas siguen usando fetch() igual que con el servidor C#; este script intercepta esas llamadas
// y las manda por un único WebSocket al Durable Object de la sesión. Así el polling cada 1 s no cuenta
// como una solicitud HTTP por vez (el plan gratuito de Workers permite 100.000 por día).
// Si el WebSocket no está disponible, cae a fetch normal y todo sigue funcionando.
(() => {
    const fetchNativo = window.fetch.bind(window);
    const SOLO_HTTP = /^\/api\/(datos|config|iniciar|efigie\/)/;
    const ESPERA_CONEXION_MS = 2000;
    const ESPERA_RESPUESTA_MS = 10000;
    let ws = null;
    let abierto = false;
    let cerrandoPagina = false;
    let siguienteId = 0;
    let reintento = 500;
    const pendientes = new Map();
    const esperandoConexion = [];

    function conectar() {
        if (cerrandoPagina || !('WebSocket' in window)) return;
        const protocolo = location.protocol === 'https:' ? 'wss:' : 'ws:';
        try { ws = new WebSocket(`${protocolo}//${location.host}/api/ws`); } catch { return; }
        ws.onopen = () => {
            abierto = true;
            reintento = 500;
            esperandoConexion.splice(0).forEach(avisar => avisar(true));
        };
        ws.onmessage = evento => {
            let mensaje;
            try { mensaje = JSON.parse(evento.data); } catch { return; }
            const pendiente = pendientes.get(mensaje.id);
            if (!pendiente) return;
            pendientes.delete(mensaje.id);
            clearTimeout(pendiente.temporizador);
            pendiente.resolver(mensaje);
        };
        ws.onclose = () => {
            abierto = false;
            ws = null;
            for (const pendiente of pendientes.values()) { clearTimeout(pendiente.temporizador); pendiente.resolver(null); }
            pendientes.clear();
            esperandoConexion.splice(0).forEach(avisar => avisar(false));
            if (!cerrandoPagina) setTimeout(conectar, reintento = Math.min(reintento * 2, 8000));
        };
        ws.onerror = () => { /* onclose se encarga del reintento */ };
    }

    function esperarConexion() {
        if (abierto) return Promise.resolve(true);
        return new Promise(resolver => {
            esperandoConexion.push(resolver);
            setTimeout(() => resolver(false), ESPERA_CONEXION_MS);
        });
    }

    window.fetch = async (entrada, opciones = {}) => {
        if (typeof entrada !== 'string' && !(entrada instanceof URL)) return fetchNativo(entrada, opciones);
        const url = new URL(entrada, location.href);
        const metodo = (opciones.method || 'GET').toUpperCase();
        if (url.origin !== location.origin || !url.pathname.startsWith('/api/') || SOLO_HTTP.test(url.pathname) ||
            opciones.keepalive || (opciones.body != null && typeof opciones.body !== 'string'))
            return fetchNativo(entrada, opciones);
        if (!(await esperarConexion()) || !ws) return fetchNativo(entrada, opciones);

        const id = ++siguienteId;
        const mensaje = await new Promise(resolver => {
            const temporizador = setTimeout(() => { pendientes.delete(id); resolver(null); }, ESPERA_RESPUESTA_MS);
            pendientes.set(id, { resolver, temporizador });
            ws.send(JSON.stringify({ id, method: metodo, url: url.pathname + url.search, body: opciones.body ?? null }));
        });
        // Si se cortó la conexión: los GET se reintentan por HTTP; un POST podría haberse aplicado, así que se informa el error.
        if (!mensaje) return metodo === 'GET' ? fetchNativo(entrada, opciones) : new Response(null, { status: 503 });
        const sinCuerpo = mensaje.status === 204 || mensaje.body == null;
        return new Response(sinCuerpo ? null : mensaje.body, {
            status: mensaje.status,
            headers: sinCuerpo ? {} : { 'Content-Type': 'application/json; charset=utf-8' }
        });
    };

    window.addEventListener('pagehide', () => { cerrandoPagina = true; if (ws) ws.close(); });
    conectar();
})();
