using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.FileProviders;

class Program
{
    private const int PuertoServidor = 5500;

    private sealed class ActiveTabSession
    {
        public string ResponseId { get; init; } = "";
        public DateTime LastSeenUtc { get; init; }
    }

    private sealed record LobbyParticipant(string ResponseId, string SessionId, string Nombre, string Svg, DateTime LastSeenUtc, bool EsBot, DatosRespuesta Datos);
    private sealed record RoundTeam(int GroupNumber, string Color, string TextColor, string[] ParticipantIds, int TriangulationPossibilities, int FitnessScore);
    private sealed record SpectatorSession(string SessionId, string ResponseId);
    private sealed record AdminNavigationRequest(string responseId, string etapa);
    private sealed record RoundTwoChoice(string Capa, string Nombre, string Efigie);
    private sealed class RoundTwoChallenge
    {
        public string TargetName { get; init; } = "";
        public string OriginalSvg { get; init; } = "";
        public string ChangedSvg { get; init; } = "";
        public string ChangedLayer { get; init; } = "";
        public RoundTwoChoice[] Choices { get; init; } = Array.Empty<RoundTwoChoice>();
        public DateTime? StartedUtc { get; set; }
        public bool Answered { get; set; }
        public bool Correct { get; set; }
        public int Points { get; set; }
    }
    private sealed class RoundTwoParticipantState
    {
        public object SyncRoot { get; } = new();
        public RoundTwoChallenge[] Challenges { get; init; } = Array.Empty<RoundTwoChallenge>();
    }
    private sealed record RoundThreeQuestion(string Layer, string Answer, string Hint);
    private sealed class RoundThreeParticipantState
    {
        public object SyncRoot { get; } = new();
        public string[] Notes { get; set; } = Array.Empty<string>();
        public string[] Answers { get; set; } = Enumerable.Repeat("", 6).ToArray();
        public bool[] HintsUsed { get; set; } = new bool[6];
        public RoundThreeQuestion[] Questions { get; init; } = Array.Empty<RoundThreeQuestion>();
        public bool Submitted { get; set; }
        public int Points { get; set; }
    }
    private sealed class RoundThreeTeamProgress
    {
        public int CurrentQuestion { get; set; }
        public string HintRecipientId { get; set; }
        public int HintQuestionIndex { get; set; } = -1;
        public string HintText { get; set; }
        public bool HintDelivered { get; set; }
    }
    private sealed record RoundFourTrait(string Property, Func<DatosRespuesta, string> GetValue);
    private sealed class RoundFourTask
    {
        public string Property { get; init; } = "";
        public string Prompt { get; init; } = "";
        public string Value { get; init; } = "";
        public string SelectedParticipantId { get; set; } = "";
    }
    private sealed class RoundFourParticipantState
    {
        public RoundFourTask[] Tasks { get; init; } = Array.Empty<RoundFourTask>();
        public bool Submitted { get; set; }
        public int Position { get; set; }
        public int Points { get; set; }
    }
    private sealed record RoundFourAnswerRequest(string responseId, string[] participantIds);
    private sealed record RoundFourSubmitRequest(string responseId);
    private sealed record RoundThreeNotesRequest(string responseId, string[] notes);
    private sealed record RoundThreeAnswerRequest(string responseId, int indice, string respuesta);
    private sealed record RoundThreeNavigationRequest(string responseId, string accion);
    private sealed record RoundThreeHintRequest(string responseId, int indice);

    private static readonly ConcurrentDictionary<string, ActiveTabSession> SesionesActivas = new();
    private static readonly ConcurrentDictionary<string, LobbyParticipant> ParticipantesLobby = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, LobbyParticipant> ParticipantesEspectador = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, RoundTeam> EquiposEspectadorPorParticipante = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SpectatorSession> SesionesEspectador = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, DateTime> ExpulsadosLobby = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> RespuestasQueVuelvenAEncuesta = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> RespuestasQueVuelvenAInicio = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> ParticipantesListos = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> ParticipantesListosSiguienteRonda = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> ParticipantesListosRondaTres = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> ParticipantesListosRondaCuatro = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> VotosRepresentanteRondaUno = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> VotosRepresentanteRondaDos = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> NavegacionesAdmin = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, DateTime> UltimaActividadParticipante = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> PuntajesRondaUno = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, RoundTwoParticipantState> EstadosRondaDos = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> PuntajesRondaDos = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, RoundThreeParticipantState> EstadosRondaTres = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> PuntajesRondaTres = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, RoundThreeQuestion[]> PreguntasRondaTresPorEquipo = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, RoundThreeTeamProgress> ProgresoEquipoRondaTres = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, RoundFourParticipantState> EstadosRondaCuatro = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> PuntajesRondaCuatro = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> GrupoPorParticipante = new(StringComparer.Ordinal);
    private static readonly string[] ColoresGrupo = { "#133463", "#7C171F", "#6064A3", "#84B444", "#E27646" };
    private static readonly string[] ColoresTextoGrupo = { "#ffffff", "#ffffff", "#ffffff", "#101a15", "#101a15" };
    private static List<RoundTeam> EquiposPreRonda = new();
    private static readonly object CandadoLobby = new();
    private static volatile bool RondaIniciada;
    private static volatile bool PreviewRondaIniciada;
    private static int EtapaRondaUno;
    private static DateTime? InicioEtapaRondaUnoUtc;
    private static int SegundosExtraRondaUno;
    private static readonly string[] EtapasRondaUno = { "pre-ronda", "memoria", "adivinanza", "leaderboard", "ronda-2-antigua", "ronda-2-representante", "ronda-2-identificacion", "ronda-2-leaderboard", "ronda-3", "ronda-3-leaderboard" };
    private static readonly RoundThreeQuestion[] PreguntasRondaTres =
    {
        new("anio", "Año de nacimiento", "No dice cuántos años tenés, pero permite calcularlos. Son cuatro números que señalan cuándo empezó tu historia."),
        new("estaciones", "Estación favorita", "Somos cuatro y nos turnamos para transformar el año. Entre frío, flores, hojas y calor, una se lleva tu elección."),
        new("carrera", "Etapa de la carrera", "Algunos recién empiezan, otros están por terminar."),
        new("rol", "Rol en un trabajo grupal", "En un equipo hay quien propone, quien organiza, quien materializa y quien mejora."),
        new("historia", "Formato de historia", "Una misma historia puede llegar de muchas maneras: con páginas, pantallas, escenarios o decisiones que la cambian."),
        new("conexiones", "Forma de trabajar", "Una tarea por resolver, distintas formas de empezar.")
    };
    private static readonly string[] OpcionesRondaTres =
    {
        "Año de nacimiento",
        "Estación favorita",
        "Etapa de la carrera",
        "Rol en un trabajo grupal",
        "Formato de historia",
        "Forma de trabajar"
    };

    [STAThread]
    static void Main(string[] args)
    {
        if (!PuertoDisponible(PuertoServidor))
        {
            Console.Error.WriteLine($"ERROR: El puerto {PuertoServidor} ya está ocupado. Efigie no se inició.");
            Console.Error.WriteLine("Cierra normalmente la aplicación que usa ese puerto o detén la instancia anterior de Efigie.");
            Environment.ExitCode = 1;
            return;
        }

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

    private static bool PuertoDisponible(int puerto)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, puerto);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
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
        builder.WebHost.UseUrls($"http://0.0.0.0:{PuertoServidor}");
        var app = builder.Build();

        string rutaCsv = Path.Combine(dirActual, "respuestas_app.csv");
        string rutaConfig = Path.Combine(dirActual, "config.json");

