(() => {
    const parametros = new URLSearchParams(window.location.search);
    const idEnRuta = window.location.pathname.match(/^\/(?:efigie|lobby)\/([^/]+)/)?.[1];
    const responseId = parametros.get('id')
        || (idEnRuta ? decodeURIComponent(idEnRuta) : null)
        || localStorage.getItem('mi_efigie_id');
    if (!responseId) return;

    let navegando = false;
    async function consultarNavegacion() {
        if (navegando) return;
        try {
            const respuesta = await fetch(`/api/participante/navegacion?responseId=${encodeURIComponent(responseId)}`, { cache: 'no-store' });
            if (respuesta.status === 204) return;
            if (!respuesta.ok) throw new Error('No se pudo consultar la navegación enviada por administración.');
            const comando = await respuesta.json();
            if (typeof comando.destino !== 'string' || !comando.destino.startsWith('/')) return;
            navegando = true;
            window.location.replace(comando.destino);
        } catch (error) {
            console.error(error);
        }
    }

    consultarNavegacion();
    const intervalo = setInterval(consultarNavegacion, 1000);
    window.addEventListener('pagehide', () => clearInterval(intervalo));
})();
