// Une rhino/script-principal.cs con el GeneradorEfigie.cs original en un único script para Rhino 8.
// Las directivas `using` se mueven arriba, como exige un script C# con instrucciones de nivel superior.
import fs from 'node:fs';
import path from 'node:path';

const raiz = path.resolve(import.meta.dirname, '..');
const principal = fs.readFileSync(path.join(raiz, 'rhino', 'script-principal.cs'), 'utf8');
const generador = fs.readFileSync(path.join(raiz, '..', 'GeneradorEfigie.cs'), 'utf8').replace(/^﻿/, '');

const esUsing = l => /^using [\w.]+;\s*$/.test(l);
const usings = new Set(['using System.IO;', 'using System.Linq;', 'using System.Text;', 'using System.Collections.Generic;']);
generador.split(/\r?\n/).filter(esUsing).forEach(l => usings.add(l.trim()));

const salida = [
    '// ARCHIVO GENERADO por `npm run rhino:armar`. Editá rhino/script-principal.cs o ../GeneradorEfigie.cs.',
    ...usings, '',
    principal, '',
    '// ---- GeneradorEfigie.cs original ----',
    generador.split(/\r?\n/).filter(l => !esUsing(l)).join('\n')
].join('\n');
fs.writeFileSync(path.join(raiz, 'rhino', 'EfigieRhino.cs'), salida);
console.log('rhino/EfigieRhino.cs listo');
