using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string rhinoSystemDir = @"C:\Program Files\Rhino 8\System";
        string rhinoNetCoreDir = Path.Combine(rhinoSystemDir, "netcore");

        Environment.SetEnvironmentVariable("PATH", $"{rhinoSystemDir};{rhinoNetCoreDir};{Environment.GetEnvironmentVariable("PATH")}");

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string p1 = Path.Combine(rhinoNetCoreDir, name.Name + ".dll");
            if (File.Exists(p1)) return context.LoadFromAssemblyPath(p1);

            string p2 = Path.Combine(rhinoSystemDir, name.Name + ".dll");
            if (File.Exists(p2)) return context.LoadFromAssemblyPath(p2);

            return null;
        };

        IniciarServidor(args);
    }

[MethodImpl(MethodImplOptions.NoInlining)]
    static void IniciarServidor(string[] args)
    {
        // 1. Guardamos la carpeta real de tu proyecto ANTES de que Rhino la cambie
        string dirActual = Directory.GetCurrentDirectory();

        Console.WriteLine("Iniciando motor de Rhino 8 en segundo plano...");
        using var rhinoCore = new Rhino.Runtime.InProcess.RhinoCore(new string[] { "/netcore", "/nosplash" }, Rhino.Runtime.InProcess.WindowStyle.Hidden);
        Console.WriteLine("Motor de Rhino 8 listo.");

        // 2. Restauramos tu carpeta como directorio actual
        Directory.SetCurrentDirectory(dirActual);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            Args = args,
            ContentRootPath = dirActual
        });
        builder.WebHost.UseUrls("http://0.0.0.0:5500");
        var app = builder.Build();

        string rutaCsv = Path.Combine(dirActual, "respuestas_app.csv");
        string rutaConfig = Path.Combine(dirActual, "config.json");

    app.UseStaticFiles(new StaticFileOptions {
        FileProvider = new PhysicalFileProvider(dirActual),
        RequestPath = ""
    });

app.MapGet("/", () => {
    var random = new Random();
    string[] nombres = { "Estudiante Anónimo", "Diseñador Creativo", "Explorador UCA", "Innovador Visual", "Arquitecto Digital" };
    string[] facultades = { "Arquitectura y Diseño", "Ciencias Económicas", "Ingeniería y Ciencias Agrarias", "Psicología y Psicopedagogía" };
    string[] etapas = { "1 (Ingresante / Primer año)", "3", "5", "Docente" };
    string[] estaciones = { "Primavera", "Verano", "Otoño", "Invierno" };
    string[] grupales = { "Solo", "Grupal" };
    string[] formatos = { "Película", "Serie", "Libro", "Videojuego", "Teatro" };
    string[] roles = { "Proponer (Generar ideas iniciales)", "Organizar (Estructurar y planificar)", "Materializar (Llevar a la práctica)", "Mejorar (Revisar y pulir detalles)" };

    var datosAleatorios = new DatosRespuesta {
        id = "random_" + random.Next(1000, 9999),
        nombre = nombres[random.Next(nombres.Length)],
        anio = random.Next(1995, 2007).ToString(),
        facultad = facultades[random.Next(facultades.Length)],
        etapa = etapas[random.Next(etapas.Length)],
        estacion = estaciones[random.Next(estaciones.Length)],
        grupal = grupales[random.Next(grupales.Length)],
        formato = formatos[random.Next(formatos.Length)],
        rol = roles[random.Next(roles.Length)]
    };

    var cfg = LeerConfig(rutaConfig);
    string svgOriginal = GeneradorEfigie.GenerarSVG(datosAleatorios, cfg, incluirMarcoYNombre: false);
    string svgAnimado = InyectarAnimacionRotacionSvg(svgOriginal);

    string html = $@"<!DOCTYPE html>
<html lang=""es"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Efigie - Bienvenida</title>
    <style>
        html, body {{
            margin: 0; padding: 0; width: 100%; height: 100%;
            background: #f7f9f8; display: flex; flex-direction: column;
            justify-content: center; align-items: center; overflow: hidden;
            font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;
        }}
        .contenedor-efigie {{
            width: 85vmin; height: 85vmin;
            display: flex; justify-content: center; align-items: center;
        }}
        svg {{
            width: 100%; height: 100%;
            overflow: visible;
        }}
        @keyframes girarHorario {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(360deg); }}
        }}
        @keyframes girarAntihorario {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(-360deg); }}
        }}
        .rotar-capa-1, .rotar-capa-2, .rotar-capa-3 {{
            transform-origin: 0px 0px;
            transform-box: view-box;
            animation-timing-function: linear;
            animation-iteration-count: infinite;
        }}
        .rotar-capa-1 {{ animation-name: girarHorario; animation-duration: 18s; }}
        .rotar-capa-2 {{ animation-name: girarAntihorario; animation-duration: 12s; }}
        .rotar-capa-3 {{ animation-name: girarHorario; animation-duration: 8s; }}
        
        .btn-generar {{
            position: absolute;
            bottom: 8vh;
            padding: 14px 28px;
            background-color: #111;
            color: #fff;
            text-decoration: none;
            font-size: 1rem;
            font-weight: 600;
            border-radius: 12px;
            box-shadow: 0 4px 15px rgba(0,0,0,0.15);
            opacity: 0;
            transform: translateY(20px);
            transition: opacity 0.6s ease, transform 0.6s ease, background-color 0.2s;
            cursor: pointer;
        }}
        .btn-generar.visible {{
            opacity: 1;
            transform: translateY(0);
        }}
        .btn-generar:hover {{
            background-color: #333;
        }}
    </style>
