using Microsoft.Data.Sqlite;

public sealed record ParticipanteComunidad(DatosRespuesta Datos, string Efigie);
public sealed record CoincidenciaComunidad(string Categoria, string Valor);
public sealed record FiguraCercana(ParticipanteComunidad Participante, IReadOnlyList<CoincidenciaComunidad> Coincidencias);

public sealed class RegistroComunidad
{
    private readonly string cadenaConexion;

    public RegistroComunidad(string ruta)
    {
        string directorio = Path.GetDirectoryName(Path.GetFullPath(ruta));
        Directory.CreateDirectory(directorio);
        cadenaConexion = new SqliteConnectionStringBuilder { DataSource = ruta }.ToString();

        using var conexion = AbrirConexion();
        using var comando = conexion.CreateCommand();
        comando.CommandText = """
            CREATE TABLE IF NOT EXISTS participantes (
                orden INTEGER PRIMARY KEY AUTOINCREMENT,
                id TEXT NOT NULL UNIQUE,
                nombre TEXT NOT NULL,
                nombre_clave TEXT NOT NULL,
                anio TEXT NOT NULL,
                anio_clave TEXT NOT NULL,
                facultad TEXT NOT NULL,
                etapa TEXT NOT NULL,
                etapa_clave TEXT NOT NULL,
                estacion TEXT NOT NULL,
                grupal TEXT NOT NULL,
                formato TEXT NOT NULL,
                rol TEXT NOT NULL,
                efigie TEXT NOT NULL
            )
            """;
        comando.ExecuteNonQuery();
        comando.CommandText = "CREATE INDEX IF NOT EXISTS idx_participantes_nombre ON participantes (nombre_clave)";
        comando.ExecuteNonQuery();
    }

    public void Registrar(IEnumerable<ParticipanteComunidad> participantes)
    {
        using var conexion = AbrirConexion();
        using var transaccion = conexion.BeginTransaction();
        using var borrarAnterior = conexion.CreateCommand();
        borrarAnterior.Transaction = transaccion;
        borrarAnterior.CommandText = """
            DELETE FROM participantes
            WHERE id = $id OR (nombre_clave = $nombreClave AND (anio_clave = $anioClave OR etapa_clave = $etapaClave))
            """;
        borrarAnterior.Parameters.Add("$nombreClave", SqliteType.Text);
        borrarAnterior.Parameters.Add("$anioClave", SqliteType.Text);
        borrarAnterior.Parameters.Add("$etapaClave", SqliteType.Text);
        borrarAnterior.Parameters.Add("$id", SqliteType.Text);
        using var comando = conexion.CreateCommand();
        comando.Transaction = transaccion;
        comando.CommandText = """
            INSERT INTO participantes (id, nombre, nombre_clave, anio, anio_clave, facultad, etapa, etapa_clave, estacion, grupal, formato, rol, efigie)
            VALUES ($id, $nombre, $nombreClave, $anio, $anioClave, $facultad, $etapa, $etapaClave, $estacion, $grupal, $formato, $rol, $efigie)
            """;
        foreach (string campo in new[] { "$id", "$nombre", "$nombreClave", "$anio", "$anioClave", "$facultad", "$etapa", "$etapaClave", "$estacion", "$grupal", "$formato", "$rol", "$efigie" })
            comando.Parameters.Add(campo, SqliteType.Text);

        foreach (ParticipanteComunidad participante in participantes)
        {
            DatosRespuesta datos = participante.Datos;
            if (string.IsNullOrWhiteSpace(datos.id) || string.IsNullOrWhiteSpace(datos.nombre) ||
                string.IsNullOrWhiteSpace(participante.Efigie))
                throw new InvalidOperationException("No se puede registrar un participante sin identificador, nombre o efigie.");

            string nombreClave = Normalizar(datos.nombre);
            string anioClave = Normalizar(datos.anio);
            string etapaClave = Normalizar(datos.etapa);
            borrarAnterior.Parameters["$nombreClave"].Value = nombreClave;
            borrarAnterior.Parameters["$anioClave"].Value = anioClave;
            borrarAnterior.Parameters["$etapaClave"].Value = etapaClave;
            borrarAnterior.Parameters["$id"].Value = datos.id;
            borrarAnterior.ExecuteNonQuery();
            comando.Parameters["$id"].Value = datos.id;
            comando.Parameters["$nombre"].Value = datos.nombre;
            comando.Parameters["$nombreClave"].Value = nombreClave;
            comando.Parameters["$anio"].Value = datos.anio ?? "";
            comando.Parameters["$anioClave"].Value = anioClave;
            comando.Parameters["$facultad"].Value = datos.facultad ?? "";
            comando.Parameters["$etapa"].Value = datos.etapa ?? "";
            comando.Parameters["$etapaClave"].Value = etapaClave;
            comando.Parameters["$estacion"].Value = datos.estacion ?? "";
            comando.Parameters["$grupal"].Value = datos.grupal ?? "";
            comando.Parameters["$formato"].Value = datos.formato ?? "";
            comando.Parameters["$rol"].Value = datos.rol ?? "";
            comando.Parameters["$efigie"].Value = participante.Efigie;
            comando.ExecuteNonQuery();
        }
        transaccion.Commit();
    }