    app.UseStaticFiles(new StaticFileOptions {
        FileProvider = new PhysicalFileProvider(dirActual),
        RequestPath = ""
    });

app.MapGet("/", (HttpContext ctx) => {
    string deviceId = EnsureDeviceId(ctx);
    bool conservarRespuesta = ctx.Request.Query["reinicioSesion"] == "1";
    if (!conservarRespuesta)
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
        if (respuestaAnterior && !{conservarRespuesta.ToString().ToLowerInvariant()}) {{
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
    <script src=""/admin-navigation.js"" defer></script>
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
        const adminPreview = new URLSearchParams(window.location.search).get('adminPreview') === '1';
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
                if (estado.regresarInicio) {{ localStorage.removeItem('mi_efigie_id'); window.location.replace('/?reinicioSesion=1'); return; }}
                if (estado.regresarEncuesta) {{ localStorage.removeItem('mi_efigie_id'); window.location.replace('/encuesta?nueva=1'); return; }}
                if (estado.expulsado) {{ localStorage.removeItem('mi_efigie_id'); window.location.replace('/'); return; }}
                if (estado.rondaIniciada) {{
                    if (adminPreview) {{ document.getElementById('estadoLobby').textContent = 'Vista de prueba · Lobby'; return; }}
                    const destinos = {{ 'memoria': '/ronda-1-preview', 'adivinanza': '/ronda-1-adivinar', 'leaderboard': '/ronda-1-leaderboard', 'ronda-2-representante': '/ronda-2-representante?ronda=2', 'ronda-2-identificacion': '/ronda-2', 'ronda-2-leaderboard': '/ronda-2-leaderboard', 'ronda-3': '/ronda-3', 'ronda-3-leaderboard': '/ronda-3-leaderboard' }};
                    const destino = destinos[estado.etapaRondaUno] || '/pre-ronda-1';
                    window.location.replace(`${{destino}}${{destino.includes('?') ? '&' : '?'}}id=${{encodeURIComponent(responseId)}}`);
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
    <script src=""/admin-navigation.js"" defer></script>
</body>
</html>";
        return Results.Content(html, "text/html; charset=utf-8");
    });

    app.MapGet("/admin-sesion", () => Results.File(Path.Combine(dirActual, "admin-sesion.html"), "text/html; charset=utf-8"));
    app.MapGet("/pre-ronda-1", () => Results.File(Path.Combine(dirActual, "pre-ronda-1.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-2-representante", () => Results.File(Path.Combine(dirActual, "pre-ronda-1.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-1-preview", () => Results.File(Path.Combine(dirActual, "ronda-en-curso.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-en-curso", () => Results.File(Path.Combine(dirActual, "ronda-en-curso.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-1-adivinar", () => Results.File(Path.Combine(dirActual, "ronda-1-adivinar.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-1-leaderboard", () => Results.File(Path.Combine(dirActual, "ronda-1-leaderboard.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-2", () => Results.File(Path.Combine(dirActual, "ronda-3-identificacion.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-2-equipo", () => Results.File(Path.Combine(dirActual, "ronda-3.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-3-equipo", () => Results.File(Path.Combine(dirActual, "ronda-3.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-2-leaderboard", () => Results.File(Path.Combine(dirActual, "ronda-3-leaderboard.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-3", () => Results.File(Path.Combine(dirActual, "ronda-4.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-3-identificacion", () => Results.Redirect("/ronda-2"));
    app.MapGet("/ronda-3-leaderboard", () => Results.File(Path.Combine(dirActual, "ronda-3-final-leaderboard.html"), "text/html; charset=utf-8"));
    app.MapGet("/ronda-4", () => Results.Redirect("/ronda-3"));
    app.MapGet("/espectador", () => Results.File(Path.Combine(dirActual, "espectador.html"), "text/html; charset=utf-8"));

    app.MapGet("/api/espectador/estado", (HttpContext ctx) => {
        string sessionId = ctx.Request.Query["sessionId"].FirstOrDefault() ?? "";
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        SpectatorSession espectador;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            if (!Guid.TryParseExact(sessionId, "N", out _)) return Results.BadRequest();
            espectador = ObtenerOCrearEspectador(sessionId);
        }
        else if (!string.IsNullOrWhiteSpace(responseId))
        {
            string[] partes = responseId.Split(':');
            if (partes.Length != 3 || partes[0] != "esp" ||
                !Guid.TryParseExact(partes[1], "N", out _) ||
                !ParticipantesEspectador.ContainsKey(responseId) ||
                !SesionesEspectador.TryGetValue(partes[1], out espectador))
                return Results.NotFound();
        }
        else
            return Results.BadRequest();

        return Results.Json(new
        {
            responseId = espectador.ResponseId,
            rondaIniciada = RondaIniciada,
            etapaRondaUno = RondaIniciada ? ObtenerEtapaRondaUno() : "pre-ronda",
            segundosRestantes = RondaIniciada ? ObtenerSegundosRestantesRondaUno() : 0
        });
    });

    app.MapGet("/api/ronda-2/equipo", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (!TryObtenerEquipoRonda(responseId, out _, out RoundTeam equipo)) return Results.NotFound();
        if (!adminPreview && EtapaRondaUno != 6 && ctx.Request.Query["consulta"] != "1") return Results.Conflict();

        return Results.Json(new
        {
            efigies = equipo.ParticipantIds
                .Where(id => TryObtenerParticipanteRonda(id, out _))
                .Select(id => new
                {
                    id,
                    nombre = ObtenerParticipanteRonda(id).Nombre,
                    svg = ObtenerParticipanteRonda(id).Svg,
                    propia = id == responseId
                }).ToArray()
        });
    });

    app.MapPost("/api/ronda-3/notas", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<RoundThreeNotesRequest>();
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) || solicitud.notes == null)
            return Results.BadRequest();
        if (!adminPreview && EtapaRondaUno != 5) return Results.Conflict();
        if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out _)) return Results.NotFound();
        if (solicitud.notes.Any(nota => nota == null || nota.Length > 15 || nota.Contains('\n') || nota.Contains('\r')))
            return Results.BadRequest(new { error = "Cada nota debe ocupar una sola línea de hasta 15 caracteres." });
        if (ctx.Request.Query["consulta"] == "1") return Results.Conflict();

        RoundThreeParticipantState estado = ObtenerEstadoRondaTres(solicitud.responseId, adminPreview);
        lock (estado.SyncRoot)
        {
            estado.Notes = solicitud.notes.Where(nota => nota.Length > 0).ToArray();
            return Results.Json(new { estado = "ok", notas = estado.Notes });
        }
    });

    app.MapGet("/api/ronda-3/identificacion", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (!TryObtenerEquipoRonda(responseId, out _, out _)) return Results.NotFound();
        if (!adminPreview && EtapaRondaUno != 6)
            return Results.Conflict(new { error = "La ronda ya no está activa." });

        RoundThreeParticipantState estado = ObtenerEstadoRondaTres(responseId, adminPreview);
        if (!adminPreview || ParticipantesEspectador.ContainsKey(responseId)) InicializarRespuestasBotsRondaTres();
        if (!TryObtenerEquipoRonda(responseId, out _, out RoundTeam equipo)) return Results.NotFound();
        RoundThreeTeamProgress progreso = ObtenerProgresoEquipoRondaTres(equipo, adminPreview);
        lock (CandadoLobby)
        {
            int indice = progreso.CurrentQuestion;
            bool consenso = adminPreview || ObtenerConsensoPreguntaActual(equipo, indice, adminPreview);
            bool representanteAsignado = TryObtenerRepresentante(equipo, out string representanteId, ronda: 2);
            string pistaPopup = null;
            if (progreso.HintRecipientId == responseId &&
                progreso.HintQuestionIndex == indice &&
                !progreso.HintDelivered)
            {
                pistaPopup = progreso.HintText;
                progreso.HintDelivered = true;
            }
            string nombreRepresentante = representanteAsignado &&
                TryObtenerParticipanteRonda(representanteId, out LobbyParticipant representante)
                    ? representante.Nombre
                    : null;
            string pistaEnviadaA = representanteAsignado &&
                responseId == representanteId &&
                progreso.HintQuestionIndex == indice &&
                progreso.HintRecipientId != null &&
                TryObtenerParticipanteRonda(progreso.HintRecipientId, out LobbyParticipant destinatarioPista)
                    ? destinatarioPista.Nombre
                    : null;
            lock (estado.SyncRoot)
            {
                RoundThreeQuestion pregunta = estado.Questions[indice];
                return Results.Json(new
                {
                    etapa = "identificacion",
                    efigie = ObtenerParticipanteRonda(responseId).Svg,
                    indiceActual = indice,
                    cantidadPreguntas = estado.Questions.Length,
                    esRepresentante = adminPreview || (representanteAsignado && representanteId == responseId),
                    pregunta = new
                    {
                        pregunta.Layer,
                        opciones = OpcionesRondaTres.OrderBy(_ => Random.Shared.Next()).ToArray(),
                        respuesta = estado.Answers[indice],
                        pistaUsada = estado.HintsUsed[indice],
                    },
                    pistaPopup,
                    consenso,
                    pistaEnviadaA,
                    cantidadRespuestas = ContarRespuestasPregunta(equipo, indice, adminPreview),
                    totalParticipantes = equipo.ParticipantIds.Count(id =>
                        TryObtenerParticipanteRonda(id, out LobbyParticipant miembro) && !miembro.EsBot),
                    nombreRepresentante,
                    respuestasPrevias = estado.Answers.Take(indice).ToArray(),
                    respondido = estado.Submitted,
                    puntos = estado.Points,
                    notas = estado.Notes
                });
            }
        }
    });

    app.MapPost("/api/ronda-3/responder", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<RoundThreeAnswerRequest>();
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) ||
            solicitud.indice is < 0 or >= 6 ||
            string.IsNullOrWhiteSpace(solicitud.respuesta) ||
            !OpcionesRondaTres.Contains(solicitud.respuesta, StringComparer.Ordinal))
            return Results.BadRequest();
        if (!adminPreview && EtapaRondaUno != 6)
            return Results.Conflict(new { error = "La ronda ya no está activa." });
        if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out RoundTeam equipo)) return Results.NotFound();
        RoundThreeParticipantState estado = ObtenerEstadoRondaTres(solicitud.responseId, adminPreview);
        RoundThreeTeamProgress progreso = ObtenerProgresoEquipoRondaTres(equipo, adminPreview);
        lock (CandadoLobby)
        {
            if (estado.Submitted || solicitud.indice != progreso.CurrentQuestion)
                return Results.Conflict();
            lock (estado.SyncRoot)
            {
                estado.Answers[solicitud.indice] = solicitud.respuesta;
            }
            bool consenso = adminPreview || ObtenerConsensoPreguntaActual(equipo, progreso.CurrentQuestion, adminPreview);
            return Results.Json(new
            {
                estado = "ok",
                consenso,
                cantidadRespuestas = ContarRespuestasPregunta(equipo, progreso.CurrentQuestion, adminPreview),
                totalParticipantes = equipo.ParticipantIds.Count(id =>
                    TryObtenerParticipanteRonda(id, out LobbyParticipant miembro) && !miembro.EsBot)
            });
        }
    });

    app.MapPost("/api/ronda-3/pista", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<RoundThreeHintRequest>();
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) ||
            solicitud.indice is < 0 or >= 6)
            return Results.BadRequest();
        if (!adminPreview && EtapaRondaUno != 6)
            return Results.Conflict(new { error = "La ronda ya no está activa." });
        if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out RoundTeam equipo)) return Results.NotFound();
        if (!TryObtenerRepresentante(equipo, out string representanteId, ronda: 2) ||
            (!adminPreview && representanteId != solicitud.responseId))
            return Results.Forbid();
        RoundThreeTeamProgress progreso = ObtenerProgresoEquipoRondaTres(equipo, adminPreview);
        lock (CandadoLobby)
        {
            if (solicitud.indice != progreso.CurrentQuestion ||
                (!adminPreview && ObtenerConsensoPreguntaActual(equipo, progreso.CurrentQuestion, adminPreview)))
                return Results.Conflict(new { error = "La pista no está disponible en esta pregunta." });
            DateTime actividadMinima = DateTime.UtcNow.AddSeconds(-30);
            string[] posiblesDestinatarios = adminPreview
                ? new[] { solicitud.responseId }
                : equipo.ParticipantIds
                    .Where(id => id != representanteId &&
                        TryObtenerParticipanteRonda(id, out LobbyParticipant participante) &&
                        !participante.EsBot &&
                        UltimaActividadParticipante.TryGetValue(id, out DateTime ultimaActividad) &&
                        ultimaActividad >= actividadMinima)
                    .ToArray();
            if (posiblesDestinatarios.Length == 0)
                return Results.Conflict(new { error = "No hay otro dispositivo activo en el equipo para mostrar la pista." });

            RoundThreeParticipantState[] estadosEquipo = equipo.ParticipantIds
                .Select(id => ObtenerEstadoRondaTres(id, adminPreview))
                .ToArray();
            if (estadosEquipo.Any(estado =>
            {
                lock (estado.SyncRoot)
                    return estado.Submitted || estado.HintsUsed[solicitud.indice];
            }))
                return Results.Conflict(new { error = "La pista de esta pregunta ya fue utilizada." });

            foreach (RoundThreeParticipantState estadoIntegrante in estadosEquipo)
            {
                lock (estadoIntegrante.SyncRoot)
                    estadoIntegrante.HintsUsed[solicitud.indice] = true;
            }
            progreso.HintRecipientId = posiblesDestinatarios[Random.Shared.Next(posiblesDestinatarios.Length)];
            progreso.HintQuestionIndex = solicitud.indice;
            progreso.HintText = estadosEquipo[0].Questions[solicitud.indice].Hint;
            progreso.HintDelivered = false;
            return Results.Json(new
                {
                    estado = "ok",
                    pistaUsada = true
                });
        }
    });

    app.MapPost("/api/ronda-3/navegar", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<RoundThreeNavigationRequest>();
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) ||
            solicitud.accion is not ("anterior" or "siguiente"))
            return Results.BadRequest();
        if (!adminPreview && EtapaRondaUno != 6) return Results.Conflict();
        if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out RoundTeam equipo)) return Results.NotFound();
        if (!adminPreview &&
            (!TryObtenerRepresentante(equipo, out string representanteId, ronda: 2) || representanteId != solicitud.responseId))
            return Results.Forbid();
        RoundThreeTeamProgress progreso = ObtenerProgresoEquipoRondaTres(equipo, adminPreview);
        lock (CandadoLobby)
        {
            if (solicitud.accion == "siguiente" &&
                !adminPreview &&
                !ObtenerConsensoPreguntaActual(equipo, progreso.CurrentQuestion, adminPreview))
                return Results.Conflict(new { error = "Esperá a que todos los integrantes respondan lo mismo antes de avanzar." });
            if (solicitud.accion == "siguiente" && progreso.CurrentQuestion == PreguntasRondaTres.Length - 1)
            {
                if (!adminPreview && !FinalizarEquipoRondaTres(equipo, adminPreview))
                    return Results.Conflict();
                if (!adminPreview)
                {
                    if (TodosLosEquiposFinalizados())
                    {
                        EtapaRondaUno = 7;
                        InicioEtapaRondaUnoUtc = null;
                    }
                }
                return Results.Json(new
                {
                    estado = "ok",
                    finalizada = true,
                    etapaRondaUno = ObtenerEtapaRondaUno(),
                    resultadosDisponibles = !adminPreview
                });
            }
            int siguienteIndice = solicitud.accion == "anterior"
                ? progreso.CurrentQuestion - 1
                : progreso.CurrentQuestion + 1;
            if (siguienteIndice < 0 || siguienteIndice >= PreguntasRondaTres.Length)
                return Results.Conflict();
            progreso.CurrentQuestion = siguienteIndice;
            progreso.HintRecipientId = null;
            progreso.HintQuestionIndex = -1;
            progreso.HintText = null;
            progreso.HintDelivered = true;
            return Results.Json(new { estado = "ok", finalizada = false, indiceActual = progreso.CurrentQuestion });
        }
    });

    app.MapGet("/api/ronda-3/leaderboard", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (!TryObtenerEquipoRonda(responseId, out int numeroGrupo, out _)) return Results.NotFound();
        if (!adminPreview && EtapaRondaUno is not (6 or 7 or 8)) return Results.Conflict();
        if (!adminPreview && EtapaRondaUno == 6 &&
            (!EstadosRondaTres.TryGetValue(responseId, out RoundThreeParticipantState estadoParticipante) || !estadoParticipante.Submitted))
            return Results.Conflict();

        var equipos = EquiposPreRonda.Select(equipo =>
        {
            int[] puntuaciones = equipo.ParticipantIds
                .Where(id => PuntajesRondaTres.ContainsKey(id))
                .Select(id => PuntajesRondaTres[id])
                .ToArray();
            return new
            {
                numero = equipo.GroupNumber,
                color = equipo.Color,
                colorTexto = equipo.TextColor,
                puntos = puntuaciones.Length == 0 ? 0
                    : (int)Math.Round(puntuaciones.Average(), MidpointRounding.AwayFromZero)
            };
        }).ToList();
        if (ParticipantesEspectador.ContainsKey(responseId))
        {
            RoundTeam equipoEspectador = EquiposEspectadorPorParticipante[responseId];
            int[] puntuaciones = equipoEspectador.ParticipantIds
                .Where(id => PuntajesRondaTres.ContainsKey(id))
                .Select(id => PuntajesRondaTres[id])
                .ToArray();
            equipos.Add(new
            {
                numero = equipoEspectador.GroupNumber,
                color = equipoEspectador.Color,
                colorTexto = equipoEspectador.TextColor,
                puntos = puntuaciones.Length == 0 ? 0
                    : (int)Math.Round(puntuaciones.Average(), MidpointRounding.AwayFromZero)
            });
        }
        var equiposOrdenados = equipos.OrderByDescending(equipo => equipo.puntos).ThenBy(equipo => equipo.numero).ToArray();
        int total = ParticipantesLobby.Values.Count(participante => !participante.EsBot);
        int listos = ParticipantesLobby.Values.Count(participante =>
            !participante.EsBot && ParticipantesListosRondaCuatro.ContainsKey(participante.ResponseId));
        int participantesRespondidos = ParticipantesLobby.Values.Count(participante =>
            !participante.EsBot && EstadosRondaTres.TryGetValue(
                ClaveEstadoRondaTres(participante.ResponseId, adminPreview: false), out var estado) && estado.Submitted);
        return Results.Json(new
        {
            equipoPropio = numeroGrupo,
            equipos = equiposOrdenados,
            listoContinuar = ParticipantesListosRondaCuatro.ContainsKey(responseId),
            listosContinuar = listos,
            totalParticipantes = total,
            participantesRespondidos,
            puedeContinuar = EtapaRondaUno is 7 or 8,
            rondaTresIniciada = ObtenerEtapaRondaUno() == "ronda-3"
        });
    });

    app.MapPost("/api/ronda-3/listo-siguiente", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<PrerondaRequest>();
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId)) return Results.BadRequest();
        if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out _)) return Results.NotFound();

        lock (CandadoLobby)
        {
            if (EtapaRondaUno != 7) return Results.Conflict();
            ParticipantesListosRondaCuatro[solicitud.responseId] = true;
            foreach (LobbyParticipant participante in ParticipantesLobby.Values.Where(persona => persona.EsBot))
                ParticipantesListosRondaCuatro[participante.ResponseId] = true;
            bool todosListos = ParticipantesLobby.Values
                .Where(participante => !participante.EsBot)
                .All(participante => ParticipantesListosRondaCuatro.ContainsKey(participante.ResponseId));
            if (todosListos)
            {
                EtapaRondaUno = 8;
                InicioEtapaRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                InicializarRondaCuatro();
            }
            return Results.Json(new { estado = "ok", listo = true, rondaLista = todosListos });
        }
    });

    app.MapGet("/api/ronda-4/juego", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (EtapaRondaUno is not (8 or 9)) return Results.Conflict();
        if (!ParticipantesLobby.TryGetValue(responseId, out LobbyParticipant participante) ||
            participante.EsBot)
            return Results.Json(new { participa = false, esBot = participante?.EsBot ?? false });
        if (!EstadosRondaCuatro.TryGetValue(responseId, out RoundFourParticipantState estado))
            return Results.Conflict();

        return Results.Json(new
        {
            participa = true,
            enviado = estado.Submitted,
            equipo = TryObtenerEquipoRonda(responseId, out int numeroGrupo, out _) ? numeroGrupo : 0,
            nombre = participante.Nombre,
            opciones = ParticipantesLobby.Values
                .Where(opcion => opcion.ResponseId != responseId)
                .OrderBy(opcion => opcion.Nombre, StringComparer.OrdinalIgnoreCase)
                .Select(opcion => new { id = opcion.ResponseId, nombre = opcion.Nombre, esBot = opcion.EsBot })
                .ToArray(),
            consignas = estado.Tasks.Select((tarea, indice) => new
            {
                indice,
                consigna = tarea.Prompt,
                seleccionado = tarea.SelectedParticipantId
            }).ToArray(),
            puntos = estado.Points,
            posicion = estado.Position,
            adminPreview
        });
    });

    app.MapPost("/api/ronda-4/responder", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<RoundFourAnswerRequest>();
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) ||
            solicitud.participantIds == null || solicitud.participantIds.Length != 3 ||
            solicitud.participantIds.Any(string.IsNullOrWhiteSpace))
            return Results.BadRequest();
        if (EtapaRondaUno != 8) return Results.Conflict();
        if (!ParticipantesLobby.TryGetValue(solicitud.responseId, out LobbyParticipant participante) ||
            participante.EsBot)
            return Results.NotFound();
        LobbyParticipant[] respuestas = new LobbyParticipant[solicitud.participantIds.Length];
        for (int indice = 0; indice < solicitud.participantIds.Length; indice++)
        {
            string participantId = solicitud.participantIds[indice];
            if (participantId == solicitud.responseId ||
                !ParticipantesLobby.TryGetValue(participantId, out respuestas[indice]))
                return Results.BadRequest();
        }
        if (!EstadosRondaCuatro.TryGetValue(solicitud.responseId, out RoundFourParticipantState estado))
            return Results.Conflict();

        lock (CandadoLobby)
        {
            if (estado.Submitted) return Results.Conflict();
            bool correcto = estado.Tasks.Select((tarea, indice) =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    ValorRasgoRondaCuatro(tarea.Property, respuestas[indice].Datos),
                    tarea.Value)).All(acierto => acierto);
            if (!correcto)
                return Results.Json(new { correcto = false });

            for (int indice = 0; indice < estado.Tasks.Length; indice++)
                estado.Tasks[indice].SelectedParticipantId = respuestas[indice].ResponseId;
            estado.Position = EstadosRondaCuatro.Values.Count(item => item.Submitted) + 1;
            estado.Points = CalcularPuntajeRondaCuatro(estado.Position);
            estado.Submitted = true;
            PuntajesRondaCuatro[solicitud.responseId] = estado.Points;

            bool todosTerminaron = ParticipantesLobby.Values
                .Where(item => !item.EsBot)
                .All(item => EstadosRondaCuatro.TryGetValue(item.ResponseId, out var estadoJugador) &&
                    estadoJugador.Submitted);
            if (todosTerminaron)
                EtapaRondaUno = 9;

            return Results.Json(new
            {
                correcto = true,
                posicion = estado.Position,
                puntos = estado.Points,
                todosTerminaron
            });
        }
    });

    app.MapPost("/api/ronda-4/enviar", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<RoundFourSubmitRequest>();
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId))
            return Results.BadRequest();
        if (EtapaRondaUno != 8) return Results.Conflict();
        if (!ParticipantesLobby.TryGetValue(solicitud.responseId, out LobbyParticipant participante) ||
            participante.EsBot)
            return Results.NotFound();
        if (!EstadosRondaCuatro.TryGetValue(solicitud.responseId, out RoundFourParticipantState estado))
            return Results.Conflict();

        lock (CandadoLobby)
        {
            if (estado.Submitted) return Results.Conflict();
            if (estado.Tasks.Any(tarea => string.IsNullOrEmpty(tarea.SelectedParticipantId)))
                return Results.Conflict();

            estado.Position = EstadosRondaCuatro.Values.Count(item => item.Submitted) + 1;
            estado.Points = CalcularPuntajeRondaCuatro(estado.Position);
            estado.Submitted = true;
            PuntajesRondaCuatro[solicitud.responseId] = estado.Points;

            bool todosTerminaron = ParticipantesLobby.Values
                .Where(item => !item.EsBot)
                .All(item => EstadosRondaCuatro.TryGetValue(item.ResponseId, out var estadoJugador) &&
                    estadoJugador.Submitted);
            if (todosTerminaron)
                EtapaRondaUno = 9;

            return Results.Json(new
            {
                estado = "ok",
                posicion = estado.Position,
                puntos = estado.Points,
                todosTerminaron
            });
        }
    });

    app.MapGet("/api/ronda-4/leaderboard", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (EtapaRondaUno is not (8 or 9)) return Results.Conflict();
        if (!TryObtenerEquipoRonda(responseId, out int numeroGrupo, out _))
            return Results.NotFound();
        if (!adminPreview && EtapaRondaUno == 8 &&
            ParticipantesLobby.TryGetValue(responseId, out LobbyParticipant jugador) &&
            !jugador.EsBot &&
            (!EstadosRondaCuatro.TryGetValue(responseId, out RoundFourParticipantState estadoJugador) ||
                !estadoJugador.Submitted))
            return Results.Conflict();

        var equipos = EquiposPreRonda.Select(equipo =>
        {
            int puntosRondaUno = equipo.ParticipantIds.Length == 0 ? 0
                : (int)Math.Round(equipo.ParticipantIds.Average(id => PuntajesRondaUno.GetValueOrDefault(id)), MidpointRounding.AwayFromZero);
            int[] puntosRondaDos = equipo.ParticipantIds
                .Where(id => PuntajesRondaDos.ContainsKey(id))
                .Select(id => PuntajesRondaDos[id])
                .ToArray();
            int promedioRondaDos = puntosRondaDos.Length == 0 ? 0
                : (int)Math.Round(puntosRondaDos.Average(), MidpointRounding.AwayFromZero);
            int puntosRondaCuatro = equipo.ParticipantIds
                .Where(id => ParticipantesLobby.TryGetValue(id, out LobbyParticipant miembro) && !miembro.EsBot)
                .Sum(id => PuntajesRondaCuatro.GetValueOrDefault(id));
            int terminaron = equipo.ParticipantIds.Count(id =>
                EstadosRondaCuatro.TryGetValue(id, out RoundFourParticipantState estadoEquipo) &&
                estadoEquipo.Submitted);
            return new
            {
                numero = equipo.GroupNumber,
                color = equipo.Color,
                colorTexto = equipo.TextColor,
                ronda1 = puntosRondaUno,
                ronda2 = promedioRondaDos,
                ronda3 = puntosRondaCuatro,
                puntos = puntosRondaUno + promedioRondaDos + puntosRondaCuatro,
                terminaron
            };
        }).OrderByDescending(equipo => equipo.puntos).ThenBy(equipo => equipo.numero).ToArray();
        int total = ParticipantesLobby.Values.Count(item => !item.EsBot);
        int respondidos = EstadosRondaCuatro.Values.Count(item => item.Submitted);
        if (ParticipantesEspectador.ContainsKey(responseId))
            numeroGrupo = 0;
        return Results.Json(new
        {
            equipoPropio = numeroGrupo,
            equipos,
            participantesRespondidos = respondidos,
            totalParticipantes = total,
            etapaRondaUno = ObtenerEtapaRondaUno(),
            rondaCompletada = EtapaRondaUno == 9
        });
    });

    app.MapGet("/api/ronda-2/estado", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (!adminPreview && ObtenerEtapaRondaUno() != "ronda-2") return Results.Conflict();
        if (!TryObtenerEquipoRonda(responseId, out _, out _)) return Results.NotFound();

        RoundTwoParticipantState estado = EstadosRondaDos.GetOrAdd(
            ClaveEstadoRondaDos(responseId, adminPreview),
            _ => CrearEstadoRondaDos(responseId));
        lock (estado.SyncRoot)
        {
            int completados = estado.Challenges.Count(desafio => desafio.Answered);
            return Results.Json(new
            {
                completados,
                total = estado.Challenges.Length,
                desafios = estado.Challenges.Select((desafio, indice) => new
                {
                    indice,
                    desafio.TargetName,
                    efigieOriginal = desafio.OriginalSvg,
                    efigieCambiada = desafio.ChangedSvg,
                    opciones = desafio.Choices,
                    iniciado = desafio.StartedUtc.HasValue,
                    segundosRestantes = desafio.StartedUtc.HasValue
                        ? Math.Max(0, 30 - (int)Math.Floor((DateTime.UtcNow - desafio.StartedUtc.Value).TotalSeconds))
                        : 30,
                    respondido = desafio.Answered,
                    desafio.Points
                })
            });
        }
    });

    app.MapPost("/api/ronda-2/iniciar", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<RoundTwoRequest>();
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) || solicitud.indice is < 0 or > 1)
            return Results.BadRequest();
        if (!adminPreview && ObtenerEtapaRondaUno() != "ronda-2") return Results.Conflict();
        if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out _)) return Results.NotFound();
        if (!EstadosRondaDos.TryGetValue(ClaveEstadoRondaDos(solicitud.responseId, adminPreview), out RoundTwoParticipantState estado))
            return Results.Conflict();

        lock (estado.SyncRoot)
        {
            if (estado.Challenges.Take(solicitud.indice).Any(desafio => !desafio.Answered))
                return Results.Conflict();
            RoundTwoChallenge desafio = estado.Challenges[solicitud.indice];
            if (desafio.Answered) return Results.Conflict();
            desafio.StartedUtc ??= DateTime.UtcNow;
            int restantes = Math.Max(0, 30 - (int)Math.Floor((DateTime.UtcNow - desafio.StartedUtc.Value).TotalSeconds));
            return Results.Json(new { estado = "ok", segundosRestantes = restantes });
        }
    });

    app.MapPost("/api/ronda-2/responder", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<RoundTwoRequest>();
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) ||
            solicitud.indice is < 0 or > 1)
            return Results.BadRequest();
        if (!adminPreview && ObtenerEtapaRondaUno() != "ronda-2") return Results.Conflict();
        if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out _)) return Results.NotFound();
        if (!EstadosRondaDos.TryGetValue(ClaveEstadoRondaDos(solicitud.responseId, adminPreview), out RoundTwoParticipantState estado))
            return Results.Conflict();

        lock (estado.SyncRoot)
        {
            if (estado.Challenges.Take(solicitud.indice).Any(desafio => !desafio.Answered))
                return Results.Conflict();
            RoundTwoChallenge desafio = estado.Challenges[solicitud.indice];
            if (desafio.Answered || !desafio.StartedUtc.HasValue ||
                !desafio.Choices.Any(opcion => opcion.Capa == solicitud.capa))
                return Results.Conflict();
            double transcurrido = (DateTime.UtcNow - desafio.StartedUtc.Value).TotalSeconds;
            desafio.Correct = transcurrido <= 30 && solicitud.capa == desafio.ChangedLayer;
            desafio.Points = desafio.Correct
                ? 100 + (int)Math.Round(100 * Math.Max(0, 30 - transcurrido) / 30, MidpointRounding.AwayFromZero)
                : 0;
            desafio.Answered = true;
            int completados = estado.Challenges.Count(elemento => elemento.Answered);
            if (completados == estado.Challenges.Length && !adminPreview)
                PuntajesRondaDos[solicitud.responseId] = estado.Challenges.Sum(elemento => elemento.Points);
            return Results.Json(new
            {
                estado = "ok",
                correcto = desafio.Correct,
                puntos = desafio.Points,
                puntosTotales = estado.Challenges.Where(elemento => elemento.Answered).Sum(elemento => elemento.Points),
                completados
            });
        }
    });

    app.MapGet("/api/ronda-2/leaderboard", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (!TryObtenerEquipoRonda(responseId, out _, out _)) return Results.NotFound();
        string clave = ClaveEstadoRondaDos(responseId, adminPreview);
        RoundTwoParticipantState estado = adminPreview
            ? EstadosRondaDos.GetOrAdd(clave, _ => CrearEstadoRondaDos(responseId))
            : EstadosRondaDos.GetValueOrDefault(clave);
        if (estado == null)
            return Results.Conflict();
        lock (estado.SyncRoot)
            if (!adminPreview && estado.Challenges.Any(desafio => !desafio.Answered))
                return Results.Conflict();

        TryObtenerEquipoRonda(responseId, out _, out RoundTeam equipoPropio);
        var equipos = EquiposPreRonda.Select(equipo =>
        {
            int[] puntuaciones = equipo.ParticipantIds
                .Where(id => PuntajesRondaDos.ContainsKey(id))
                .Select(id => PuntajesRondaDos[id])
                .ToArray();
            return new
            {
                numero = equipo.GroupNumber,
                color = equipo.Color,
                colorTexto = equipo.TextColor,
                cantidadPuntajes = puntuaciones.Length,
                puntos = puntuaciones.Length == 0
                    ? 0
                    : (int)Math.Round(puntuaciones.Average(), MidpointRounding.AwayFromZero)
            };
        }).ToList();
        if (ParticipantesEspectador.ContainsKey(responseId))
        {
            RoundTeam equipoEspectador = EquiposEspectadorPorParticipante[responseId];
            int[] puntuaciones = equipoEspectador.ParticipantIds
                .Where(id => PuntajesRondaDos.ContainsKey(id))
                .Select(id => PuntajesRondaDos[id])
                .ToArray();
            equipos.Add(new
            {
                numero = equipoEspectador.GroupNumber,
                color = equipoEspectador.Color,
                colorTexto = equipoEspectador.TextColor,
                cantidadPuntajes = puntuaciones.Length,
                puntos = puntuaciones.Length == 0 ? 0
                    : (int)Math.Round(puntuaciones.Average(), MidpointRounding.AwayFromZero)
            });
        }
        var equiposOrdenados = equipos.OrderByDescending(equipo => equipo.puntos).ThenBy(equipo => equipo.numero).ToArray();
        int listosRondaTres = EquiposPreRonda.Sum(equipo => equipo.ParticipantIds.Count(id =>
            ParticipantesLobby.TryGetValue(id, out var participante) &&
            (participante.EsBot || ParticipantesListosRondaTres.ContainsKey(id))));
        int totalParticipantes = EquiposPreRonda.Sum(equipo => equipo.ParticipantIds.Length);
        bool listoContinuar = ParticipantesListosRondaTres.ContainsKey(responseId);
        return Results.Json(new
        {
            equipoPropio = equipoPropio.GroupNumber,
            equipos = equiposOrdenados,
            listoContinuar,
            listosContinuar = listosRondaTres,
            totalParticipantes,
            etapaRondaUno = ObtenerEtapaRondaUno(),
            rondaTresIniciada = ObtenerEtapaRondaUno() == "ronda-3"
        });
    });

    app.MapPost("/api/admin/participante/navegar", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<AdminNavigationRequest>();
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) ||
            string.IsNullOrWhiteSpace(solicitud.etapa))
            return Results.BadRequest();
        if (!ParticipantesLobby.TryGetValue(solicitud.responseId, out var participante))
            return Results.NotFound();

        string destino = solicitud.etapa switch
        {
            "encuesta" => "/encuesta?nueva=1",
            "efigie" => $"/efigie/{Uri.EscapeDataString(solicitud.responseId)}?adminPreview=1",
            "lobby" => $"/lobby/{Uri.EscapeDataString(solicitud.responseId)}?adminPreview=1",
            "pre-ronda-1" => $"/pre-ronda-1?id={Uri.EscapeDataString(solicitud.responseId)}&adminPreview=1",
            "ronda-1-memoria" => $"/ronda-1-preview?id={Uri.EscapeDataString(solicitud.responseId)}&adminPreview=1",
            "ronda-1-adivinanza" => $"/ronda-1-adivinar?id={Uri.EscapeDataString(solicitud.responseId)}&adminPreview=1",
            "ronda-1-resultados" => $"/ronda-1-leaderboard?id={Uri.EscapeDataString(solicitud.responseId)}&adminPreview=1",
            "ronda-2" => $"/ronda-2?id={Uri.EscapeDataString(solicitud.responseId)}&adminPreview=1",
            "ronda-2-representante" => $"/ronda-2-representante?ronda=2&id={Uri.EscapeDataString(solicitud.responseId)}&adminPreview=1",
            "ronda-2-equipo" => $"/ronda-2-equipo?id={Uri.EscapeDataString(solicitud.responseId)}&adminPreview=1&consulta=1",
            "ronda-2-resultados" => $"/ronda-2-leaderboard?id={Uri.EscapeDataString(solicitud.responseId)}&adminPreview=1",
            "ronda-3" => $"/ronda-3?id={Uri.EscapeDataString(solicitud.responseId)}&adminPreview=1",
            _ => null
        };
        if (destino == null) return Results.BadRequest();
        if (participante.EsBot)
            return Results.Json(new
            {
                estado = "preview",
                destino = destino.Contains("adminPreview=1", StringComparison.Ordinal)
                    ? destino
                    : $"{destino}{(destino.Contains('?') ? "&" : "?")}adminPreview=1"
            });

        NavegacionesAdmin[solicitud.responseId] = destino;
        return Results.Json(new { estado = "enviado", nombre = participante.Nombre });
    });

    app.MapGet("/api/participante/navegacion", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(responseId)) return Results.BadRequest();
        UltimaActividadParticipante[responseId] = DateTime.UtcNow;
        if (NavegacionesAdmin.TryRemove(responseId, out string destino))
        {
            RespuestasQueVuelvenAInicio.TryRemove(responseId, out _);
            if (destino.Contains("reinicioSesion=1", StringComparison.Ordinal))
                UltimaActividadParticipante.TryRemove(responseId, out _);
            return Results.Json(new { destino });
        }
        return Results.NoContent();
    });

    app.MapGet("/api/admin/sesion", () => {
        LimpiarParticipantesLobby();
        _ = ObtenerEtapaRondaUno();
        var participantes = ParticipantesLobby.Values
            .OrderBy(participante => participante.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(participante => new { id = participante.ResponseId, nombre = participante.Nombre, esBot = participante.EsBot });
        LobbyParticipant[] participantesReales = ParticipantesLobby.Values
            .Concat(ParticipantesEspectador.Values)
            .Where(participante => !participante.EsBot)
            .DistinctBy(participante => participante.ResponseId)
            .ToArray();
        int rondaRepresentanteActual = EtapaRondaUno == 5 ? 2 : 1;
        ConcurrentDictionary<string, string> votosRepresentanteActual = ObtenerVotosRepresentante(rondaRepresentanteActual);
        var equipos = EquiposPreRonda.Select(equipo => new
        {
            numero = equipo.GroupNumber,
            color = equipo.Color,
            posibilidades = equipo.TriangulationPossibilities,
            puntaje = equipo.FitnessScore,
            representanteId = TryObtenerRepresentante(equipo, out string representanteEquipo, rondaRepresentanteActual) ? representanteEquipo : null,
            representanteNombre = TryObtenerRepresentante(equipo, out string representanteParaNombre, rondaRepresentanteActual)
                ? ObtenerParticipanteRonda(representanteParaNombre).Nombre
                : null,
            integrantes = equipo.ParticipantIds.Select(id =>
            {
                if (!ParticipantesLobby.TryGetValue(id, out var integrante))
                    return new { id, nombre = id, esBot = false, listo = false, votoRepresentanteNombre = (string)null };
                bool listo = ParticipantesListos.TryGetValue(id, out bool marcado) && marcado;
                string votoRepresentanteNombre = votosRepresentanteActual.TryGetValue(id, out string votoRepresentanteId) &&
                    TryObtenerParticipanteRonda(votoRepresentanteId, out LobbyParticipant votado)
                    ? votado.Nombre
                    : null;
                return new { id, nombre = integrante.Nombre, esBot = integrante.EsBot, listo, votoRepresentanteNombre };
            }).ToArray()
        }).ToArray();
        int listosEtapa = EtapaRondaUno switch
        {
            0 => participantesReales.Count(participante => VotosRepresentanteRondaUno.ContainsKey(participante.ResponseId)),
            5 => participantesReales.Count(participante => VotosRepresentanteRondaDos.ContainsKey(participante.ResponseId)),
            1 => participantesReales.Count(participante => ParticipantesListos.ContainsKey(participante.ResponseId)),
            3 => participantesReales.Count(participante => ParticipantesListosSiguienteRonda.ContainsKey(participante.ResponseId)),
            4 => participantesReales.Count(participante => ParticipantesListosRondaTres.ContainsKey(participante.ResponseId)),
            7 => participantesReales.Count(participante => ParticipantesListosRondaCuatro.ContainsKey(participante.ResponseId)),
            _ => 0
        };
        int respondieronEtapa = EtapaRondaUno switch
        {
            2 => participantesReales.Count(participante => PuntajesRondaUno.ContainsKey(participante.ResponseId)),
            6 => participantesReales.Count(participante =>
                EstadosRondaTres.TryGetValue(participante.ResponseId, out RoundThreeParticipantState estado) &&
                estado.Answers.Any(respuesta => !string.IsNullOrWhiteSpace(respuesta))),
            8 => participantesReales.Count(participante =>
                EstadosRondaCuatro.TryGetValue(participante.ResponseId, out RoundFourParticipantState estado) &&
                estado.Submitted),
            _ => 0
        };
        return Results.Json(new
        {
            cantidad = ParticipantesLobby.Count,
            capacidad = 15,
            rondaIniciada = RondaIniciada,
            preRondaActiva = RondaIniciada && !PreviewRondaIniciada,
            previewIniciada = PreviewRondaIniciada,
            etapaRondaUno = ObtenerEtapaRondaUno(),
            segundosRestantesRondaUno = ObtenerSegundosRestantesRondaUno(),
            totalParticipantesReales = participantesReales.Length,
            listosEtapa,
            respondieronEtapa,
            participantes,
            equipos
        });
    });

    app.MapGet("/api/lobby/estado", () => Results.Json(new { rondaIniciada = RondaIniciada, previewIniciada = PreviewRondaIniciada, etapaRondaUno = ObtenerEtapaRondaUno() }));

    app.MapGet("/api/preronda/estado", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        bool adminPreview = ctx.Request.Query["adminPreview"] == "1";
        if (!RondaIniciada)
            return Results.Json(new { rondaIniciada = false, previewIniciada = false });
        if (!TryObtenerEquipoRonda(responseId, out int numeroGrupo, out RoundTeam equipo)) return Results.NotFound();

        int ronda = ctx.Request.Query["ronda"] == "2" ? 2 : 1;
        if (!adminPreview && ((ronda == 1 && EtapaRondaUno != 0) || (ronda == 2 && EtapaRondaUno != 5)))
            return Results.Conflict();
        var votosRepresentante = ObtenerVotosRepresentante(ronda);
        bool tieneVoto = votosRepresentante.TryGetValue(responseId, out string miVoto);
        bool representanteAsignado = TryObtenerRepresentante(equipo, out string representanteId, ronda);
        var conteoVotos = equipo.ParticipantIds
            .Where(votosRepresentante.ContainsKey)
            .Select(id => votosRepresentante[id])
            .GroupBy(id => id, StringComparer.Ordinal)
            .Select(grupo => new
            {
                id = grupo.Key,
                nombre = TryObtenerParticipanteRonda(grupo.Key, out LobbyParticipant votado) ? votado.Nombre : grupo.Key,
                cantidad = grupo.Count()
            }).ToArray();
        return Results.Json(new
        {
            rondaIniciada = true,
            previewIniciada = PreviewRondaIniciada,
            etapaRondaUno = ObtenerEtapaRondaUno(),
            ronda,
            grupo = numeroGrupo,
            color = equipo.Color,
            colorTexto = equipo.TextColor,
            candidatosRepresentante = equipo.ParticipantIds
                .Where(id => TryObtenerParticipanteRonda(id, out _))
                .Select(id => new { id, nombre = ObtenerParticipanteRonda(id).Nombre }),
            tieneVoto,
            miVoto = tieneVoto ? miVoto : null,
            conteoVotos,
            representanteAsignado,
            representanteId = representanteAsignado ? representanteId : null,
            representanteNombre = representanteAsignado ? ObtenerParticipanteRonda(representanteId).Nombre : null,
            todosLosRepresentantesAsignados = ronda == 2 && TodosLosRepresentantesRondaDosAsignados(),
            total = equipo.ParticipantIds.Length
        });
    });

    app.MapPost("/api/preronda/representante", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<RepresentanteRondaUnoRequest>();
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) ||
            string.IsNullOrWhiteSpace(solicitud.representanteId))
            return Results.BadRequest();
        int ronda = solicitud.ronda;
        if (!RondaIniciada || (ronda == 1 && EtapaRondaUno != 0) || (ronda == 2 && EtapaRondaUno != 5) || ronda is < 1 or > 2)
            return Results.Conflict();
        if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out RoundTeam equipo)) return Results.NotFound();
        if (!equipo.ParticipantIds.Contains(solicitud.representanteId, StringComparer.Ordinal) ||
            !TryObtenerParticipanteRonda(solicitud.responseId, out LobbyParticipant participante) ||
            participante.EsBot)
            return Results.BadRequest();

        lock (CandadoLobby)
        {
            var votosRepresentante = ObtenerVotosRepresentante(ronda);
            votosRepresentante[solicitud.responseId] = solicitud.representanteId;
            foreach (string idBot in equipo.ParticipantIds)
                if (TryObtenerParticipanteRonda(idBot, out LobbyParticipant miembro) && miembro.EsBot)
                    votosRepresentante[idBot] = solicitud.representanteId;
            bool asignado = TryObtenerRepresentante(equipo, out string representanteId, ronda);
            bool todosAsignados = ronda == 2 && TodosLosRepresentantesRondaDosAsignados();
            if (todosAsignados)
            {
                EtapaRondaUno = 6;
                InicializarEstadosRondaTres();
                InicializarRespuestasBotsRondaTres();
            }
            return Results.Json(new
            {
                estado = "ok",
                representanteAsignado = asignado,
                representanteId = asignado ? representanteId : null,
                representanteNombre = asignado ? ObtenerParticipanteRonda(representanteId).Nombre : null,
                todosLosRepresentantesAsignados = todosAsignados,
                etapaRondaUno = ObtenerEtapaRondaUno()
            });
        }
    });

    app.MapGet("/api/ronda-1/estado", (HttpContext ctx) => {
        string responseId = ctx.Request.Query["responseId"].FirstOrDefault() ?? "";
        if (!RondaIniciada)
            return Results.Json(new { rondaIniciada = false, previewIniciada = false, etapaRondaUno = "pre-ronda" });
        if (!TryObtenerEquipoRonda(responseId, out int numeroGrupo, out RoundTeam equipo)) return Results.NotFound();

        bool incluirEfigies = ctx.Request.Query["incluirEfigies"] == "1";
        var integrantes = equipo.ParticipantIds.Select(id =>
        {
            if (!TryObtenerParticipanteRonda(id, out var integrante))
                return new { id, nombre = id, efigie = "" };
            return new { id, nombre = integrante.Nombre, efigie = incluirEfigies ? integrante.Svg : "" };
        }).ToArray();
        IEnumerable<LobbyParticipant> participantesDisponibles = ParticipantesEspectador.ContainsKey(responseId)
            ? EquiposEspectadorPorParticipante[responseId].ParticipantIds.Select(ObtenerParticipanteRonda)
            : ParticipantesLobby.Values;
        var opciones = participantesDisponibles
            .OrderBy(participante => participante.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(participante => new { id = participante.ResponseId, nombre = participante.Nombre, efigie = incluirEfigies ? participante.Svg : "" })
            .ToArray();
        string etapaRondaUno = ObtenerEtapaRondaUno();
        bool representanteAsignado = TryObtenerRepresentante(equipo, out string representanteId);
        bool esRepresentante = ctx.Request.Query["adminPreview"] == "1" ||
            (representanteAsignado && representanteId == responseId);
        var equipos = EquiposPreRonda.Select(grupo =>
        {
            double promedio = grupo.ParticipantIds.Average(id => PuntajesRondaUno.GetValueOrDefault(id, 0));
            int puntos = (int)Math.Round(promedio, MidpointRounding.AwayFromZero);
            return new { numero = grupo.GroupNumber, color = grupo.Color, colorTexto = grupo.TextColor, puntos };
        }).ToList();
        if (ParticipantesEspectador.ContainsKey(responseId))
        {
            RoundTeam equipoEspectador = EquiposEspectadorPorParticipante[responseId];
            double promedio = equipoEspectador.ParticipantIds.Average(id => PuntajesRondaUno.GetValueOrDefault(id, 0));
            equipos.Add(new
            {
                numero = equipoEspectador.GroupNumber,
                color = equipoEspectador.Color,
                colorTexto = equipoEspectador.TextColor,
                puntos = (int)Math.Round(promedio, MidpointRounding.AwayFromZero)
            });
        }
        var equiposOrdenados = equipos.OrderByDescending(grupo => grupo.puntos).ThenBy(grupo => grupo.numero).ToArray();
        bool listoSiguiente = ParticipantesListosSiguienteRonda.ContainsKey(responseId);
        bool equipoListoSiguiente = equipo.ParticipantIds.All(id =>
            TryObtenerParticipanteRonda(id, out var participante) &&
            (participante.EsBot || ParticipantesListosSiguienteRonda.ContainsKey(id)));
        int cantidadListosEquipoSiguiente = equipo.ParticipantIds.Count(id =>
            TryObtenerParticipanteRonda(id, out var participante) &&
            (participante.EsBot || ParticipantesListosSiguienteRonda.ContainsKey(id)));
        return Results.Json(new
        {
            rondaIniciada = true,
            previewIniciada = PreviewRondaIniciada,
            etapaRondaUno,
            equipoPropio = numeroGrupo,
            color = equipo.Color,
            colorTexto = equipo.TextColor,
            esRepresentante,
            representanteAsignado,
            representanteId = representanteAsignado ? representanteId : null,
            representanteNombre = representanteAsignado ? ObtenerParticipanteRonda(representanteId).Nombre : null,
            segundosRestantes = ObtenerSegundosRestantesRondaUno(),
            integrantes,
            opciones,
            yaRespondio = equipo.ParticipantIds.Any(id => PuntajesRondaUno.ContainsKey(id)),
            listoSiguiente,
            equipoListoSiguiente,
            cantidadListosSiguiente = cantidadListosEquipoSiguiente,
            totalParticipantes = equipo.ParticipantIds.Length,
            equipos = equiposOrdenados
        });
    });

    app.MapPost("/api/ronda-1/adivinar", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<AdivinanzaRondaUnoRequest>();
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) ||
            solicitud.seleccionadas == null || solicitud.seleccionadas.Length != 3 ||
            solicitud.seleccionadas.Any(string.IsNullOrWhiteSpace) ||
            solicitud.seleccionadas.Distinct(StringComparer.Ordinal).Count() != 3)
            return Results.BadRequest();

        lock (CandadoLobby)
        {
            if (EtapaRondaUno != 2) return Results.Conflict();
            if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out RoundTeam equipo)) return Results.NotFound();
            if (!TryObtenerRepresentante(equipo, out string representanteId) || representanteId != solicitud.responseId)
                return Results.Conflict();
            if (solicitud.seleccionadas.Any(id => !TryObtenerParticipanteRonda(id, out _)))
                return Results.BadRequest();
            if (equipo.ParticipantIds.Any(id => PuntajesRondaUno.ContainsKey(id))) return Results.Conflict();

            int aciertos = solicitud.seleccionadas.Count(id =>
                equipo.ParticipantIds.Contains(id, StringComparer.Ordinal));
            int puntos = CalcularPuntajeRondaUno(aciertos);
            foreach (string id in equipo.ParticipantIds)
                PuntajesRondaUno[id] = puntos;
            return Results.Json(new { estado = "ok", aciertos, puntos, etapaRondaUno = ObtenerEtapaRondaUno() });
        }
    });

    app.MapPost("/api/ronda-1/listo-siguiente", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<PrerondaRequest>();
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId))
            return Results.BadRequest();
        if (EtapaRondaUno != 3) return Results.Conflict();
        if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out RoundTeam equipoParticipante)) return Results.NotFound();

        ParticipantesListosSiguienteRonda[solicitud.responseId] = true;
        foreach (string id in equipoParticipante.ParticipantIds)
            if (TryObtenerParticipanteRonda(id, out var participante) && participante.EsBot)
                ParticipantesListosSiguienteRonda[id] = true;

        bool todosListos = EquiposPreRonda.All(equipo => equipo.ParticipantIds.All(id =>
            ParticipantesLobby.TryGetValue(id, out var participante) &&
            (participante.EsBot || ParticipantesListosSiguienteRonda.ContainsKey(id))));
        bool equipoListo = equipoParticipante.ParticipantIds.All(id =>
            TryObtenerParticipanteRonda(id, out var participante) &&
            (participante.EsBot || ParticipantesListosSiguienteRonda.ContainsKey(id)));
        return Results.Json(new
        {
            estado = "ok",
            listo = true,
            rondaLista = todosListos,
            equipoListo,
            etapaRondaUno = ObtenerEtapaRondaUno()
        });
    });

    app.MapPost("/api/ronda-2/listo-siguiente", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<PrerondaRequest>();
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId))
            return Results.BadRequest();
        if (EtapaRondaUno != 4) return Results.Conflict();
        if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out RoundTeam equipoRondaTres)) return Results.NotFound();

        ParticipantesListosRondaTres[solicitud.responseId] = true;
        foreach (string id in equipoRondaTres.ParticipantIds)
            if (TryObtenerParticipanteRonda(id, out var participante) && participante.EsBot)
                ParticipantesListosRondaTres[id] = true;

        bool todosListosRondaTres = EquiposPreRonda.All(equipo => equipo.ParticipantIds.All(id =>
            ParticipantesLobby.TryGetValue(id, out var participante) &&
            (participante.EsBot || ParticipantesListosRondaTres.ContainsKey(id))));
        bool equipoListoRondaTres = equipoRondaTres.ParticipantIds.All(id =>
            TryObtenerParticipanteRonda(id, out var participante) &&
            (participante.EsBot || ParticipantesListosRondaTres.ContainsKey(id)));
        return Results.Json(new
        {
            estado = "ok",
            listo = true,
            rondaLista = todosListosRondaTres,
            equipoListo = equipoListoRondaTres,
            etapaRondaUno = ObtenerEtapaRondaUno()
        });
    });

    app.MapPost("/api/preronda/listo", async (HttpContext ctx) => {
        var solicitud = await ctx.Request.ReadFromJsonAsync<PrerondaRequest>();
        if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.responseId) || !RondaIniciada)
            return Results.BadRequest();
        if (!TryObtenerEquipoRonda(solicitud.responseId, out _, out _)) return Results.NotFound();

        ParticipantesListos[solicitud.responseId] = true;
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

    app.MapPost("/api/admin/sesion/forzar-inicio", () => IniciarPreRonda(completarConBots: false, botsListos: true));

    app.MapPost("/api/admin/preronda/forzar-listo", () => {
        lock (CandadoLobby)
        {
            if (!RondaIniciada) return Results.Conflict();
            if (EquiposPreRonda.Any(equipo => !TryObtenerRepresentante(equipo, out _)))
                return Results.Conflict();
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
            if (EtapaRondaUno is not (1 or 2)) return Results.Conflict();
            SegundosExtraRondaUno += 20;
            return Results.Json(new { estado = "ok", segundosRestantes = ObtenerSegundosRestantesRondaUno() });
        }
    });

    app.MapPost("/api/admin/ronda-1/saltar-tiempo", () => {
        lock (CandadoLobby)
        {
            if (EtapaRondaUno == 1)
            {
                EtapaRondaUno = 2;
                InicioEtapaRondaUnoUtc = DateTime.UtcNow;
                SegundosExtraRondaUno = 0;
                return Results.Json(new { estado = "ok", etapaRondaUno = "adivinanza" });
            }
            if (EtapaRondaUno == 2)
            {
                SimularRespuestasBots();
                EtapaRondaUno = 3;
                InicioEtapaRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                return Results.Json(new { estado = "ok", etapaRondaUno = "leaderboard" });
            }
            if (EtapaRondaUno == 5)
            {
                EtapaRondaUno = 6;
                InicioEtapaRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                InicializarEstadosRondaTres();
                InicializarRespuestasBotsRondaTres();
                return Results.Json(new { estado = "ok", etapaRondaUno = "ronda-2-identificacion" });
            }
            if (EtapaRondaUno == 6)
            {
                CompletarRespuestasRondaTresPorForzarAvance();
                EtapaRondaUno = 7;
                InicioEtapaRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                return Results.Json(new { estado = "ok", etapaRondaUno = "ronda-2-leaderboard" });
            }
            return Results.Conflict();
        }
    });

    app.MapPost("/api/admin/ronda-1/forzar-leaderboard", () => {
        lock (CandadoLobby)
        {
            if (EtapaRondaUno != 2) return Results.Conflict();
            SimularRespuestasBots();
            EtapaRondaUno = 3;
            InicioEtapaRondaUnoUtc = null;
            SegundosExtraRondaUno = 0;
            return Results.Json(new { estado = "ok", etapaRondaUno = "leaderboard" });
        }
    });

    app.MapPost("/api/admin/ronda-1/forzar-siguiente", () => {
        lock (CandadoLobby)
        {
            if (EtapaRondaUno == 3)
            {
                foreach (RoundTeam equipo in EquiposPreRonda)
                    foreach (string id in equipo.ParticipantIds)
                        ParticipantesListosSiguienteRonda[id] = true;
                InicioEtapaRondaUnoUtc = DateTime.UtcNow;
                SegundosExtraRondaUno = 0;
                IniciarEleccionRepresentanteRondaDos();
                return Results.Json(new { estado = "ok", etapaRondaUno = ObtenerEtapaRondaUno() });
            }
            if (EtapaRondaUno == 5)
            {
                EtapaRondaUno = 6;
                InicioEtapaRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                InicializarEstadosRondaTres();
                InicializarRespuestasBotsRondaTres();
                return Results.Json(new { estado = "ok", etapaRondaUno = "ronda-2-identificacion" });
            }
            if (EtapaRondaUno == 6)
            {
                CompletarRespuestasRondaTresPorForzarAvance();
                EtapaRondaUno = 7;
                InicioEtapaRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                return Results.Json(new { estado = "ok", etapaRondaUno = "ronda-2-leaderboard" });
            }
            if (EtapaRondaUno == 7)
            {
                foreach (LobbyParticipant participante in ParticipantesLobby.Values.Concat(ParticipantesEspectador.Values))
                    ParticipantesListosRondaCuatro[participante.ResponseId] = true;
                EtapaRondaUno = 8;
                InicioEtapaRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                InicializarRondaCuatro();
                return Results.Json(new { estado = "ok", etapaRondaUno = "ronda-3" });
            }
            if (EtapaRondaUno == 8)
            {
                EtapaRondaUno = 9;
                InicioEtapaRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                foreach (LobbyParticipant participante in ParticipantesLobby.Values.Where(persona => !persona.EsBot))
                    NavegacionesAdmin[participante.ResponseId] =
                        $"/ronda-3-leaderboard?id={Uri.EscapeDataString(participante.ResponseId)}";
                return Results.Json(new { estado = "ok", etapaRondaUno = "ronda-3-leaderboard" });
            }
            return Results.Conflict();
        }
    });

    IResult RetrocederSesion()
    {
        lock (CandadoLobby)
        {
            if (RondaIniciada && EtapaRondaUno > 0)
            {
                string rutaAnterior = EtapaRondaUno switch
                {
                    1 => "/pre-ronda-1?id=",
                    2 => "/ronda-1-preview?id=",
                    3 => "/ronda-1-adivinar?id=",
                    4 => "/ronda-1-leaderboard?id=",
                    5 => "/ronda-1-leaderboard?id=",
                    6 => "/ronda-2-representante?ronda=2&id=",
                    7 => "/ronda-2?id=",
                    8 => "/ronda-2-leaderboard?id=",
                    9 => "/ronda-3?id=",
                    _ => null
                };
                if (rutaAnterior == null)
                    return Results.Conflict();

                int etapaAnterior = EtapaRondaUno == 5 ? 3 : EtapaRondaUno - 1;
                foreach (LobbyParticipant participante in ParticipantesLobby.Values.Where(persona => !persona.EsBot))
                    NavegacionesAdmin[participante.ResponseId] =
                        $"{rutaAnterior}{Uri.EscapeDataString(participante.ResponseId)}";

                EtapaRondaUno = etapaAnterior;
                PreviewRondaIniciada = etapaAnterior > 0;
                InicioEtapaRondaUnoUtc = etapaAnterior is 1 or 2 or 5 ? DateTime.UtcNow : null;
                SegundosExtraRondaUno = 0;
                if (etapaAnterior == 8)
                {
                    PuntajesRondaCuatro.Clear();
                    foreach (RoundFourParticipantState estado in EstadosRondaCuatro.Values)
                    {
                        estado.Submitted = false;
                        estado.Position = 0;
                        estado.Points = 0;
                    }
                }
                if (etapaAnterior is 5 or 6)
                {
                    foreach (LobbyParticipant participante in ParticipantesLobby.Values.Concat(ParticipantesEspectador.Values))
                    {
                        if (!EstadosRondaTres.TryGetValue(
                            ClaveEstadoRondaTres(participante.ResponseId, adminPreview: false),
                            out RoundThreeParticipantState estado))
                            continue;
                        lock (estado.SyncRoot)
                        {
                            estado.Submitted = false;
                            estado.Points = 0;
                            if (etapaAnterior == 5 || participante.EsBot)
                            {
                                estado.Answers = Enumerable.Repeat("", estado.Questions.Length).ToArray();
                                estado.HintsUsed = new bool[estado.Questions.Length];
                            }
                        }
                    }
                    if (etapaAnterior == 5)
                        ProgresoEquipoRondaTres.Clear();
                    PuntajesRondaTres.Clear();
                }
                return Results.Json(new
                {
                    estado = "ok",
                    etapaAnterior = EtapasRondaUno[etapaAnterior],
                    cambio = true
                });
            }

            if (PreviewRondaIniciada)
            {
                EnviarNavegacionATodos("/pre-ronda-1?id=");
                PreviewRondaIniciada = false;
                EtapaRondaUno = 0;
                InicioEtapaRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                ParticipantesListos.Clear();
                ParticipantesListosSiguienteRonda.Clear();
                ParticipantesListosRondaTres.Clear();
                ParticipantesListosRondaCuatro.Clear();
                PuntajesRondaUno.Clear();
                VotosRepresentanteRondaUno.Clear();
                PuntajesRondaDos.Clear();
                PuntajesRondaTres.Clear();
                EstadosRondaCuatro.Clear();
                PuntajesRondaCuatro.Clear();
                EstadosRondaDos.Clear();
                EstadosRondaTres.Clear();
                return Results.Json(new { estado = "ok", etapaAnterior = "pre-ronda-1", cambio = true });
            }
            if (RondaIniciada)
            {
                EnviarNavegacionATodos("/lobby/");
                RondaIniciada = false;
                PreviewRondaIniciada = false;
                EtapaRondaUno = 0;
                InicioEtapaRondaUnoUtc = null;
                SegundosExtraRondaUno = 0;
                EquiposPreRonda = new List<RoundTeam>();
                GrupoPorParticipante.Clear();
                ParticipantesListos.Clear();
                ParticipantesListosSiguienteRonda.Clear();
                ParticipantesListosRondaTres.Clear();
                ParticipantesListosRondaCuatro.Clear();
                PuntajesRondaUno.Clear();
                VotosRepresentanteRondaUno.Clear();
                PuntajesRondaDos.Clear();
                PuntajesRondaTres.Clear();
                EstadosRondaCuatro.Clear();
                PuntajesRondaCuatro.Clear();
                EstadosRondaDos.Clear();
                EstadosRondaTres.Clear();
                return Results.Json(new { estado = "ok", etapaAnterior = "lobby", cambio = true });
            }

            string[] idsEncuesta = ParticipantesLobby.Values
                .Where(participante => !participante.EsBot)
                .Select(participante => participante.ResponseId)
                .ToArray();
            foreach (string id in idsEncuesta)
            {
                RespuestasQueVuelvenAEncuesta[id] = 0;
                NavegacionesAdmin[id] = "/encuesta?nueva=1";
            }
            bool habiaParticipantes = idsEncuesta.Length > 0;
            ParticipantesLobby.Clear();
            return Results.Json(new { estado = "ok", etapaAnterior = "encuesta", cambio = habiaParticipantes });
        }
    }

    void EnviarNavegacionATodos(string rutaBase)
    {
        foreach (LobbyParticipant participante in ParticipantesLobby.Values.Where(persona => !persona.EsBot))
            NavegacionesAdmin[participante.ResponseId] =
                $"{rutaBase}{Uri.EscapeDataString(participante.ResponseId)}";
    }

    app.MapPost("/api/admin/sesion/retroceder", () => RetrocederSesion());
    app.MapPost("/api/admin/sesion/volver-lobby", () => RetrocederSesion());
    app.MapPost("/api/admin/sesion/terminar-todas", () => {
        lock (CandadoLobby)
        {
            DateTime ahora = DateTime.UtcNow;
            DateTime actividadMinima = ahora.AddSeconds(-30);
            string[] participantesActivos = UltimaActividadParticipante
                .Where(actividad => actividad.Value >= actividadMinima)
                .Select(actividad => actividad.Key)
                .Concat(ParticipantesLobby.Values
                    .Where(participante => !participante.EsBot && ahora - participante.LastSeenUtc <= TimeSpan.FromSeconds(90))
                    .Select(participante => participante.ResponseId))
                .Concat(SesionesActivas.Values
                    .Where(sesion => ahora - sesion.LastSeenUtc <= TimeSpan.FromSeconds(90))
                    .Select(sesion => sesion.ResponseId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            NavegacionesAdmin.Clear();
            foreach (string responseId in participantesActivos)
            {
                NavegacionesAdmin[responseId] = "/?reinicioSesion=1";
                RespuestasQueVuelvenAInicio[responseId] = 0;
            }

            RondaIniciada = false;
            PreviewRondaIniciada = false;
            EtapaRondaUno = 0;
            InicioEtapaRondaUnoUtc = null;
            SegundosExtraRondaUno = 0;
            EquiposPreRonda = new List<RoundTeam>();
            GrupoPorParticipante.Clear();
            ParticipantesLobby.Clear();
            ParticipantesEspectador.Clear();
            EquiposEspectadorPorParticipante.Clear();
            SesionesEspectador.Clear();
            SesionesActivas.Clear();
            ExpulsadosLobby.Clear();
            ParticipantesListos.Clear();
            ParticipantesListosSiguienteRonda.Clear();
            ParticipantesListosRondaTres.Clear();
            ParticipantesListosRondaCuatro.Clear();
            VotosRepresentanteRondaUno.Clear();
            VotosRepresentanteRondaDos.Clear();
            PuntajesRondaUno.Clear();
            PuntajesRondaDos.Clear();
            PuntajesRondaTres.Clear();
            EstadosRondaCuatro.Clear();
            PuntajesRondaCuatro.Clear();
            EstadosRondaDos.Clear();
            EstadosRondaTres.Clear();
            PreguntasRondaTresPorEquipo.Clear();
            ProgresoEquipoRondaTres.Clear();
            RespuestasQueVuelvenAEncuesta.Clear();

            return Results.Json(new { estado = "ok", participantes = participantesActivos.Length });
        }
    });

    app.MapPost("/api/lobby/heartbeat", (SessionHeartbeat heartbeat) => {
        if (heartbeat == null || !Guid.TryParseExact(heartbeat.sessionId, "N", out _) || string.IsNullOrWhiteSpace(heartbeat.responseId))
            return Results.BadRequest();

        DateTime ahora = DateTime.UtcNow;
        if (RespuestasQueVuelvenAInicio.TryRemove(heartbeat.responseId, out _))
        {
            NavegacionesAdmin.TryRemove(heartbeat.responseId, out _);
            UltimaActividadParticipante.TryRemove(heartbeat.responseId, out _);
            return Results.Json(new { regresarInicio = true });
        }
        if (RespuestasQueVuelvenAEncuesta.ContainsKey(heartbeat.responseId))
            return Results.Json(new { regresarEncuesta = true });
        if (ExpulsadosLobby.TryGetValue(heartbeat.responseId, out DateTime expulsionHasta))
        {
            if (expulsionHasta > ahora) return Results.Json(new { expulsado = true, cantidad = ParticipantesLobby.Count });
            ExpulsadosLobby.TryRemove(heartbeat.responseId, out _);
        }
        if (RondaIniciada) return Results.Json(new { rondaIniciada = true, previewIniciada = PreviewRondaIniciada, etapaRondaUno = ObtenerEtapaRondaUno(), cantidad = ParticipantesLobby.Count });

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
    <script src=""/admin-navigation.js"" defer></script>
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

    SpectatorSession ObtenerOCrearEspectador(string sessionId)
    {
        lock (CandadoLobby)
        {
            if (SesionesEspectador.TryGetValue(sessionId, out SpectatorSession existente))
                return existente;

            var equiposActivos = SesionesEspectador.Values
                .Select(sesion => EquiposEspectadorPorParticipante[sesion.ResponseId])
                .DistinctBy(equipo => equipo.GroupNumber)
                .OrderBy(equipo => equipo.GroupNumber);
            RoundTeam equipo = equiposActivos.FirstOrDefault(equipo =>
                SesionesEspectador.Values.Count(sesion =>
                    EquiposEspectadorPorParticipante[sesion.ResponseId].GroupNumber == equipo.GroupNumber) < 3);
            int numeroEquipo = equipo?.GroupNumber ?? (equiposActivos.Select(e => e.GroupNumber).DefaultIfEmpty(5).Max() + 1);
            string idJugador = $"esp:{sessionId}:player";
            var sesionesEquipo = SesionesEspectador.Values
                .Where(sesion => equipo != null
                    ? EquiposEspectadorPorParticipante[sesion.ResponseId].GroupNumber == numeroEquipo
                    : false)
                .ToList();
            sesionesEquipo.Add(new SpectatorSession(sessionId, idJugador));
            var integrantes = new List<LobbyParticipant>();
            foreach (SpectatorSession sesion in sesionesEquipo)
            {
                DatosRespuesta datos = CrearDatosBot("ESP");
                datos.id = sesion.ResponseId;
                datos.nombre = "ESP";
                string svg = GeneradorEfigie.GenerarSVG(datos, LeerConfig(rutaConfig), incluirMarcoYNombre: false);
                integrantes.Add(new LobbyParticipant(sesion.ResponseId, "", "ESP", svg, DateTime.UtcNow, false, datos));
            }
            int cantidadBots = 3 - integrantes.Count;
            for (int indice = 1; indice <= cantidadBots; indice++)
            {
                string idBot = $"esp-bot:{numeroEquipo}:{indice}";
                DatosRespuesta datos = CrearDatosBot($"Bot {indice}");
                datos.id = idBot;
                datos.nombre = $"Bot {indice}";
                string svg = GeneradorEfigie.GenerarSVG(datos, LeerConfig(rutaConfig), incluirMarcoYNombre: false);
                integrantes.Add(new LobbyParticipant(idBot, "", datos.nombre, svg, DateTime.UtcNow, true, datos));
            }

            string[] ids = integrantes.Select(persona => persona.ResponseId).ToArray();
            var (puntaje, posibilidades) = EvaluarTrio(integrantes);
            var nuevoEquipo = new RoundTeam(numeroEquipo, ColoresGrupo[(numeroEquipo - 1) % ColoresGrupo.Length],
                ColoresTextoGrupo[(numeroEquipo - 1) % ColoresTextoGrupo.Length], ids, posibilidades, puntaje);
            var idsActuales = ids.ToHashSet(StringComparer.Ordinal);
            if (equipo != null)
            {
                foreach (string idBotAnterior in equipo.ParticipantIds.Where(id => id.StartsWith("esp-bot:", StringComparison.Ordinal)))
                {
                    if (idsActuales.Contains(idBotAnterior)) continue;
                    ParticipantesEspectador.TryRemove(idBotAnterior, out _);
                    EquiposEspectadorPorParticipante.TryRemove(idBotAnterior, out _);
                    EstadosRondaDos.TryRemove(ClaveEstadoRondaDos(idBotAnterior, false), out _);
                    EstadosRondaTres.TryRemove(ClaveEstadoRondaTres(idBotAnterior, false), out _);
                    PuntajesRondaDos.TryRemove(idBotAnterior, out _);
                    PuntajesRondaTres.TryRemove(idBotAnterior, out _);
                }
            }
            foreach (LobbyParticipant integrante in integrantes)
            {
                ParticipantesEspectador[integrante.ResponseId] = integrante;
                EquiposEspectadorPorParticipante[integrante.ResponseId] = nuevoEquipo;
                if (integrante.EsBot)
                {
                    ParticipantesListos[integrante.ResponseId] = true;
                    ParticipantesListosSiguienteRonda[integrante.ResponseId] = true;
                    ParticipantesListosRondaTres[integrante.ResponseId] = true;
                    ParticipantesListosRondaCuatro[integrante.ResponseId] = true;
                }
            }
            if (equipo != null)
                foreach (string id in equipo.ParticipantIds)
                    VotosRepresentanteRondaUno.TryRemove(id, out _);
            SembrarVotosBots(nuevoEquipo);
            if (equipo != null && PreguntasRondaTresPorEquipo.TryGetValue(FirmaEquipo(equipo), out RoundThreeQuestion[] preguntasEquipo))
                PreguntasRondaTresPorEquipo.TryAdd(FirmaEquipo(nuevoEquipo), preguntasEquipo);
            SesionesEspectador[sessionId] = new SpectatorSession(sessionId, idJugador);
            return SesionesEspectador[sessionId];
        }
    }

    bool TryObtenerParticipanteRonda(string responseId, out LobbyParticipant participante)
    {
        if (ParticipantesLobby.TryGetValue(responseId, out participante)) return true;
        return ParticipantesEspectador.TryGetValue(responseId, out participante);
    }

    bool TryObtenerEquipoRonda(string responseId, out int numeroGrupo, out RoundTeam equipo)
    {
        if (GrupoPorParticipante.TryGetValue(responseId, out numeroGrupo) &&
            numeroGrupo > 0 && numeroGrupo <= EquiposPreRonda.Count)
        {
            equipo = EquiposPreRonda[numeroGrupo - 1];
            return true;
        }
        if (EquiposEspectadorPorParticipante.TryGetValue(responseId, out equipo))
        {
            numeroGrupo = equipo.GroupNumber;
            return true;
        }
        numeroGrupo = 0;
        equipo = null;
        return false;
    }

    ConcurrentDictionary<string, string> ObtenerVotosRepresentante(int ronda) =>
        ronda == 2 ? VotosRepresentanteRondaDos : VotosRepresentanteRondaUno;

    bool TryObtenerRepresentante(RoundTeam equipo, out string representanteId, int ronda = 1)
    {
        ConcurrentDictionary<string, string> votos = ObtenerVotosRepresentante(ronda);
        representanteId = null;
        if (equipo.ParticipantIds.Length == 0 ||
            !votos.TryGetValue(equipo.ParticipantIds[0], out representanteId))
            return false;

        string candidatoAsignado = representanteId;
        return equipo.ParticipantIds.All(id =>
            votos.TryGetValue(id, out string voto) &&
            string.Equals(voto, candidatoAsignado, StringComparison.Ordinal));
    }

    void SembrarVotosBots(RoundTeam equipo, int ronda = 1)
    {
        if (equipo.ParticipantIds.Length == 0) return;
        if (equipo.ParticipantIds.Any(id =>
            TryObtenerParticipanteRonda(id, out LobbyParticipant participante) && !participante.EsBot))
            return;

        ConcurrentDictionary<string, string> votos = ObtenerVotosRepresentante(ronda);
        string candidatoPorDefecto = equipo.ParticipantIds[0];
        foreach (string id in equipo.ParticipantIds)
            if (TryObtenerParticipanteRonda(id, out LobbyParticipant participante) && participante.EsBot)
                votos[id] = candidatoPorDefecto;
    }

    bool TodosLosRepresentantesRondaDosAsignados() =>
        EquiposPreRonda.All(equipo => TryObtenerRepresentante(equipo, out _, ronda: 2));

    void IniciarEleccionRepresentanteRondaDos()
    {
        VotosRepresentanteRondaDos.Clear();
        EtapaRondaUno = 5;
        foreach (RoundTeam equipo in EquiposPreRonda)
            SembrarVotosBots(equipo, ronda: 2);
        foreach (RoundTeam equipo in EquiposEspectadorPorParticipante.Values.DistinctBy(FirmaEquipo))
            SembrarVotosBots(equipo, ronda: 2);
        if (!TodosLosRepresentantesRondaDosAsignados()) return;

        EtapaRondaUno = 6;
        InicializarEstadosRondaTres();
        InicializarRespuestasBotsRondaTres();
    }

    LobbyParticipant ObtenerParticipanteRonda(string responseId) =>
        TryObtenerParticipanteRonda(responseId, out LobbyParticipant participante)
            ? participante
            : throw new InvalidOperationException($"No se encontró al integrante {responseId}.");

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

            if (ParticipantesLobby.Count is < 3 or > 15 || ParticipantesLobby.Count % 3 != 0)
                return Results.Conflict();

            EquiposPreRonda = ConstruirEquiposOptimos(
                ParticipantesLobby.Values.ToList(),
                priorizarParticipantesReales: completarConBots);
            GrupoPorParticipante.Clear();
            ParticipantesListos.Clear();
            VotosRepresentanteRondaUno.Clear();
            VotosRepresentanteRondaDos.Clear();
            foreach (RoundTeam equipo in EquiposPreRonda)
            {
                foreach (string id in equipo.ParticipantIds)
                {
                    GrupoPorParticipante[id] = equipo.GroupNumber;
                    if (botsListos && ParticipantesLobby.TryGetValue(id, out var participante) && participante.EsBot)
                        ParticipantesListos[id] = true;
                }
                SembrarVotosBots(equipo);
            }
            foreach (RoundTeam equipoEspectador in EquiposEspectadorPorParticipante.Values.DistinctBy(FirmaEquipo))
                SembrarVotosBots(equipoEspectador);
            foreach (LobbyParticipant participante in ParticipantesEspectador.Values.Where(persona => persona.EsBot))
                ParticipantesListos[participante.ResponseId] = true;

            PreviewRondaIniciada = false;
            EtapaRondaUno = 0;
            InicioEtapaRondaUnoUtc = null;
            SegundosExtraRondaUno = 0;
            ParticipantesListosSiguienteRonda.Clear();
            ParticipantesListosRondaTres.Clear();
            ParticipantesListosRondaCuatro.Clear();
            NavegacionesAdmin.Clear();
            PuntajesRondaUno.Clear();
            PuntajesRondaDos.Clear();
            PuntajesRondaTres.Clear();
            EstadosRondaCuatro.Clear();
            PuntajesRondaCuatro.Clear();
            EstadosRondaDos.Clear();
            EstadosRondaTres.Clear();
            RespuestasQueVuelvenAEncuesta.Clear();
            RondaIniciada = true;
        }
        return Results.Json(new { estado = "ok", rondaIniciada = true, equipos = EquiposPreRonda.Count, participantes = ParticipantesLobby.Count });
    }

    List<RoundTeam> ConstruirEquiposOptimos(
        List<LobbyParticipant> participantes,
        bool priorizarParticipantesReales = false)
    {
        const int reinicios = 8;
        const int iteraciones = 5000;
        int cantidadReales = participantes.Count(participante => !participante.EsBot);
        List<LobbyParticipant> mejorOrden = null;
        int mejorPuntaje = int.MinValue;

        int EvaluarOrden(IReadOnlyList<LobbyParticipant> orden)
        {
            int total = 0;
            for (int grupo = 0; grupo < orden.Count / 3; grupo++)
            {
                LobbyParticipant[] integrantes = orden.Skip(grupo * 3).Take(3).ToArray();
                int pesoCompatibilidad = priorizarParticipantesReales && integrantes.Any(persona => !persona.EsBot)
                    ? 100
                    : 1;
                total += EvaluarTrio(integrantes).Puntaje * pesoCompatibilidad;
            }
            return total;
        }

        for (int reinicio = 0; reinicio < reinicios; reinicio++)
        {
            List<LobbyParticipant> actualesReales = participantes.Where(persona => !persona.EsBot).ToList();
            List<LobbyParticipant> actualesBots = participantes.Where(persona => persona.EsBot).ToList();
            List<LobbyParticipant> actual;
            if (priorizarParticipantesReales)
            {
                Barajar(actualesReales);
                Barajar(actualesBots);
                actual = actualesReales.Concat(actualesBots).ToList();
            }
            else
            {
                actual = participantes.ToList();
                Barajar(actual);
            }

            int puntajeActual = EvaluarOrden(actual);
            if (puntajeActual > mejorPuntaje)
            {
                mejorPuntaje = puntajeActual;
                mejorOrden = actual.ToList();
            }

            for (int iteracion = 0; iteracion < iteraciones; iteracion++)
            {
                int primero = Random.Shared.Next(actual.Count);
                int segundo = Random.Shared.Next(actual.Count);
                if (primero / 3 == segundo / 3) continue;
                if (priorizarParticipantesReales &&
                    (primero < cantidadReales) != (segundo < cantidadReales))
                    continue;

                (actual[primero], actual[segundo]) = (actual[segundo], actual[primero]);
                int puntajeNuevo = EvaluarOrden(actual);
                double escalaTemperatura = priorizarParticipantesReales ? 100 : 1;
                double temperatura = escalaTemperatura *
                    (1.5 * (1.0 - iteracion / (double)iteraciones) + 0.05);
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

        var equipos = new List<RoundTeam>(mejorOrden.Count / 3);
        for (int grupo = 0; grupo < mejorOrden.Count / 3; grupo++)
        {
            var trio = mejorOrden.Skip(grupo * 3).Take(3).ToArray();
            var (puntaje, posibilidades) = EvaluarTrio(trio);
            equipos.Add(new RoundTeam(grupo + 1, ColoresGrupo[grupo], ColoresTextoGrupo[grupo], trio.Select(persona => persona.ResponseId).ToArray(), posibilidades, puntaje));
        }
        return equipos;

        static void Barajar(List<LobbyParticipant> integrantes)
        {
            for (int indice = integrantes.Count - 1; indice > 0; indice--)
            {
                int siguiente = Random.Shared.Next(indice + 1);
                (integrantes[indice], integrantes[siguiente]) =
                    (integrantes[siguiente], integrantes[indice]);
            }
        }
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

    void IniciarRondaUno()
    {
        if (PreviewRondaIniciada) return;
        InicioEtapaRondaUnoUtc = DateTime.UtcNow;
        EtapaRondaUno = 1;
        SegundosExtraRondaUno = 0;
        PreviewRondaIniciada = true;
    }

    void SimularRespuestasBots()
    {
        foreach (RoundTeam equipo in EquiposPreRonda)
        {
            if (!TryObtenerRepresentante(equipo, out string representanteId) ||
                !TryObtenerParticipanteRonda(representanteId, out LobbyParticipant representante) ||
                !representante.EsBot || equipo.ParticipantIds.Any(id => PuntajesRondaUno.ContainsKey(id)))
                continue;

            int puntos = CalcularPuntajeRondaUno(Random.Shared.Next(0, 4));
            foreach (string id in equipo.ParticipantIds)
                PuntajesRondaUno.TryAdd(id, puntos);
        }
        foreach (LobbyParticipant participante in ParticipantesEspectador.Values.Where(persona => persona.EsBot))
        {
            if (!TryObtenerEquipoRonda(participante.ResponseId, out _, out RoundTeam equipoEspectador) ||
                !TryObtenerRepresentante(equipoEspectador, out string representanteId) ||
                !TryObtenerParticipanteRonda(representanteId, out LobbyParticipant representante) ||
                !representante.EsBot || equipoEspectador.ParticipantIds.Any(id => PuntajesRondaUno.ContainsKey(id)))
                continue;

            int puntos = CalcularPuntajeRondaUno(Random.Shared.Next(0, 4));
            foreach (string id in equipoEspectador.ParticipantIds)
                PuntajesRondaUno.TryAdd(id, puntos);
        }
    }

    int CalcularPuntajeRondaUno(int aciertos) => aciertos * 50;

    void InicializarRondaCuatro()
    {
        EstadosRondaCuatro.Clear();
        PuntajesRondaCuatro.Clear();
        LobbyParticipant[] participantesReales = ParticipantesLobby.Values
            .Where(participante => !participante.EsBot)
            .OrderBy(participante => participante.ResponseId, StringComparer.Ordinal)
            .ToArray();
        RoundFourTrait[] rasgos = RasgosRondaCuatro();

        foreach (LobbyParticipant participante in participantesReales)
        {
            if (!TryObtenerEquipoRonda(participante.ResponseId, out _, out RoundTeam equipo))
                continue;
            var opcionesPorRasgo = rasgos.Select(rasgo =>
            {
                var grupos = participantesReales
                    .Select(persona => new { Persona = persona, Valor = rasgo.GetValue(persona.Datos).Trim() })
                    .Where(item => !string.IsNullOrWhiteSpace(item.Valor))
                    .GroupBy(item => item.Valor, StringComparer.OrdinalIgnoreCase)
                    .Select(grupo => new
                    {
                        Valor = grupo.First().Valor,
                        Integrantes = grupo.Select(item => item.Persona).ToArray()
                    })
                    .ToArray();
                var valoresExternos = grupos
                    .Where(grupo => grupo.Integrantes.Any(persona =>
                        !equipo.ParticipantIds.Contains(persona.ResponseId, StringComparer.Ordinal)))
                    .ToArray();
                var valoresDeCompaneros = grupos
                    .Where(grupo => grupo.Integrantes.Any(persona =>
                        persona.ResponseId != participante.ResponseId))
                    .ToArray();
                var valoresPropios = grupos
                    .Where(grupo => grupo.Integrantes.Any(persona =>
                        persona.ResponseId == participante.ResponseId))
                    .ToArray();
                var candidatos = valoresExternos.Length > 0
                    ? valoresExternos
                    : valoresDeCompaneros.Length > 0
                        ? valoresDeCompaneros
                        : valoresPropios;
                return new
                {
                    Rasgo = rasgo,
                    Prioridad = valoresExternos.Length > 0 ? 0 : valoresDeCompaneros.Length > 0 ? 1 : 2,
                    Valores = candidatos
                };
            })
            .Where(item => item.Valores.Length > 0)
            .OrderBy(item => item.Prioridad)
            .ThenBy(_ => Random.Shared.Next())
            .Take(3)
            .ToArray();

            if (opcionesPorRasgo.Length != 3)
                throw new InvalidOperationException($"No se pudieron crear tres consignas válidas para {participante.Nombre}.");

            RoundFourTask[] tareas = opcionesPorRasgo.Select(item =>
            {
                var valor = item.Valores[Random.Shared.Next(item.Valores.Length)];
                return new RoundFourTask
                {
                    Property = item.Rasgo.Property,
                    Prompt = FormatearConsignaRondaCuatro(item.Rasgo.Property, valor.Valor),
                    Value = valor.Valor
                };
            }).ToArray();
            EstadosRondaCuatro[participante.ResponseId] = new RoundFourParticipantState { Tasks = tareas };
        }
    }

    RoundFourTrait[] RasgosRondaCuatro() =>
    new[]
    {
        new RoundFourTrait("anio", datos => datos.anio),
        new RoundFourTrait("facultad", datos => datos.facultad),
        new RoundFourTrait("etapa", datos => datos.etapa),
        new RoundFourTrait("estacion", datos => datos.estacion),
        new RoundFourTrait("grupal", datos => datos.grupal),
        new RoundFourTrait("formato", datos => datos.formato),
        new RoundFourTrait("rol", datos => datos.rol)
    };

    string FormatearConsignaRondaCuatro(string propiedad, string valor) =>
        propiedad switch
        {
            "anio" => $"Encontrá a alguien que haya nacido en {valor}.",
            "facultad" => $"Encontrá a alguien que pertenezca a la facultad «{valor}».",
            "etapa" => $"Encontrá a alguien cuya etapa de carrera o rol sea «{valor}».",
            "estacion" => $"Encontrá a alguien cuya estación favorita sea «{valor}».",
            "grupal" => $"Encontrá a alguien que prefiera trabajar «{valor}».",
            "formato" => $"Encontrá a alguien que prefiera disfrutar historias en formato «{valor}».",
            "rol" => $"Encontrá a alguien cuyo rol en un trabajo grupal sea «{valor}».",
            _ => throw new InvalidOperationException($"Rasgo de ronda 3 desconocido: {propiedad}.")
        };

    string ValorRasgoRondaCuatro(string propiedad, DatosRespuesta datos) =>
        RasgosRondaCuatro().First(rasgo => rasgo.Property == propiedad).GetValue(datos).Trim();

    int CalcularPuntajeRondaCuatro(int posicion) =>
        posicion switch
        {
            1 => 250,
            2 => 200,
            3 => 150,
            _ => Math.Max(0, 50 - ((posicion - 4) * 5))
        };

    string ObtenerEtapaRondaUno()
    {
        lock (CandadoLobby)
            return EtapasRondaUno[Math.Clamp(EtapaRondaUno, 0, EtapasRondaUno.Length - 1)];
    }

    int ObtenerSegundosRestantesRondaUno()
    {
        lock (CandadoLobby)
        {
            return ObtenerSegundosRestantesEtapaActual();
        }
    }

    int ObtenerSegundosRestantesEtapaActual()
    {
        if (!InicioEtapaRondaUnoUtc.HasValue || EtapaRondaUno is not (1 or 2 or 5)) return 0;
        int duracion = EtapaRondaUno switch
        {
            1 => 30 + SegundosExtraRondaUno,
            2 => 120 + SegundosExtraRondaUno,
            5 => 30,
            _ => 0
        };
        int transcurridos = (int)Math.Ceiling((DateTime.UtcNow - InicioEtapaRondaUnoUtc.Value).TotalSeconds);
        return Math.Max(0, duracion - transcurridos);
    }

    string ClaveEstadoRondaDos(string responseId, bool adminPreview) =>
        adminPreview ? $"admin-preview:{responseId}" : responseId;

    string ClaveEstadoRondaTres(string responseId, bool adminPreview) =>
        adminPreview ? $"admin-preview:{responseId}" : responseId;

    string ClaveProgresoEquipoRondaTres(RoundTeam equipo, bool adminPreview) =>
        adminPreview ? $"admin-preview:{FirmaEquipo(equipo)}" : FirmaEquipo(equipo);

    string FirmaEquipo(RoundTeam equipo) =>
        string.Join("|", equipo.ParticipantIds.OrderBy(id => id, StringComparer.Ordinal));

    RoundThreeTeamProgress ObtenerProgresoEquipoRondaTres(RoundTeam equipo, bool adminPreview) =>
        ProgresoEquipoRondaTres.GetOrAdd(ClaveProgresoEquipoRondaTres(equipo, adminPreview), _ => new RoundThreeTeamProgress());

    RoundThreeParticipantState ObtenerEstadoRondaTres(string responseId, bool adminPreview) =>
        EstadosRondaTres.GetOrAdd(ClaveEstadoRondaTres(responseId, adminPreview), claveEstado =>
        {
            if (!TryObtenerEquipoRonda(responseId, out _, out RoundTeam equipo))
                throw new InvalidOperationException($"No se encontró el equipo del integrante {responseId}.");
            RoundThreeQuestion[] preguntas = PreguntasRondaTresPorEquipo.GetOrAdd(
                FirmaEquipo(equipo), firmaEquipo => PreguntasRondaTres.OrderBy(_ => Random.Shared.Next()).ToArray());
            return new RoundThreeParticipantState
            {
                Questions = preguntas,
                Answers = Enumerable.Repeat("", preguntas.Length).ToArray(),
                HintsUsed = new bool[preguntas.Length]
            };
        });

    void InicializarEstadosRondaTres()
    {
        EstadosRondaTres.Clear();
        PuntajesRondaTres.Clear();
        PreguntasRondaTresPorEquipo.Clear();
        ProgresoEquipoRondaTres.Clear();
        ParticipantesListosRondaCuatro.Clear();
        foreach (LobbyParticipant participante in ParticipantesLobby.Values.Where(persona => !persona.EsBot))
            _ = ObtenerEstadoRondaTres(participante.ResponseId, adminPreview: false);
    }

    int CalcularPuntajeRondaTres(RoundThreeParticipantState estado) =>
        estado.Answers.Select((respuesta, indice) =>
            respuesta == estado.Questions[indice].Answer
                ? estado.HintsUsed[indice] ? 50 : 100
                : 0).Sum();

    void FinalizarRespuestasRondaTres(string responseId, RoundThreeParticipantState estado, bool adminPreview)
    {
        estado.Submitted = true;
        estado.Points = CalcularPuntajeRondaTres(estado);
        if (!adminPreview)
            PuntajesRondaTres[responseId] = estado.Points;
    }

    void InicializarRespuestasBotsRondaTres()
    {
        foreach (RoundTeam equipo in EquiposPreRonda.Concat(EquiposEspectadorPorParticipante.Values
            .DistinctBy(FirmaEquipo)))
        {
            LobbyParticipant[] integrantes = equipo.ParticipantIds
                .Select(id => TryObtenerParticipanteRonda(id, out LobbyParticipant participante) ? participante : null)
                .OfType<LobbyParticipant>()
                .ToArray();
            if (integrantes.Any(participante => !participante.EsBot)) continue;
            foreach (LobbyParticipant participante in integrantes)
            {
                RoundThreeParticipantState estado = ObtenerEstadoRondaTres(participante.ResponseId, adminPreview: false);
                lock (estado.SyncRoot)
                {
                    if (estado.Submitted) continue;
                    estado.Answers = estado.Questions.Select(pregunta => pregunta.Answer).ToArray();
                    estado.HintsUsed = new bool[estado.Questions.Length];
                    FinalizarRespuestasRondaTres(participante.ResponseId, estado, adminPreview: false);
                }
            }
        }
    }

    int ContarRespuestasPregunta(RoundTeam equipo, int indice, bool adminPreview) =>
        equipo.ParticipantIds.Count(id =>
            TryObtenerParticipanteRonda(id, out LobbyParticipant participante) &&
            !participante.EsBot &&
            ObtenerEstadoRondaTres(id, adminPreview).Answers[indice].Length > 0);

    bool ObtenerConsensoPreguntaActual(RoundTeam equipo, int indice, bool adminPreview)
    {
        LobbyParticipant[] participantes = equipo.ParticipantIds
            .Select(id => TryObtenerParticipanteRonda(id, out LobbyParticipant participante) ? participante : null)
            .Where(participante => participante != null && !participante.EsBot)
            .ToArray()!;
        if (participantes.Length == 0) return false;
        RoundThreeParticipantState[] estados = participantes
            .Select(participante => ObtenerEstadoRondaTres(participante!.ResponseId, adminPreview))
            .ToArray();
        string[] respuestas = estados.Select(estado =>
        {
            lock (estado.SyncRoot)
                return estado.Answers[indice];
        }).ToArray();
        if (respuestas.Any(string.IsNullOrWhiteSpace) ||
            respuestas.Distinct(StringComparer.Ordinal).Count() != 1)
            return false;

        string respuestaConsensuada = respuestas[0];
        foreach (string id in equipo.ParticipantIds)
            if (!adminPreview &&
                TryObtenerParticipanteRonda(id, out LobbyParticipant participante) && participante.EsBot)
            {
                RoundThreeParticipantState estado = ObtenerEstadoRondaTres(id, adminPreview);
                lock (estado.SyncRoot)
                    estado.Answers[indice] = respuestaConsensuada;
            }
        return true;
    }

    bool FinalizarEquipoRondaTres(RoundTeam equipo, bool adminPreview)
    {
        if (!Enumerable.Range(0, PreguntasRondaTres.Length)
            .All(indice => ObtenerConsensoPreguntaActual(equipo, indice, adminPreview)))
            return false;

        foreach (string id in equipo.ParticipantIds)
        {
            if (!TryObtenerParticipanteRonda(id, out LobbyParticipant participante)) continue;
            RoundThreeParticipantState estado = ObtenerEstadoRondaTres(id, adminPreview);
            lock (estado.SyncRoot)
            {
                FinalizarRespuestasRondaTres(id, estado, adminPreview);
            }
        }
        return true;
    }

    bool TodosLosEquiposFinalizados()
    {
        RoundTeam[] equipos = EquiposPreRonda
            .Concat(EquiposEspectadorPorParticipante.Values)
            .DistinctBy(FirmaEquipo)
            .ToArray();
        return equipos.Length > 0 && equipos.All(equipo => equipo.ParticipantIds
            .Where(id => TryObtenerParticipanteRonda(id, out _))
            .All(id => EstadosRondaTres.TryGetValue(
                ClaveEstadoRondaTres(id, adminPreview: false),
                out RoundThreeParticipantState estado) && estado.Submitted));
    }

    void CompletarRespuestasRondaTresPorForzarAvance()
    {
        foreach (RoundTeam equipo in EquiposPreRonda.Concat(EquiposEspectadorPorParticipante.Values.DistinctBy(FirmaEquipo)))
        {
            foreach (string id in equipo.ParticipantIds)
            {
                if (!TryObtenerParticipanteRonda(id, out _)) continue;
                RoundThreeParticipantState estado = ObtenerEstadoRondaTres(id, adminPreview: false);
                lock (estado.SyncRoot)
                    if (!estado.Submitted)
                        FinalizarRespuestasRondaTres(id, estado, adminPreview: false);
            }
        }
    }

    RoundTwoParticipantState CrearEstadoRondaDos(string responseId)
    {
        if (!TryObtenerEquipoRonda(responseId, out int numeroGrupo, out RoundTeam equipo))
            throw new InvalidOperationException($"No se encontró el equipo del integrante {responseId}.");
        LobbyParticipant[] companeros = equipo.ParticipantIds
            .Where(id => id != responseId)
            .Select(ObtenerParticipanteRonda)
            .OrderBy(_ => Random.Shared.Next())
            .ToArray();
        if (companeros.Length != 2)
            throw new InvalidOperationException($"El equipo {numeroGrupo} no tiene dos compañeros para la ronda 2.");

        string[] capas = { "anio", "estaciones", "carrera", "rol", "historia", "conexiones" };
        string[] nombresCapas = { "Año", "Estaciones", "Carrera", "Rol", "Historia", "Conexiones" };
        var desafios = new RoundTwoChallenge[companeros.Length];
        for (int indice = 0; indice < companeros.Length; indice++)
        {
            LobbyParticipant companero = companeros[indice];
            string capaCambiada = capas[Random.Shared.Next(capas.Length)];
            DatosRespuesta datosCambiados = DatosConCapaCambiada(companero.Datos, capaCambiada);
            string svgOriginal = GeneradorEfigie.GenerarSVG(companero.Datos, LeerConfig(rutaConfig), incluirMarcoYNombre: false);
            string svgCambiadoMotor = GeneradorEfigie.GenerarSVG(datosCambiados, LeerConfig(rutaConfig), incluirMarcoYNombre: false);
            string grupoCambiado = ExtraerGrupoSvg(svgCambiadoMotor, capaCambiada);
            string svgCambiado = ReemplazarGrupoSvg(svgOriginal, capaCambiada, grupoCambiado);

            var opcionesCapas = capas
                .Where(capa => capa != capaCambiada)
                .OrderBy(_ => Random.Shared.Next())
                .Take(3)
                .Append(capaCambiada)
                .OrderBy(_ => Random.Shared.Next())
                .ToArray();
            var opciones = opcionesCapas.Select(capa => new RoundTwoChoice(
                capa,
                nombresCapas[Array.IndexOf(capas, capa)],
                SvgSoloGrupo(svgCambiado, capa))).ToArray();

            desafios[indice] = new RoundTwoChallenge
            {
                TargetName = companero.Nombre,
                OriginalSvg = svgOriginal,
                ChangedSvg = svgCambiado,
                ChangedLayer = capaCambiada,
                Choices = opciones
            };
        }
        return new RoundTwoParticipantState { Challenges = desafios };
    }

    DatosRespuesta DatosConCapaCambiada(DatosRespuesta original, string capa)
    {
        var cambiado = new DatosRespuesta
        {
            id = original.id,
            nombre = original.nombre,
            anio = original.anio,
            facultad = original.facultad,
            etapa = original.etapa,
            estacion = original.estacion,
            grupal = original.grupal,
            formato = original.formato,
            rol = original.rol,
            deviceId = original.deviceId
        };
        switch (capa)
        {
            case "anio":
                string[] anios = { "1995", "1998", "2001", "2004", "2007", "2010", "2013" };
                cambiado.anio = anios.Where(anio => anio != original.anio).OrderBy(_ => Random.Shared.Next()).First();
                break;
            case "estaciones":
                string[] estaciones = { "Verano", "Otoño", "Invierno", "Primavera" };
                cambiado.estacion = estaciones.Where(estacion => estacion != original.estacion).OrderBy(_ => Random.Shared.Next()).First();
                break;
            case "carrera":
                string[] etapas = { "1 (Ingresante / Primer año)", "2", "3", "4", "5", "6 (Sexto año o más)", "Docente", "No Docente" };
                cambiado.etapa = etapas.Where(etapa => etapa != original.etapa).OrderBy(_ => Random.Shared.Next()).First();
                break;
            case "rol":
                string[] roles = { "Proponer (Generar ideas iniciales)", "Organizar (Estructurar y planificar)", "Materializar (Llevar a la práctica)", "Mejorar (Revisar y pulir detalles)" };
                cambiado.rol = roles.Where(rol => rol != original.rol).OrderBy(_ => Random.Shared.Next()).First();
                break;
            case "historia":
                string[] formatos = { "Película", "Serie", "Libro", "Videojuego", "Teatro" };
                cambiado.formato = formatos.Where(formato => formato != original.formato).OrderBy(_ => Random.Shared.Next()).First();
                break;
            case "conexiones":
                cambiado.grupal = original.grupal.Contains("grupal", StringComparison.OrdinalIgnoreCase) ? "Solo" : "Grupal";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(capa), capa, "La capa no se puede modificar en la ronda 2.");
        }
        return cambiado;
    }

    string ObtenerGrupoSvg(string svg, string capa)
    {
        Match match = Regex.Matches(svg, @"<g\s+data-capa=""([^""]+)""[^>]*>[\s\S]*?</g>")
            .Cast<Match>()
            .FirstOrDefault(grupo => grupo.Groups[1].Value == capa);
        if (match == null)
            throw new InvalidOperationException($"El SVG generado no contiene la capa '{capa}'.");
        return match.Value;
    }

    string ExtraerGrupoSvg(string svg, string capa) => ObtenerGrupoSvg(svg, capa);

    string ReemplazarGrupoSvg(string svg, string capa, string grupoNuevo)
    {
        bool reemplazado = false;
        string resultado = Regex.Replace(svg, @"<g\s+data-capa=""([^""]+)""[^>]*>[\s\S]*?</g>", match =>
        {
            if (match.Groups[1].Value != capa) return match.Value;
            reemplazado = true;
            return grupoNuevo;
        });
        if (!reemplazado)
            throw new InvalidOperationException($"No se pudo reemplazar la capa '{capa}' del SVG.");
        return resultado;
    }

    string SvgSoloGrupo(string svg, string capa)
    {
        Match viewBox = Regex.Match(svg, @"viewBox=""([^""]+)""");
        if (!viewBox.Success)
            throw new InvalidOperationException("El SVG generado no contiene un viewBox.");
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{viewBox.Groups[1].Value}\" width=\"100%\" height=\"100%\" fill=\"none\" stroke=\"#000000\" stroke-width=\"0.4\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{ObtenerGrupoSvg(svg, capa)}</svg>";
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

public class RepresentanteRondaUnoRequest
{
    public string responseId { get; set; } = "";
    public string representanteId { get; set; } = "";
    public int ronda { get; set; } = 1;
}

public class AdivinanzaRondaUnoRequest
{
    public string responseId { get; set; } = "";
    public string[] seleccionadas { get; set; } = Array.Empty<string>();
}

public class RoundTwoRequest
{
    public string responseId { get; set; } = "";
    public int indice { get; set; }
    public string capa { get; set; } = "";
}
