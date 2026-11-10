// Script para el ScriptEditor de Rhino 8 (Mac o Windows): Tools > Script > Edit, lenguaje C#.
// Usa el GeneradorEfigie.cs ORIGINAL (se agrega abajo con `npm run rhino:armar`) como referencia exacta:
//  1. Pide un CSV de respuestas (respuestas_app.csv) o la URL de una instancia (https://.../api/datos).
//  2. Genera cada efigie con RhinoCommon y guarda el SVG en rhino/referencias/<id>.svg
//     (lo usa `npm run comparar` para mostrarlo al lado de la versión JS).
//  3. Dibuja las efigies en el documento, en una grilla, con una capa por cada data-capa.

var entrada = "";
var resultado = Rhino.Input.RhinoGet.GetString("Ruta de respuestas_app.csv o URL de /api/datos", false, ref entrada);
if (resultado != Rhino.Commands.Result.Success || string.IsNullOrWhiteSpace(entrada)) return;
entrada = entrada.Trim().Trim('"');

List<DatosRespuesta> respuestas;
ConfigEfigie config = new ConfigEfigie();
string dirSalida;

if (entrada.StartsWith("http"))
{
    using var http = new System.Net.Http.HttpClient();
    string json = http.GetStringAsync(entrada).Result;
    respuestas = System.Text.Json.JsonSerializer.Deserialize<List<DatosRespuesta>>(json) ?? new();
    string urlConfig = entrada.Replace("/api/datos", "/api/config");
    config = System.Text.Json.JsonSerializer.Deserialize<ConfigEfigie>(http.GetStringAsync(urlConfig).Result) ?? config;
    dirSalida = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "efigie-referencias");
}
else
{
    respuestas = LeerCsv(entrada);
    string rutaConfig = Path.Combine(Path.GetDirectoryName(entrada) ?? "", "config.json");
    if (File.Exists(rutaConfig))
        config = System.Text.Json.JsonSerializer.Deserialize<ConfigEfigie>(File.ReadAllText(rutaConfig)) ?? config;
    dirSalida = Path.Combine(Path.GetDirectoryName(entrada) ?? "", "cloudflare", "rhino", "referencias");
}
Directory.CreateDirectory(dirSalida);

var doc = Rhino.RhinoDoc.ActiveDoc;
var capasDoc = new Dictionary<string, int>();
int IndiceCapa(string nombre)
{
    if (capasDoc.TryGetValue(nombre, out int i)) return i;
    string completo = "Efigie::" + nombre;
    int existente = doc.Layers.FindByFullPath(completo, -1);
    if (existente < 0)
    {
        int padre = doc.Layers.FindByFullPath("Efigie", -1);
        if (padre < 0) padre = doc.Layers.Add("Efigie", System.Drawing.Color.Black);
        existente = doc.Layers.Add(new Rhino.DocObjects.Layer { Name = nombre, ParentLayerId = doc.Layers[padre].Id });
    }
    return capasDoc[nombre] = existente;
}

int columnas = 8;
for (int n = 0; n < respuestas.Count; n++)
{
    var r = respuestas[n];
    string svg = GeneradorEfigie.GenerarSVG(r, config, incluirMarcoYNombre: false);
    File.WriteAllText(Path.Combine(dirSalida, r.id + ".svg"), svg);

    // Dibujar: se leen los paths del SVG (Y invertida) y se desplazan a su celda de la grilla.
    var desplazamiento = new Rhino.Geometry.Vector3d((n % columnas) * 110, -(n / columnas) * 110, 0);
    string capaActual = "marco";
    foreach (string linea in svg.Split('\n'))
    {
        var capa = System.Text.RegularExpressions.Regex.Match(linea, "data-capa=\"([^\"]+)\"");
        if (capa.Success) capaActual = capa.Groups[1].Value;
        var pts = new List<Rhino.Geometry.Point3d>();
        var d = System.Text.RegularExpressions.Regex.Match(linea, " d=\"([^\"]+)\"");
        var l = System.Text.RegularExpressions.Regex.Match(linea, "x1=\"([^\"]+)\" y1=\"([^\"]+)\" x2=\"([^\"]+)\" y2=\"([^\"]+)\"");
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        if (d.Success)
        {
            var nums = System.Text.RegularExpressions.Regex.Matches(d.Groups[1].Value, "-?[0-9.]+");
            for (int k = 0; k + 1 < nums.Count; k += 2)
                pts.Add(new Rhino.Geometry.Point3d(double.Parse(nums[k].Value, ic), -double.Parse(nums[k + 1].Value, ic), 0));
        }
        else if (l.Success)
        {
            pts.Add(new Rhino.Geometry.Point3d(double.Parse(l.Groups[1].Value, ic), -double.Parse(l.Groups[2].Value, ic), 0));
            pts.Add(new Rhino.Geometry.Point3d(double.Parse(l.Groups[3].Value, ic), -double.Parse(l.Groups[4].Value, ic), 0));
        }
        if (pts.Count < 2) continue;
        var pl = new Rhino.Geometry.Polyline(pts);
        pl.Transform(Rhino.Geometry.Transform.Translation(desplazamiento));
        doc.Objects.AddPolyline(pl, new Rhino.DocObjects.ObjectAttributes { LayerIndex = IndiceCapa(capaActual), Name = r.nombre });
    }
}
doc.Views.Redraw();
Rhino.RhinoApp.WriteLine($"Efigie: {respuestas.Count} efigies dibujadas y exportadas a {dirSalida}");

static List<DatosRespuesta> LeerCsv(string ruta)
{
    var lista = new List<DatosRespuesta>();
    foreach (string linea in File.ReadAllLines(ruta).Skip(1))
    {
        if (string.IsNullOrWhiteSpace(linea)) continue;
        var partes = new List<string>(); bool comillas = false; var actual = new StringBuilder();
        foreach (char c in linea)
        {
            if (c == '"') comillas = !comillas;
            else if (c == ',' && !comillas) { partes.Add(actual.ToString().Trim()); actual.Clear(); }
            else actual.Append(c);
        }
        partes.Add(actual.ToString().Trim());
        if (partes.Count < 9) continue;
        lista.Add(new DatosRespuesta { id = partes[0], nombre = partes[1], anio = partes[2], facultad = partes[3],
            etapa = partes[4], estacion = partes[5], grupal = partes[6], formato = partes[7], rol = partes[8] });
    }
    return lista;
}