    public ParticipanteComunidad BuscarPorCredenciales(string nombre, string estacion) =>
        LeerParticipantes().LastOrDefault(participante =>
            Coinciden(participante.Datos.nombre, nombre) && Coinciden(participante.Datos.estacion, estacion));

    public IReadOnlyList<FiguraCercana> BuscarFigurasCercanas(string id) =>
        BuscarFigurasCercanas(LeerParticipantes(), id);

    public static IReadOnlyList<FiguraCercana> BuscarFigurasCercanas(IEnumerable<ParticipanteComunidad> origen, string id)
    {
        List<ParticipanteComunidad> participantes = origen.ToList();
        ParticipanteComunidad usuario = participantes.FirstOrDefault(participante => participante.Datos.id == id);
        if (usuario == null) return null;

        var figuras = participantes
            .Where(participante => participante.Datos.id != id)
            .Select(participante => new FiguraCercana(participante, Comparar(usuario.Datos, participante.Datos)))
            .OrderByDescending(figura => figura.Coincidencias.Count)
            .ToList();
        if (figuras.Count == 0) return figuras;
        int maximo = figuras[0].Coincidencias.Count;
        return figuras.Where(figura => figura.Coincidencias.Count == maximo).Take(2).ToList();
    }

    private static IReadOnlyList<CoincidenciaComunidad> Comparar(DatosRespuesta usuario, DatosRespuesta otro)
    {
        var campos = new (string Categoria, string Propio, string Ajeno)[]
        {
            ("Año de nacimiento", usuario.anio, otro.anio),
            ("Facultad", usuario.facultad, otro.facultad),
            ("Etapa o rol académico", usuario.etapa, otro.etapa),
            ("Estación favorita", usuario.estacion, otro.estacion),
            ("Forma de trabajar o estudiar", usuario.grupal, otro.grupal),
            ("Formato de historia", usuario.formato, otro.formato),
            ("Rol en equipo", usuario.rol, otro.rol)
        };
        return campos.Where(campo => Coinciden(campo.Propio, campo.Ajeno))
            .Select(campo => new CoincidenciaComunidad(campo.Categoria, campo.Propio.Trim()))
            .ToList();
    }

    private static bool Coinciden(string primero, string segundo) =>
        !string.IsNullOrWhiteSpace(primero) && !string.IsNullOrWhiteSpace(segundo) &&
        string.Equals(primero.Trim(), segundo.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Normalizar(string valor) => (valor ?? "").Trim().ToUpperInvariant();

    private SqliteConnection AbrirConexion()
    {
        var conexion = new SqliteConnection(cadenaConexion);
        conexion.Open();
        return conexion;
    }

    private List<ParticipanteComunidad> LeerParticipantes()
    {
        using var conexion = AbrirConexion();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT id, nombre, anio, facultad, etapa, estacion, grupal, formato, rol, efigie FROM participantes ORDER BY orden";
        using var lector = comando.ExecuteReader();
        var participantes = new List<ParticipanteComunidad>();
        while (lector.Read())
        {
            var datos = new DatosRespuesta
            {
                id = lector.GetString(0),
                nombre = lector.GetString(1),
                anio = lector.GetString(2),
                facultad = lector.GetString(3),
                etapa = lector.GetString(4),
                estacion = lector.GetString(5),
                grupal = lector.GetString(6),
                formato = lector.GetString(7),
                rol = lector.GetString(8)
            };
            participantes.Add(new ParticipanteComunidad(datos, lector.GetString(9)));
        }
        return participantes;
    }
}
