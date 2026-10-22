using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.FileProviders;

class Program
{
    private static readonly ConcurrentDictionary<string, ActiveTabSession> SesionesActivas = new();

    private sealed class ActiveTabSession
    {
        public string ResponseId { get; init; } = "";
        public DateTime LastSeenUtc { get; set; }
    }

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

app.MapGet("/", (HttpContext ctx) => {
    string deviceId = EnsureDeviceId(ctx);
    EliminarRespuestasAnteriores(rutaCsv, deviceId);

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
            background: #ffffff; display: flex; flex-direction: column;
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
    <a href=""/encuesta?nueva=1"" id=""btnGenerar"" class=""btn-generar"">Generá tu Efigie</a>
    <script>
        const respuestaAnterior = localStorage.getItem('mi_efigie_id');
        if (respuestaAnterior) {{
            fetch('/api/iniciar?id=' + encodeURIComponent(respuestaAnterior), {{
                method: 'POST',
                keepalive: true
            }});
        }}
        setTimeout(() => {{
            document.getElementById('btnGenerar').classList.add('visible');
        }}, 1000);
    </script>
</body>
</html>";

    return Results.Content(html, "text/html; charset=utf-8");
});
    app.MapGet("/resultados", () => Results.File(Path.Combine(dirActual, "resultados.html"), "text/html"));
    app.MapGet("/ajustes", () => Results.File(Path.Combine(dirActual, "ajustes.html"), "text/html"));
    app.MapGet("/encuesta", () => Results.File(Path.Combine(dirActual, "index.html"), "text/html"));

    app.MapPost("/api/iniciar", (HttpContext ctx) => {
        string deviceId = EnsureDeviceId(ctx);
        string previousId = ctx.Request.Query["id"].FirstOrDefault();
        EliminarRespuestasAnteriores(rutaCsv, deviceId, previousId);
        return Results.NoContent();
    });