</head>
<body>
    <div class=""contenedor-efigie"">
        {svgAnimado}
    </div>
    <a href=""/encuesta"" id=""btnGenerar"" class=""btn-generar"">Generá tu Efigie</a>
    <script>
        setTimeout(() => {{
            document.getElementById('btnGenerar').classList.add('visible');
        }}, 2000);
    </script>
</body>
</html>";

    return Results.Content(html, "text/html; charset=utf-8");
});
    app.MapGet("/resultados", () => Results.File(Path.Combine(dirActual, "resultados.html"), "text/html"));
    app.MapGet("/ajustes", () => Results.File(Path.Combine(dirActual, "ajustes.html"), "text/html"));
    app.MapGet("/encuesta", () => Results.File(Path.Combine(dirActual, "index.html"), "text/html"));

    // --- GUARDAR RESPUESTA Y DEVOLVER ID ÚNICO + URL DE PERMANENCIA ---
    app.MapPost("/guardar", async (HttpContext ctx) => {
        var datos = await ctx.Request.ReadFromJsonAsync<DatosRespuesta>();
        if (datos == null) return Results.BadRequest();

        if (string.IsNullOrWhiteSpace(datos.id))
            datos.id = Guid.NewGuid().ToString("N").Substring(0, 8);

        bool existe = File.Exists(rutaCsv);
        using (var sw = new StreamWriter(rutaCsv, append: true, Encoding.UTF8))
        {
            if (!existe)
                sw.WriteLine("id,nombre,anio,facultad,etapa,estacion,grupal,formato,rol");

            string Escapar(string s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
            sw.WriteLine(string.Join(",",
                Escapar(datos.id), Escapar(datos.nombre), Escapar(datos.anio),
                Escapar(datos.facultad), Escapar(datos.etapa), Escapar(datos.estacion),
                Escapar(datos.grupal), Escapar(datos.formato), Escapar(datos.rol)
            ));
        }

        return Results.Json(new {
            estado = "ok",
            id = datos.id,
            url = $"/efigie/{datos.id}"
        });
    });

    // --- VER EFIGIE INDIVIDUAL EN PESTAÑA EN BLANCO (PERMANENTE POR ID) ---
    app.MapGet("/efigie/{id}", (string id) => {
        var usuario = LeerUsuariosCsv(rutaCsv).FirstOrDefault(u => u.id == id);
        if (usuario == null)
            return Results.Content("<h2>No se encontró la efigie solicitada.</h2>", "text/html; charset=utf-8");

        var cfg = LeerConfig(rutaConfig);
        string svg = GeneradorEfigie.GenerarSVG(usuario, cfg);

        string html = $@"<!DOCTYPE html>
<html lang=""es"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Efigie - {System.Net.WebUtility.HtmlEncode(usuario.nombre)}</title>
    <style>
        html, body {{ margin: 0; padding: 0; width: 100%; height: 100%; background: #ffffff; display: flex; justify-content: center; align-items: center; overflow: hidden; }}
        svg {{ width: 92vmin; height: 92vmin; }}
    </style>
</head>
<body>
    {svg}
</body>
</html>";

        return Results.Content(html, "text/html; charset=utf-8");
    });

    // --- API DATOS Y CONFIGURACIÓN ---
    app.MapGet("/api/datos", () => Results.Json(LeerUsuariosCsv(rutaCsv)));

    app.MapPost("/limpiar", () => {
        if (File.Exists(rutaCsv)) File.Delete(rutaCsv);
        return Results.Json(new { estado = "ok" });
    });

    app.MapGet("/api/config", () => Results.Json(LeerConfig(rutaConfig)));

    app.MapPost("/api/config", async (HttpContext ctx) => {
        var cfg = await ctx.Request.ReadFromJsonAsync<ConfigEfigie>();
        if (cfg != null)
            File.WriteAllText(rutaConfig, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        return Results.Json(new { estado = "ok" });
    });

    app.Run();
}

static ConfigEfigie LeerConfig(string ruta)
{
    if (File.Exists(ruta))
    {
        try { return JsonSerializer.Deserialize<ConfigEfigie>(File.ReadAllText(ruta)) ?? new ConfigEfigie(); }
        catch { }
    }
    return new ConfigEfigie();
}

static List<DatosRespuesta> LeerUsuariosCsv(string ruta)
{
    var lista = new List<DatosRespuesta>();
    if (!File.Exists(ruta)) return lista;
    var lineas = File.ReadAllLines(ruta, Encoding.UTF8);
    for (int i = 1; i < lineas.Length; i++)
    {
        if (string.IsNullOrWhiteSpace(lineas[i])) continue;
        var d = AnalizarLineaCSV(lineas[i]);
        if (d.Length < 9) continue;
        lista.Add(new DatosRespuesta {
            id = d[0], nombre = d[1], anio = d[2], facultad = d[3],
            etapa = d[4], estacion = d[5], grupal = d[6], formato = d[7], rol = d[8]
        });
    }
    return lista;
}

static string[] AnalizarLineaCSV(string linea)
{
    List<string> partes = new List<string>();
    bool entreComillas = false; string actual = "";
    foreach (char c in linea) {
        if (c == '"') entreComillas = !entreComillas;
        else if (c == ',' && !entreComillas) { partes.Add(actual.Trim()); actual = ""; }
        else { actual += c; }
    }
    partes.Add(actual.Trim());
    return partes.ToArray();
}
static string InyectarAnimacionRotacionSvg(string svgOriginal)
{
    var capas = new Dictionary<string, int>
    {
        ["anio"] = 1,
        ["estaciones"] = 2,
        ["carrera"] = 3,
        ["rol"] = 1,
        ["historia"] = 2,
        ["conexiones"] = 3
    };

    return Regex.Replace(svgOriginal, @"<g data-capa=""([^""]+)"">", match =>
    {
        string nombre = match.Groups[1].Value;
        return capas.TryGetValue(nombre, out int capa)
            ? $@"<g class=""rotar-capa-{capa} capa-{nombre}"">"
            : match.Value;
    });
}
}
