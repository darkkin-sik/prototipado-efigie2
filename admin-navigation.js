(() => {
    const parametros = new URLSearchParams(window.location.search);
    function leerSesion(clave) {
        try {
            return sessionStorage.getItem(clave);
        } catch (error) {
            console.warn('El almacenamiento de sesión no está disponible en este navegador.', error);
            return null;
        }
    }
    function guardarSesion(clave, valor) {
        try {
            sessionStorage.setItem(clave, valor);
        } catch (error) {
            console.warn('El almacenamiento de sesión no está disponible en este navegador.', error);
        }
    }
    const idEnRuta = window.location.pathname.match(/^\/(?:efigie|lobby)\/([^/]+)/)?.[1];
    const sesionEspectador = parametros.get('sesion') || leerSesion('efigie_espectador_sesion');
    const idEspectador = leerSesion('efigie_espectador_id');
    const responseId = parametros.get('id')
        || (idEnRuta ? decodeURIComponent(idEnRuta) : null)
        || idEspectador
        || localStorage.getItem('mi_efigie_id');
    const esEspectador = parametros.get('espectador') === '1'
        || (Boolean(sesionEspectador) && responseId === idEspectador);
    if (!responseId) return;

    if (esEspectador) {
        guardarSesion('efigie_espectador_sesion', sesionEspectador);
        guardarSesion('efigie_espectador_id', responseId);
        const urlActual = new URL(window.location.href);
        if (urlActual.searchParams.get('espectador') !== '1' || !urlActual.searchParams.has('sesion')) {
            urlActual.searchParams.set('id', responseId);
            urlActual.searchParams.set('espectador', '1');
            urlActual.searchParams.set('sesion', sesionEspectador);
            window.location.replace(urlActual);
            return;
        }

        const destinos = {
            'pre-ronda': '/pre-ronda-1',
            'memoria': '/ronda-1-preview',
            'adivinanza': '/ronda-1-adivinar',
            'leaderboard': '/ronda-1-leaderboard',
            'ronda-2': '/ronda-2',
            'ronda-3-notas': '/ronda-3',
            'ronda-3-identificacion': '/ronda-3-identificacion',
            'ronda-3-leaderboard': '/ronda-3-leaderboard',
            'ronda-4': '/ronda-4'
        };
        let sincronizando = false;
        async function sincronizarEtapa() {
            if (sincronizando) return;
            try {
                const respuesta = await fetch(`/api/espectador/estado?responseId=${encodeURIComponent(responseId)}`, { cache: 'no-store' });
                if (!respuesta.ok) throw new Error('No se pudo consultar la sesión de espectador.');
                const sesion = await respuesta.json();
                if (!sesion.rondaIniciada) {
                    sincronizando = true;
                    window.location.replace(`/espectador?sesion=${encodeURIComponent(sesionEspectador)}`);
                    return;
                }
                const destino = destinos[sesion.etapaRondaUno] || destinos['pre-ronda'];
                if (window.location.pathname === destino) return;
                sincronizando = true;
                const query = new URLSearchParams({
                    id: responseId,
                    espectador: '1',
                    sesion: sesionEspectador
                });
                window.location.replace(`${destino}?${query}`);
            } catch (error) {
                console.error(error);
            }
        }

        sincronizarEtapa();
        const intervaloEspectador = setInterval(sincronizarEtapa, 1000);
        window.addEventListener('pagehide', () => clearInterval(intervaloEspectador));
        return;
    }

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
