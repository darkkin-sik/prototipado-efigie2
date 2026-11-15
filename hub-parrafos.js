import silabaJS from './vendor/silabea/index.js';

const palabras = new Map();

function marcarPalabra(palabra) {
    if (palabras.has(palabra)) return palabras.get(palabra);

    const silabas = silabaJS.getSilabas(palabra).silabas;
    let resultado = palabra;
    if (silabas.length > 2) {
        const cortes = silabas.slice(1)
            .map(silaba => silaba.inicioPosicion)
            .filter(posicion => posicion >= 2 && posicion <= palabra.length - 2);
        for (const posicion of cortes.reverse())
            resultado = `${resultado.slice(0, posicion)}\u00ad${resultado.slice(posicion)}`;
    }
    palabras.set(palabra, resultado);
    return resultado;
}

function marcarTexto(nodo) {
    const original = nodo.data;
    const marcado = original.replace(/\u00ad/g, '')
        .replace(/[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]+/g, marcarPalabra);
    if (marcado !== original) nodo.data = marcado;
}

function marcarParrafo(parrafo) {
    const textos = document.createTreeWalker(parrafo, NodeFilter.SHOW_TEXT);
    while (textos.nextNode()) marcarTexto(textos.currentNode);
}

document.querySelectorAll('p').forEach(marcarParrafo);

new MutationObserver(cambios => {
    for (const cambio of cambios) {
        if (cambio.type === 'characterData') {
            if (cambio.target.parentElement?.closest('p')) marcarTexto(cambio.target);
            continue;
        }
        for (const nodo of cambio.addedNodes) {
            if (nodo.nodeType === Node.TEXT_NODE) {
                if (nodo.parentElement?.closest('p')) marcarTexto(nodo);
            } else if (nodo.nodeType === Node.ELEMENT_NODE) {
                if (nodo.matches('p')) marcarParrafo(nodo);
                else if (nodo.closest('p')) marcarParrafo(nodo);
                else nodo.querySelectorAll('p').forEach(marcarParrafo);
            }
        }
    }
}).observe(document.body, { childList: true, characterData: true, subtree: true });