    // --- GUARDAR RESPUESTA Y DEVOLVER ID ÚNICO + URL DE PERMANENCIA ---
    app.MapPost("/guardar", async (HttpContext ctx) => {
        var datos = await ctx.Request.ReadFromJsonAsync<DatosRespuesta>();
        if (datos == null) return Results.BadRequest();

        if (string.IsNullOrWhiteSpace(datos.id))
            datos.id = Guid.NewGuid().ToString("N");

        datos.deviceId = EnsureDeviceId(ctx);
        var respuestas = LeerUsuariosCsv(rutaCsv);
        respuestas.RemoveAll(respuesta => respuesta.deviceId == datos.deviceId || respuesta.id == datos.id);
        respuestas.Add(datos);
        EscribirUsuariosCsv(rutaCsv, respuestas);

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
        * {{ box-sizing: border-box; }}
        html, body {{ margin: 0; min-height: 100%; background: #ffffff; }}
        body {{ padding: 20px 16px 40px; display: flex; flex-direction: column; align-items: center; color: #111; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; }}
        .efigie-principal {{ width: min(92vmin, 720px); height: min(92vmin, 720px); flex: 0 0 auto; }}
        .efigie-principal svg {{ display: block; width: 100%; height: 100%; background: #ffffff; }}
        .sesion {{ width: min(1100px, 100%); margin-top: 18px; }}
        .sesion h1 {{ margin: 0 0 14px; font-size: 1.25rem; }}
        .galeria-sesion {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 14px; }}
        .tarjeta-efigie {{ min-width: 0; padding: 10px; border: 1px solid #e5e5e5; border-radius: 12px; text-align: center; }}
        .tarjeta-efigie svg {{ display: block; width: 100%; aspect-ratio: 1; background: #ffffff; }}
        .nombre-efigie {{ margin: 8px 0 0; overflow-wrap: anywhere; font-size: 0.9rem; }}
        .sesion-vacia {{ margin: 0; color: #666; }}
        @media (min-width: 700px) {{ body {{ padding-top: 28px; }} }}
    </style>
</head>
<body>
    <main class=""efigie-principal"">{svg}</main>
    <section class=""sesion"" aria-live=""polite"">
        <h1>Efigies de esta sesión</h1>
        <div id=""galeriaSesion"" class=""galeria-sesion""></div>
        <p id=""sesionVacia"" class=""sesion-vacia"">Buscando otras efigies activas...</p>
    </section>
    <script>
        const efigieId = {JsonSerializer.Serialize(id)};
        const claveSesion = 'efigie_sesion_tab';
        let sesionTab = sessionStorage.getItem(claveSesion);
        if (!sesionTab) {{
            sesionTab = crypto.randomUUID().replace(/-/g, '');
            sessionStorage.setItem(claveSesion, sesionTab);
        }}

        async function actualizarSesion() {{
            try {{
                const respuesta = await fetch('/api/sesion/heartbeat', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{ sessionId: sesionTab, responseId: efigieId }})
                }});
                if (!respuesta.ok) throw new Error('No se pudo actualizar la sesión.');

                const efigies = await respuesta.json();
                const galeria = document.getElementById('galeriaSesion');
                const vacio = document.getElementById('sesionVacia');
                galeria.replaceChildren();
                vacio.hidden = efigies.length > 0;
                vacio.textContent = 'Por ahora, solo está tu efigie en esta sesión.';

                for (const efigie of efigies) {{
                    const tarjeta = document.createElement('article');
                    tarjeta.className = 'tarjeta-efigie';
                    const imagen = document.createElement('div');
                    imagen.innerHTML = efigie.svg;
                    tarjeta.appendChild(imagen);
                    const nombre = document.createElement('p');
                    nombre.className = 'nombre-efigie';
                    nombre.textContent = efigie.nombre;
                    tarjeta.appendChild(nombre);
                    galeria.appendChild(tarjeta);
                }}
            }} catch (error) {{
                document.getElementById('sesionVacia').textContent = 'No se pudieron actualizar las efigies de la sesión.';
                console.error(error);
            }}
        }}

        actualizarSesion();
        setInterval(actualizarSesion, 10000);
    </script>
</body>
</html>";

        return Results.Content(html, "text/html; charset=utf-8");
    });

    app.MapPost("/api/sesion/heartbeat", (SessionHeartbeat heartbeat) => {
        if (heartbeat == null ||
            !Guid.TryParseExact(heartbeat.sessionId, "N", out _) ||
            string.IsNullOrWhiteSpace(heartbeat.responseId))
            return Results.BadRequest();

        var respuestas = LeerUsuariosCsv(rutaCsv);
        var actual = respuestas.FirstOrDefault(respuesta => respuesta.id == heartbeat.responseId);
        if (actual == null)
        {
            SesionesActivas.TryRemove(heartbeat.sessionId, out _);
            return Results.NotFound();
        }

        DateTime ahora = DateTime.UtcNow;
        SesionesActivas[heartbeat.sessionId] = new ActiveTabSession
        {
            ResponseId = actual.id,
            LastSeenUtc = ahora
        };

        foreach (var sesion in SesionesActivas)
        {
            if (ahora - sesion.Value.LastSeenUtc > TimeSpan.FromSeconds(35))
                SesionesActivas.TryRemove(sesion.Key, out _);
        }

        var idsActivos = SesionesActivas.Values
            .Select(sesion => sesion.ResponseId)
            .Where(responseId => responseId != heartbeat.responseId)
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);

        var cfg = LeerConfig(rutaConfig);
        var efigiesActivas = respuestas
            .Where(respuesta => idsActivos.Contains(respuesta.id))
            .Select(respuesta => new
            {
                id = respuesta.id,
                nombre = respuesta.nombre,
                svg = GeneradorEfigie.GenerarSVG(respuesta, cfg)
            });

        return Results.Json(efigiesActivas);
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
            etapa = d[4], estacion = d[5], grupal = d[6], formato = d[7], rol = d[8],
            deviceId = d.Length > 9 ? d[9] : ""
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

static string EnsureDeviceId(HttpContext ctx)
{
    const string cookieName = "efigie_device_id";
    if (ctx.Request.Cookies.TryGetValue(cookieName, out string existingId) &&
        Guid.TryParseExact(existingId, "N", out _))
        return existingId;

    string deviceId = Guid.NewGuid().ToString("N");
    ctx.Response.Cookies.Append(cookieName, deviceId, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromDays(365)
    });
    return deviceId;
}

static void EliminarRespuestasAnteriores(string rutaCsv, string deviceId, string previousId = null)
{
    if (!File.Exists(rutaCsv)) return;

    var respuestas = LeerUsuariosCsv(rutaCsv);
    int cantidadAnterior = respuestas.Count;
    respuestas.RemoveAll(respuesta =>
        respuesta.deviceId == deviceId ||
        (!string.IsNullOrWhiteSpace(previousId) && respuesta.id == previousId));

    if (respuestas.Count != cantidadAnterior)
        EscribirUsuariosCsv(rutaCsv, respuestas);
}

static void EscribirUsuariosCsv(string rutaCsv, IEnumerable<DatosRespuesta> respuestas)
{
    string Escapar(string valor) => $"\"{(valor ?? "").Replace("\"", "\"\"")}\"";
    using var sw = new StreamWriter(rutaCsv, append: false, Encoding.UTF8);
    sw.WriteLine("id,nombre,anio,facultad,etapa,estacion,grupal,formato,rol,device_id");

    foreach (var respuesta in respuestas)
    {
        sw.WriteLine(string.Join(",",
            Escapar(respuesta.id), Escapar(respuesta.nombre), Escapar(respuesta.anio),
            Escapar(respuesta.facultad), Escapar(respuesta.etapa), Escapar(respuesta.estacion),
            Escapar(respuesta.grupal), Escapar(respuesta.formato), Escapar(respuesta.rol),
            Escapar(respuesta.deviceId)
        ));
    }
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

public class SessionHeartbeat
{
    public string sessionId { get; set; } = "";
    public string responseId { get; set; } = "";
}
