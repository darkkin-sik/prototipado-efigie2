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
    private sealed class ActiveTabSession
    {
        public string ResponseId { get; init; } = "";
        public DateTime LastSeenUtc { get; init; }
    }

    private sealed record LobbyParticipant(string ResponseId, string SessionId, string Nombre, string Svg, DateTime LastSeenUtc, bool EsBot, DatosRespuesta Datos);
    private sealed record RoundTeam(int GroupNumber, string Color, string TextColor, string[] ParticipantIds, int TriangulationPossibilities, int FitnessScore);

    private static readonly ConcurrentDictionary<string, ActiveTabSession> SesionesActivas = new();
    private static readonly ConcurrentDictionary<string, LobbyParticipant> ParticipantesLobby = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, DateTime> ExpulsadosLobby = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> RespuestasQueVuelvenAEncuesta = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> ParticipantesListos = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> GrupoPorParticipante = new(StringComparer.Ordinal);
    private static readonly string[] ColoresGrupo = { "#133463", "#7C171F", "#6064A3", "#84B444", "#E27646" };
    private static readonly string[] ColoresTextoGrupo = { "#ffffff", "#ffffff", "#ffffff", "#101a15", "#101a15" };
    private static List<RoundTeam> EquiposPreRonda = new();
    private static readonly object CandadoLobby = new();
    private static volatile bool RondaIniciada;
    private static volatile bool PreviewRondaIniciada;
    private static DateTime? InicioRondaUnoUtc;
    private static int SegundosExtraRondaUno;

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

        .logo-bienvenida {{
            position: absolute;
            top: 6vh;
            left: 50%;
            width: min(177px, 54vw);
            height: auto;
            opacity: 0;
            transform: translate(-50%, 10px);
            transition: opacity 0.6s ease, transform 0.6s ease;
        }}
        .logo-bienvenida.visible {{
            opacity: 1;
            transform: translate(-50%, 0);
        }}
        
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
    <img src=""/efigie-logo.svg"" alt=""Efigie"" id=""logoBienvenida"" class=""logo-bienvenida"">
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
            document.getElementById('logoBienvenida').classList.add('visible');
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
            url = $"/lobby/{datos.id}"
        });
    });

    app.MapGet("/lobby/{id}", (string id) => {
        var usuario = LeerUsuariosCsv(rutaCsv).FirstOrDefault(respuesta => respuesta.id == id);
        if (usuario == null)
            return Results.Content("<h2>No se encontró la efigie solicitada.</h2>", "text/html; charset=utf-8");

        var cfg = LeerConfig(rutaConfig);
        string svg = InyectarAnimacionRotacionSvg(GeneradorEfigie.GenerarSVG(usuario, cfg, incluirMarcoYNombre: false));
        string nombre = System.Net.WebUtility.HtmlEncode(usuario.nombre);
        string efigieId = JsonSerializer.Serialize(id);
        string html = $@"<!DOCTYPE html>
<html lang=""es"">
<head>
    <meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1.0""><title>Sala de espera - {nombre}</title>
    <style>
        * {{ box-sizing: border-box; }}
        html, body {{ width: 100%; min-height: 100%; margin: 0; background: #ffffff; color: #111111; font-family: ""Avenir Next"", Avenir, ""Segoe UI"", sans-serif; }}
        body {{ min-height: 100vh; display: flex; flex-direction: column; align-items: center; padding: 22px 18px max(24px, env(safe-area-inset-bottom)); }}
        .marca {{ width: min(177px, 54vw); height: auto; margin: 4px 0 25px; }}
        h1 {{ margin: 0 0 12px; text-align: center; font-size: clamp(1.15rem, 4vw, 1.6rem); font-weight: 650; }}
        .nombre {{ margin: 0 0 14px; text-align: center; font-size: 1rem; color: #53615b; overflow-wrap: anywhere; }}
        .efigie {{ width: min(94vmin, 720px, calc(100vw - 36px), calc(100dvh - 200px)); height: min(94vmin, 720px, calc(100vw - 36px), calc(100dvh - 200px)); flex: 0 1 auto; }}
        .efigie svg {{ width: 100%; height: 100%; display: block; }}
        .rotar-capa-1, .rotar-capa-2, .rotar-capa-3 {{ transform-origin: 0px 0px; transform-box: view-box; animation-timing-function: linear; animation-iteration-count: infinite; }}
        .rotar-capa-1 {{ animation-name: girarHorario; animation-duration: 18s; }}
        .rotar-capa-2 {{ animation-name: girarAntihorario; animation-duration: 12s; }}
        .rotar-capa-3 {{ animation-name: girarHorario; animation-duration: 8s; }}
        @keyframes girarHorario {{ to {{ transform: rotate(360deg); }} }}
        @keyframes girarAntihorario {{ to {{ transform: rotate(-360deg); }} }}
        @media (max-height: 680px) {{ body {{ padding-block: 12px max(16px, env(safe-area-inset-bottom)); }} .marca {{ margin-bottom: 12px; }} h1 {{ margin-bottom: 8px; }} .nombre {{ margin-bottom: 8px; }} .efigie {{ width: min(94vmin, 720px, calc(100vw - 36px), calc(100dvh - 150px)); height: min(94vmin, 720px, calc(100vw - 36px), calc(100dvh - 150px)); }} }}
        @media (prefers-reduced-motion: reduce) {{ .rotar-capa-1, .rotar-capa-2, .rotar-capa-3 {{ animation-duration: 0.01ms; }} }}
    </style>
</head>
<body>
    <img class=""marca"" src=""/efigie-logo.svg"" alt=""Efigie"">
    <h1 id=""estadoLobby"">Esperando participantes... 0/15</h1>
    <p class=""nombre"">{nombre}</p>
    <main class=""efigie"" aria-label=""Efigie de {nombre}"">{svg}</main>
    <script>
        const responseId = {efigieId};
        const sessionId = (() => {{
            const bytes = new Uint8Array(16);
            crypto.getRandomValues(bytes);
            return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
        }})();
        async function actualizarLobby() {{
            try {{
                const respuesta = await fetch('/api/lobby/heartbeat', {{ method: 'POST', headers: {{ 'Content-Type': 'application/json' }}, body: JSON.stringify({{ sessionId, responseId }}) }});
                if (!respuesta.ok) return;
                const estado = await respuesta.json();
                if (estado.regresarEncuesta) {{ localStorage.removeItem('mi_efigie_id'); window.location.replace('/encuesta?nueva=1'); return; }}
                if (estado.expulsado) {{ localStorage.removeItem('mi_efigie_id'); window.location.replace('/'); return; }}
                if (estado.rondaIniciada) {{
                    const destino = estado.previewIniciada ? '/ronda-1-preview' : '/pre-ronda-1';
                    window.location.replace(`${{destino}}?id=${{encodeURIComponent(responseId)}}`);
                    return;
                }}
                document.getElementById('estadoLobby').textContent = `Esperando participantes... ${{estado.cantidad}}/15`;
            }} catch (error) {{ console.error(error); }}
        }}
        function abandonarLobby() {{
            fetch('/api/lobby/desconectar', {{ method: 'POST', headers: {{ 'Content-Type': 'application/json' }}, body: JSON.stringify({{ sessionId, responseId }}), keepalive: true }}).catch(console.error);
        }}
        actualizarLobby();
        const intervaloLobby = setInterval(actualizarLobby, 3000);
        window.addEventListener('pagehide', () => {{ clearInterval(intervaloLobby); abandonarLobby(); }});
    </script>
</body>
</html>";
        return Results.Content(html, "text/html; charset=utf-8");
    });

    app.MapGet("/admin-sesion", () => Results.File(Path.Combine(dirActual, "admin-sesion.html"), "text/html; charset=utf-8"));
    app.MapGet("/pre-ronda-1", () => Results.File(Path.Combine(dirActual, "pre-ronda-1.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-1-preview", () => Results.File(Path.Combine(dirActual, "ronda-en-curso.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-en-curso", () => Results.File(Path.Combine(dirActual, "ronda-en-curso.html"), "text/html; charset=utf-8"));

    app.MapGet("/api/admin/sesion", () => {
        LimpiarParticipantesLobby();
        var participantes = ParticipantesLobby.Values
            .OrderBy(participante => participante.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(participante => new { id = participante.ResponseId, nombre = participante.Nombre, esBot = participante.EsBot });
        var equipos = EquiposPreRonda.Select(equipo => new
        {
            numero = equipo.GroupNumber,
            color = equipo.Color,
            posibilidades = equipo.TriangulationPossibilities,
            puntaje = equipo.FitnessScore,
            integrantes = equipo.ParticipantIds.Select(id =>
            {
                if (!ParticipantesLobby.TryGetValue(id, out var integrante))
                    return new { id, nombre = id, esBot = false, listo = false, efigie = "" };
                bool listo = ParticipantesListos.TryGetValue(id, out bool marcado) && marcado;
                return new { id, nombre = integrante.Nombre, esBot = integrante.EsBot, listo, efigie = integrante.Svg };
            }).ToArray()
        }).ToArray();
        return Results.Json(new
        {
            cantidad = ParticipantesLobby.Count,
            capacidad = 15,
            rondaIniciada = RondaIniciada,
            preRondaActiva = RondaIniciada && !PreviewRondaIniciada,
            previewIniciada = PreviewRondaIniciada,
            segundosRestantesRondaUno = ObtenerSegundosRestantesRondaUno(),
            participantes,
            equipos
        });
    });

    app.MapGet("/api/lobby/estado", () => Results.Json(new { rondaIniciada = RondaIniciada, previewIniciada = PreviewRondaIniciada }));

    app.MapGet("/api/preronda/estado", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        if (!RondaIniciada)
            return Results.Json(new { rondaIniciada = false, previewIniciada = false });
        if (!GrupoPorParticipante.TryGetValue(responseId, out int numeroGrupo)) return Results.NotFound();

        RoundTeam equipo = EquiposPreRonda[numeroGrupo - 1];
        bool listo = ParticipantesListos.TryGetValue(responseId, out bool marcado) && marcado;
        int cantidadListos = equipo.ParticipantIds.Count(id =>
            ParticipantesLobby.TryGetValue(id, out var participante) &&
            ParticipantesListos.TryGetValue(id, out bool estaListo) && estaListo);
        return Results.Json(new
        {
            rondaIniciada = true,
            previewIniciada = PreviewRondaIniciada,
            grupo = numeroGrupo,
            color = equipo.Color,
            colorTexto = equipo.TextColor,
            listo,
            cantidadListos,
            total = 3
        });
    });

    app.MapGet("/api/ronda-1/estado", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        if (!RondaIniciada)
            return Results.Json(new { rondaIniciada = false, previewIniciada = false });
        if (!GrupoPorParticipante.TryGetValue(responseId, out int numeroGrupo)) return Results.NotFound();

        RoundTeam equipo = EquiposPreRonda[numeroGrupo - 1];
        var integrantes = equipo.ParticipantIds.Select(id =>
        {
            if (!ParticipantesLobby.TryGetValue(id, out var integrante))
                return new { id, nombre = id, efigie = "" };
            return new { id, nombre = integrante.Nombre, efigie = integrante.Svg };
        }).ToArray();
        return Results.Json(new
        {
            rondaIniciada = true,
            previewIniciada = PreviewRondaIniciada,
            segundosRestantes = ObtenerSegundosRestantesRondaUno(),
            integrantes
        });
    });

    app.MapPost("/api/preronda/listo", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<PrerondaRequest>();
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) || !RondaIniciada)
            return Results.BadRequest();
        if (!GrupoPorParticipante.ContainsKey(solicitud.responseId)) return Results.NotFound();

        ParticipantesListos[solicitud.responseId] = true;
        EvaluarAvancePreview();
        return Results.Json(new { estado = "ok", previewIniciada = PreviewRondaIniciada });
    });

    app.MapPost("/api/admin/sesion/expulsar", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<ExpulsionLobbyRequest>();
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId)) return Results.BadRequest();
        if (RondaIniciada) return Results.Conflict();

        if (ParticipantesLobby.TryGetValue(solicitud.responseId, out var participante) && !participante.EsBot)
        {
            ParticipantesLobby.TryRemove(solicitud.responseId, out _);
            ExpulsadosLobby[solicitud.responseId] = DateTime.UtcNow.AddMinutes(30);
        }
        else if (participante?.EsBot == true)
        {
            ParticipantesLobby.TryRemove(solicitud.responseId, out _);
        }
        return Results.Json(new { estado = "ok" });
    });

    app.MapPost("/api/admin/sesion/simular", () => IniciarPreRonda(completarConBots: true, botsListos: false));

    app.MapPost("/api/admin/sesion/iniciar", () => IniciarPreRonda(completarConBots: false, botsListos: false));

    app.MapPost("/api/admin/sesion/forzar-inicio", () => IniciarPreRonda(completarConBots: true, botsListos: true));

    app.MapPost("/api/admin/preronda/forzar-listo", () => {
        lock (CandadoLobby)
        {
            if (!RondaIniciada) return Results.Conflict();
            foreach (var equipo in EquiposPreRonda)
                foreach (string id in equipo.ParticipantIds)
                    ParticipantesListos[id] = true;
            IniciarRondaUno();
            return Results.Json(new { estado = "ok", previewIniciada = true });
        }
    });

    app.MapPost("/api/admin/ronda-1/agregar-tiempo", () => {
        lock (CandadoLobby)
        {
            if (!PreviewRondaIniciada) return Results.Conflict();
            SegundosExtraRondaUno += 20;
            return Results.Json(new { estado = "ok", segundosRestantes = ObtenerSegundosRestantesRondaUno() });
        }
    });

    IResult RetrocederSesion()
    {
        lock (CandadoLobby)
        {
            if (PreviewRondaIniciada)
            {
                PreviewRondaIniciada = false;
                InicioRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                ParticipantesListos.Clear();
                return Results.Json(new { estado = "ok", etapaAnterior = "pre-ronda-1", cambio = true });
            }
            if (RondaIniciada)
            {
                RondaIniciada = false;
                PreviewRondaIniciada = false;
                InicioRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                EquiposPreRonda = new List<RoundTeam>();
                GrupoPorParticipante.Clear();
                ParticipantesListos.Clear();
                return Results.Json(new { estado = "ok", etapaAnterior = "lobby", cambio = true });
            }

            string[] idsEncuesta = ParticipantesLobby.Values
                .Where(participante => !participante.EsBot)
                .Select(participante => participante.ResponseId)
                .ToArray();
            foreach (string id in idsEncuesta)
                RespuestasQueVuelvenAEncuesta[id] = 0;
            bool habiaParticipantes = idsEncuesta.Length > 0;
            ParticipantesLobby.Clear();
            return Results.Json(new { estado = "ok", etapaAnterior = "encuesta", cambio = habiaParticipantes });
        }
    }

    app.MapPost("/api/admin/sesion/retroceder", () => RetrocederSesion());
    app.MapPost("/api/admin/sesion/volver-lobby", () => RetrocederSesion());

    app.MapPost("/api/lobby/heartbeat", (SessionHeartbeat heartbeat) => {
        if (heartbeat == null || !Guid.TryParseExact(heartbeat.sessionId, "N", out _) || string.IsNullOrWhiteSpace(heartbeat.responseId))
            return Results.BadRequest();

        DateTime ahora = DateTime.UtcNow;
        if (RespuestasQueVuelvenAEncuesta.ContainsKey(heartbeat.responseId))
            return Results.Json(new { regresarEncuesta = true });
        if (ExpulsadosLobby.TryGetValue(heartbeat.responseId, out DateTime expulsionHasta))
        {
            if (expulsionHasta > ahora) return Results.Json(new { expulsado = true, cantidad = ParticipantesLobby.Count });
            ExpulsadosLobby.TryRemove(heartbeat.responseId, out _);
        }
        if (RondaIniciada) return Results.Json(new { rondaIniciada = true, previewIniciada = PreviewRondaIniciada, cantidad = ParticipantesLobby.Count });

        var usuario = LeerUsuariosCsv(rutaCsv).FirstOrDefault(respuesta => respuesta.id == heartbeat.responseId);
        if (usuario == null) return Results.NotFound();
        LimpiarParticipantesLobby();

        lock (CandadoLobby)
        {
            if (!ParticipantesLobby.ContainsKey(usuario.id) && ParticipantesLobby.Count >= 15)
                return Results.Json(new { salaCompleta = true, cantidad = ParticipantesLobby.Count, rondaIniciada = false, expulsado = false });

            ParticipantesLobby.AddOrUpdate(
                usuario.id,
                _ => new LobbyParticipant(usuario.id, heartbeat.sessionId, usuario.nombre, GeneradorEfigie.GenerarSVG(usuario, LeerConfig(rutaConfig), incluirMarcoYNombre: false), ahora, false, usuario),
                (_, existente) => existente with { SessionId = heartbeat.sessionId, LastSeenUtc = ahora });
        }

        return Results.Json(new { cantidad = ParticipantesLobby.Count, rondaIniciada = false, expulsado = false, salaCompleta = false });
    });

    app.MapPost("/api/lobby/desconectar", (SessionHeartbeat heartbeat) => {
        if (heartbeat == null || !Guid.TryParseExact(heartbeat.sessionId, "N", out _)) return Results.BadRequest();
        if (RondaIniciada) return Results.NoContent();
        if (ParticipantesLobby.TryGetValue(heartbeat.responseId, out var participante) && participante.SessionId == heartbeat.sessionId)
            ParticipantesLobby.TryRemove(heartbeat.responseId, out _);
        return Results.NoContent();
    });

    // --- VER EFIGIE INDIVIDUAL EN PESTAÑA EN BLANCO (PERMANENTE POR ID) ---
    app.MapGet("/efigie/{id}", (string id) => {
        var usuario = LeerUsuariosCsv(rutaCsv).FirstOrDefault(u => u.id == id);
        if (usuario == null)
            return Results.Content("<h2>No se encontró la efigie solicitada.</h2>", "text/html; charset=utf-8");

        var cfg = LeerConfig(rutaConfig);
        string svg = InyectarAnimacionRotacionSvg(GeneradorEfigie.GenerarSVG(usuario, cfg));
        double radioBaseTexto = cfg.RadioBaseTexto > 0 ? cfg.RadioBaseTexto : 15.0;
        double margenMarco = radioBaseTexto * 1.3;
        double insetMarcoPercent = (75.0 - margenMarco) / 150.0 * 100.0;

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
        .efigie-principal {{ position: relative; width: min(92vmin, 720px); height: min(92vmin, 720px); flex: 0 0 auto; }}
        .efigie-principal svg {{ display: block; width: 100%; height: 100%; background: #ffffff; }}
        .animacion-efigie .rotar-capa-1, .animacion-efigie .rotar-capa-2, .animacion-efigie .rotar-capa-3, .efigie-activa .rotar-capa-1, .efigie-activa .rotar-capa-2, .efigie-activa .rotar-capa-3, .efigie-ampliada .rotar-capa-1, .efigie-ampliada .rotar-capa-2, .efigie-ampliada .rotar-capa-3 {{
            transform-origin: 0px 0px;
            transform-box: view-box;
            animation-timing-function: linear;
            animation-iteration-count: infinite;
        }}
        .animacion-efigie .rotar-capa-1, .efigie-activa .rotar-capa-1, .efigie-ampliada .rotar-capa-1 {{ animation: girarHorario 75s linear infinite; }}
        .animacion-efigie .rotar-capa-2, .efigie-activa .rotar-capa-2, .efigie-ampliada .rotar-capa-2 {{ animation: girarAntihorario 95s linear infinite; }}
        .animacion-efigie .rotar-capa-3, .efigie-activa .rotar-capa-3, .efigie-ampliada .rotar-capa-3 {{ animation: girarHorario 115s linear infinite; }}
        @keyframes girarHorario {{ to {{ transform: rotate(360deg); }} }}
        @keyframes girarAntihorario {{ to {{ transform: rotate(-360deg); }} }}
        .animacion-efigie.pausada .rotar-capa-1, .animacion-efigie.pausada .rotar-capa-2, .animacion-efigie.pausada .rotar-capa-3 {{ animation: none; transform: none; }}
        .efigies-activas {{ width: min(1100px, 100%); display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 14px; margin: 18px 0 72px; }}
        .efigie-activa {{ min-width: 0; padding: 10px; border: 1px solid #e5e5e5; border-radius: 12px; text-align: center; }}
        .boton-efigie-activa {{ display: block; width: 100%; padding: 0; border: 0; background: transparent; cursor: zoom-in; }}
        .efigie-activa svg {{ display: block; width: 100%; aspect-ratio: 1; background: #ffffff; }}
        .nombre-efigie {{ margin: 8px 0 0; overflow-wrap: anywhere; font-size: 0.9rem; }}
        .connected-indicator {{ position: fixed; z-index: 10; left: 50%; bottom: max(16px, env(safe-area-inset-bottom)); transform: translateX(-50%); padding: 10px 16px; border: 1px solid #e5e5e5; border-radius: 999px; background: #ffffff; color: #111111; box-shadow: 0 4px 18px rgba(0,0,0,0.14); font-size: 0.9rem; font-weight: 600; white-space: nowrap; }}
        .boton-animacion {{ position: absolute; z-index: 2; right: calc(var(--inset-marco) + 8px); bottom: calc(var(--inset-marco) + 8px); width: 48px; height: 48px; border: 1px solid #e5e5e5; border-radius: 50%; background: #ffffff; color: #111111; box-shadow: 0 4px 18px rgba(0,0,0,0.16); font-size: 1.1rem; cursor: pointer; }}
        .efigie-modal {{ width: min(90vw, 760px); max-width: 90vw; padding: 18px; border: 0; border-radius: 16px; background: #ffffff; box-shadow: 0 12px 40px rgba(0,0,0,0.25); }}
        .efigie-modal::backdrop {{ background: rgba(0,0,0,0.6); }}
        .modal-cerrar {{ display: block; margin: 0 0 8px auto; padding: 8px 12px; border: 0; border-radius: 8px; background: #f1f1f1; cursor: pointer; }}
        .efigie-ampliada svg {{ display: block; width: min(80vw, 700px); height: min(80vw, 700px); margin: 0 auto; background: #ffffff; }}
        @media (min-width: 700px) {{ body {{ padding-top: 28px; }} }}
    </style>
</head>
<body>
    <main id=""efigiePropia"" class=""efigie-principal animacion-efigie"" style=""--inset-marco: {insetMarcoPercent.ToString(System.Globalization.CultureInfo.InvariantCulture)}%;"">
        {svg}
        <button id=""toggleAnimacion"" class=""boton-animacion"" type=""button"" aria-label=""Pausar animación"" aria-pressed=""false"" title=""Pausar animación"">❚❚</button>
    </main>
    <section id=""efigiesActivas"" class=""efigies-activas"" aria-label=""Efigies activas""></section>
    <div id=""connectedIndicator"" class=""connected-indicator"" role=""status"" aria-live=""polite"">Connected 1</div>
    <dialog id=""modalEfigie"" class=""efigie-modal"">
        <button id=""cerrarModal"" class=""modal-cerrar"" type=""button"">Cerrar</button>
        <div id=""efigieAmpliada"" class=""efigie-ampliada""></div>
    </dialog>
    <script>
        const efigieId = {JsonSerializer.Serialize(id)};
        const efigiePropia = document.getElementById('efigiePropia');
        const toggleAnimacion = document.getElementById('toggleAnimacion');
        toggleAnimacion.addEventListener('click', () => {{
            const pausada = efigiePropia.classList.toggle('pausada');
            toggleAnimacion.textContent = pausada ? '▶' : '❚❚';
            toggleAnimacion.setAttribute('aria-label', pausada ? 'Reproducir animación' : 'Pausar animación');
            toggleAnimacion.setAttribute('title', pausada ? 'Reproducir animación' : 'Pausar animación');
            toggleAnimacion.setAttribute('aria-pressed', String(pausada));
        }});
        const modalEfigie = document.getElementById('modalEfigie');
        document.getElementById('cerrarModal').addEventListener('click', () => modalEfigie.close());
        modalEfigie.addEventListener('click', event => {{
            if (event.target === modalEfigie) modalEfigie.close();
        }});

        function crearIdSesion() {{
            const bytes = new Uint8Array(16);
            if (window.crypto && window.crypto.getRandomValues) {{
                window.crypto.getRandomValues(bytes);
            }} else {{
                for (let i = 0; i < bytes.length; i++) bytes[i] = Math.floor(Math.random() * 256);
            }}
            return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
        }}
        const sesionTab = crearIdSesion();

        async function actualizarSesion() {{
            try {{
                const respuesta = await fetch('/api/sesion/heartbeat', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{ sessionId: sesionTab, responseId: efigieId }})
                }});
                if (!respuesta.ok) throw new Error('No se pudo actualizar la sesión.');

                const resultado = await respuesta.json();
                document.getElementById('connectedIndicator').textContent = 'Connected ' + resultado.count;
                const contenedorEfigies = document.getElementById('efigiesActivas');
                contenedorEfigies.replaceChildren();
                for (const efigie of resultado.efigies) {{
                    const tarjeta = document.createElement('article');
                    tarjeta.className = 'efigie-activa';
                    const botonEfigie = document.createElement('button');
                    botonEfigie.className = 'boton-efigie-activa';
                    botonEfigie.type = 'button';
                    botonEfigie.setAttribute('aria-label', 'Ampliar efigie de ' + efigie.nombre);
                    botonEfigie.innerHTML = efigie.svg;
                    botonEfigie.addEventListener('click', () => {{
                        const ampliada = document.getElementById('efigieAmpliada');
                        ampliada.replaceChildren(botonEfigie.querySelector('svg').cloneNode(true));
                        modalEfigie.showModal();
                    }});
                    tarjeta.appendChild(botonEfigie);
                    const nombre = document.createElement('p');
                    nombre.className = 'nombre-efigie';
                    nombre.textContent = efigie.nombre;
                    tarjeta.appendChild(nombre);
                    contenedorEfigies.appendChild(tarjeta);
                }}
            }} catch (error) {{
                console.error(error);
            }}
        }}

        function abandonarSesion() {{
            fetch('/api/sesion/desconectar', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/json' }},
                body: JSON.stringify({{ sessionId: sesionTab }}),
                keepalive: true
            }}).catch(console.error);
        }}

        actualizarSesion();
        const intervaloSesion = setInterval(actualizarSesion, 10000);
        window.addEventListener('pagehide', () => {{
            clearInterval(intervaloSesion);
            abandonarSesion();
        }});
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

        DateTime ahora = DateTime.UtcNow;
        var respuestas = LeerUsuariosCsv(rutaCsv);
        var actual = respuestas.FirstOrDefault(respuesta => respuesta.id == heartbeat.responseId);
        if (actual == null)
        {
            SesionesActivas.TryRemove(heartbeat.sessionId, out _);
            return Results.NotFound();
        }

        SesionesActivas[heartbeat.sessionId] = new ActiveTabSession
        {
            ResponseId = actual.id,
            LastSeenUtc = ahora
        };

        foreach (var sesion in SesionesActivas)
        {
            if (ahora - sesion.Value.LastSeenUtc > TimeSpan.FromSeconds(90))
                SesionesActivas.TryRemove(sesion.Key, out _);
        }

        var idsEfigiesActivas = SesionesActivas.Values
            .Select(sesion => sesion.ResponseId)
            .Where(responseId => responseId != actual.id)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var cfg = LeerConfig(rutaConfig);
        var efigiesActivas = respuestas
            .Where(respuesta => idsEfigiesActivas.Contains(respuesta.id))
            .Select(respuesta => new
            {
                id = respuesta.id,
                nombre = respuesta.nombre,
                svg = InyectarAnimacionRotacionSvg(GeneradorEfigie.GenerarSVG(respuesta, cfg))
            });

        return Results.Json(new { count = SesionesActivas.Count, efigies = efigiesActivas });
    });

    app.MapPost("/api/sesion/desconectar", (SessionHeartbeat heartbeat) => {
        if (heartbeat == null || !Guid.TryParseExact(heartbeat.sessionId, "N", out _))
            return Results.BadRequest();

        SesionesActivas.TryRemove(heartbeat.sessionId, out _);
        return Results.NoContent();
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

    void LimpiarParticipantesLobby()
    {
        DateTime ahora = DateTime.UtcNow;
        if (!RondaIniciada)
        {
            foreach (var participante in ParticipantesLobby)
            {
                if (!participante.Value.EsBot && ahora - participante.Value.LastSeenUtc > TimeSpan.FromSeconds(90))
                    ParticipantesLobby.TryRemove(participante.Key, out _);
            }
        }
        foreach (var expulsion in ExpulsadosLobby)
        {
            if (expulsion.Value <= ahora)
                ExpulsadosLobby.TryRemove(expulsion.Key, out _);
        }
    }

    DatosRespuesta CrearDatosBot(string nombreBot)
    {
        string[] facultades = { "Arquitectura y Diseño", "Ciencias Económicas", "Ciencias Médicas", "Ciencias Sociales", "Derecho", "Filosofía y Letras", "Ingeniería y Ciencias Agrarias", "Música", "Psicología y Psicopedagogía", "Teología", "Staff / Docentes" };
        string[] etapas = { "1 (Ingresante / Primer año)", "2", "3", "4", "5", "6 (Sexto año o más)", "Docente", "No Docente" };
        string[] estaciones = { "Verano", "Otoño", "Invierno", "Primavera" };
        string[] grupales = { "Solo", "Grupal" };
        string[] formatos = { "Película", "Serie", "Libro", "Videojuego", "Teatro" };
        string[] roles = { "Proponer (Generar ideas iniciales)", "Organizar (Estructurar y planificar)", "Materializar (Llevar a la práctica)", "Mejorar (Revisar y pulir detalles)" };
        return new DatosRespuesta
        {
            id = $"bot:{nombreBot}", nombre = nombreBot, anio = Random.Shared.Next(1990, 2009).ToString(),
            facultad = facultades[Random.Shared.Next(facultades.Length)], etapa = etapas[Random.Shared.Next(etapas.Length)],
            estacion = estaciones[Random.Shared.Next(estaciones.Length)], grupal = grupales[Random.Shared.Next(grupales.Length)],
            formato = formatos[Random.Shared.Next(formatos.Length)], rol = roles[Random.Shared.Next(roles.Length)]
        };
    }

    IResult IniciarPreRonda(bool completarConBots, bool botsListos)
    {
        lock (CandadoLobby)
        {
            if (RondaIniciada) return Results.Conflict();
            LimpiarParticipantesLobby();

            int numeroBot = 1;
            while (completarConBots && ParticipantesLobby.Count < 15)
            {
                string nombre = $"bot{numeroBot++}";
                string idBot = $"bot:{nombre}";
                if (ParticipantesLobby.ContainsKey(idBot)) continue;
                DatosRespuesta datosBot = CrearDatosBot(nombre);
                string svgBot = GeneradorEfigie.GenerarSVG(datosBot, LeerConfig(rutaConfig), incluirMarcoYNombre: false);
                ParticipantesLobby[idBot] = new LobbyParticipant(idBot, "", nombre, svgBot, DateTime.UtcNow, true, datosBot);
            }

            if (ParticipantesLobby.Count != 15) return Results.Conflict();

            EquiposPreRonda = ConstruirEquiposOptimos(ParticipantesLobby.Values.ToList());
            GrupoPorParticipante.Clear();
            ParticipantesListos.Clear();
            foreach (RoundTeam equipo in EquiposPreRonda)
            {
                foreach (string id in equipo.ParticipantIds)
                {
                    GrupoPorParticipante[id] = equipo.GroupNumber;
                    if (botsListos && ParticipantesLobby.TryGetValue(id, out var participante) && participante.EsBot)
                        ParticipantesListos[id] = true;
                }
            }

            PreviewRondaIniciada = false;
            InicioRondaUnoUtc = null;
            SegundosExtraRondaUno = 0;
            RespuestasQueVuelvenAEncuesta.Clear();
            RondaIniciada = true;
        }
        EvaluarAvancePreview();
        return Results.Json(new { estado = "ok", rondaIniciada = true, equipos = 5, participantes = 15 });
    }

    List<RoundTeam> ConstruirEquiposOptimos(List<LobbyParticipant> participantes)
    {
        const int reinicios = 8;
        const int iteraciones = 5000;
        List<LobbyParticipant> mejorOrden = null;
        int mejorPuntaje = int.MinValue;

        int EvaluarOrden(IReadOnlyList<LobbyParticipant> orden)
        {
            int total = 0;
            for (int grupo = 0; grupo < 5; grupo++)
                total += EvaluarTrio(orden.Skip(grupo * 3).Take(3).ToArray()).Puntaje;
            return total;
        }

        for (int reinicio = 0; reinicio < reinicios; reinicio++)
        {
            var actual = participantes.ToList();
            for (int i = actual.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (actual[i], actual[j]) = (actual[j], actual[i]);
            }

            int puntajeActual = EvaluarOrden(actual);
            if (puntajeActual > mejorPuntaje)
            {
                mejorPuntaje = puntajeActual;
                mejorOrden = actual.ToList();
            }

            for (int iteracion = 0; iteracion < iteraciones; iteracion++)
            {
                int primero = Random.Shared.Next(15);
                int segundo = Random.Shared.Next(15);
                if (primero / 3 == segundo / 3) continue;

                (actual[primero], actual[segundo]) = (actual[segundo], actual[primero]);
                int puntajeNuevo = EvaluarOrden(actual);
                double temperatura = 1.5 * (1.0 - iteracion / (double)iteraciones) + 0.05;
                bool aceptar = puntajeNuevo >= puntajeActual ||
                    Random.Shared.NextDouble() < Math.Exp((puntajeNuevo - puntajeActual) / temperatura);
                if (aceptar)
                {
                    puntajeActual = puntajeNuevo;
                    if (puntajeNuevo > mejorPuntaje)
                    {
                        mejorPuntaje = puntajeNuevo;
                        mejorOrden = actual.ToList();
                    }
                }
                else
                {
                    (actual[primero], actual[segundo]) = (actual[segundo], actual[primero]);
                }
            }
        }

        var equipos = new List<RoundTeam>(5);
        for (int grupo = 0; grupo < 5; grupo++)
        {
            var trio = mejorOrden.Skip(grupo * 3).Take(3).ToArray();
            var (puntaje, posibilidades) = EvaluarTrio(trio);
            equipos.Add(new RoundTeam(grupo + 1, ColoresGrupo[grupo], ColoresTextoGrupo[grupo], trio.Select(persona => persona.ResponseId).ToArray(), posibilidades, puntaje));
        }
        return equipos;
    }

    (int Puntaje, int Posibilidades) EvaluarTrio(IReadOnlyList<LobbyParticipant> trio)
    {
        string[][] variables = trio.Select(persona => new[]
        {
            FirmaAnio(persona.Datos.anio), persona.Datos.facultad, persona.Datos.etapa,
            persona.Datos.estacion, persona.Datos.grupal, persona.Datos.formato, persona.Datos.rol
        }).ToArray();

        int puntaje = 0;
        int posibilidades = 0;
        for (int variable = 0; variable < 7; variable++)
        {
            int distintos = variables.Select(respuestas => respuestas[variable]).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (distintos == 2)
            {
                puntaje += 2;
                posibilidades++;
            }
            else if (distintos == 3)
            {
                puntaje++;
            }
        }
        return (puntaje, posibilidades);
    }

    string FirmaAnio(string texto)
    {
        int.TryParse(new string((texto ?? "").Where(char.IsDigit).ToArray()), out int anio);
        int dosDigitos = Math.Abs(anio % 100);
        return $"{(anio < 2000 ? "petalos" : "poligono")}-{dosDigitos:D2}";
    }

    void EvaluarAvancePreview()
    {
        lock (CandadoLobby)
        {
            if (!RondaIniciada || EquiposPreRonda.Count != 5) return;
            bool todosListos = EquiposPreRonda
                .SelectMany(equipo => equipo.ParticipantIds)
                .All(id => ParticipantesListos.TryGetValue(id, out bool listo) && listo);
            if (todosListos)
                IniciarRondaUno();
        }
    }

    void IniciarRondaUno()
    {
        if (PreviewRondaIniciada) return;
        InicioRondaUnoUtc = DateTime.UtcNow;
        SegundosExtraRondaUno = 0;
        PreviewRondaIniciada = true;
    }

    int ObtenerSegundosRestantesRondaUno()
    {
        lock (CandadoLobby)
        {
            if (!InicioRondaUnoUtc.HasValue) return 120;
            int totalSegundos = 120 + SegundosExtraRondaUno;
            int transcurridos = (int)Math.Ceiling((DateTime.UtcNow - InicioRondaUnoUtc.Value).TotalSeconds);
            return Math.Max(0, totalSegundos - transcurridos);
        }
    }

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

public class ExpulsionLobbyRequest
{
    public string responseId { get; set; } = "";
}

public class PrerondaRequest
{
    public string responseId { get; set; } = "";
}
