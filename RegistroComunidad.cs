using Microsoft.Data.Sqlite;

public sealed record ParticipanteComunidad(DatosRespuesta Datos, string Efigie, string EfigieCompleta = "");
public sealed record CoincidenciaComunidad(string Categoria, string Valor);
public sealed record FiguraCercana(ParticipanteComunidad Participante, IReadOnlyList<CoincidenciaComunidad> Coincidencias);
public enum OrigenParticipante { Experiencia, Prueba }

public sealed class NombreOcupadoException : Exception
{
    public NombreOcupadoException() : base("¡Ocupado! Prueba con otro apodo.") { }
}

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
        using var transaccion = conexion.BeginTransaction();
        comando.Transaction = transaccion;
        comando.CommandText = "PRAGMA table_info(participantes)";
        bool tieneEfigieCompleta = false;
        bool tieneOrigen = false;
        using (var columnas = comando.ExecuteReader())
            while (columnas.Read())
            {
                if (columnas.GetString(1) == "efigie_completa") tieneEfigieCompleta = true;
                if (columnas.GetString(1) == "origen") tieneOrigen = true;
            }
        if (!tieneEfigieCompleta)
        {
            comando.CommandText = "ALTER TABLE participantes ADD COLUMN efigie_completa TEXT NOT NULL DEFAULT ''";
            comando.ExecuteNonQuery();
        }
        if (!tieneOrigen)
        {
            comando.CommandText = "ALTER TABLE participantes ADD COLUMN origen TEXT NOT NULL DEFAULT 'sin_clasificar' CHECK (origen IN ('experiencia', 'prueba', 'sin_clasificar'))";
            comando.ExecuteNonQuery();
        }
        comando.CommandText = "SELECT id, nombre FROM participantes";
        var nombres = new List<(string Id, string Clave)>();
        using (var lector = comando.ExecuteReader())
            while (lector.Read())
                nombres.Add((lector.GetString(0), NormalizarNombre(lector.GetString(1))));
        if (nombres.Any(item => item.Clave.Length == 0) ||
            nombres.GroupBy(item => item.Clave, StringComparer.Ordinal).Any(grupo => grupo.Count() > 1))
            throw new InvalidOperationException("La base de comunidad contiene nombres duplicados o vacios. No se modificaron los registros; resuelve los conflictos antes de iniciar.");
        comando.CommandText = "UPDATE participantes SET nombre_clave = $clave WHERE id = $id";
        comando.Parameters.Add("$clave", SqliteType.Text);
        comando.Parameters.Add("$id", SqliteType.Text);
        foreach (var nombre in nombres)
        {
            comando.Parameters["$clave"].Value = nombre.Clave;
            comando.Parameters["$id"].Value = nombre.Id;
            comando.ExecuteNonQuery();
        }
        comando.Parameters.Clear();
        comando.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_participantes_nombre_unico ON participantes (nombre_clave)";
        comando.ExecuteNonQuery();
        transaccion.Commit();
    }

    public void Registrar(IEnumerable<ParticipanteComunidad> participantes, OrigenParticipante origen)
    {
        string origenRegistro = origen switch
        {
            OrigenParticipante.Experiencia => "experiencia",
            OrigenParticipante.Prueba => "prueba",
            _ => throw new ArgumentOutOfRangeException(nameof(origen))
        };
        using var conexion = AbrirConexion();
        using var transaccion = conexion.BeginTransaction();
        using var comando = conexion.CreateCommand();
        comando.Transaction = transaccion;
        comando.CommandText = """
            INSERT INTO participantes (id, nombre, nombre_clave, anio, anio_clave, facultad, etapa, etapa_clave, estacion, grupal, formato, rol, efigie, efigie_completa, origen)
            VALUES ($id, $nombre, $nombreClave, $anio, $anioClave, $facultad, $etapa, $etapaClave, $estacion, $grupal, $formato, $rol, $efigie, $efigieCompleta, $origen)
            """;
        foreach (string campo in new[] { "$id", "$nombre", "$nombreClave", "$anio", "$anioClave", "$facultad", "$etapa", "$etapaClave", "$estacion", "$grupal", "$formato", "$rol", "$efigie", "$efigieCompleta", "$origen" })
            comando.Parameters.Add(campo, SqliteType.Text);
        comando.Parameters["$origen"].Value = origenRegistro;

        foreach (ParticipanteComunidad participante in participantes)
        {
            DatosRespuesta datos = participante.Datos;
            if (string.IsNullOrWhiteSpace(datos.id) || string.IsNullOrWhiteSpace(datos.nombre) ||
                string.IsNullOrWhiteSpace(participante.Efigie))
                throw new InvalidOperationException("No se puede registrar un participante sin identificador, nombre o efigie.");

            string nombreClave = NormalizarNombre(datos.nombre);
            string anioClave = Normalizar(datos.anio);
            string etapaClave = Normalizar(datos.etapa);
            if (nombreClave.Length == 0)
                throw new InvalidOperationException("No se puede registrar un nombre vacio.");
            if (EsNombreDePrueba(datos.nombre))
                throw new InvalidOperationException("Los nombres de prueba no se guardan en la comunidad.");
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
            comando.Parameters["$efigieCompleta"].Value = participante.EfigieCompleta ?? "";
            try
            {
                comando.ExecuteNonQuery();
            }
            catch (SqliteException error) when (error.SqliteErrorCode == 19 && NombreExiste(conexion, transaccion, nombreClave))
            {
                throw new NombreOcupadoException();
            }
        }
        transaccion.Commit();
    }

    public ParticipanteComunidad BuscarPorCredenciales(string nombre, string estacion) =>
        LeerParticipantes(nombreClave: NormalizarNombre(nombre)).FirstOrDefault(participante =>
            Coinciden(participante.Datos.estacion, estacion));

    public ParticipanteComunidad BuscarPorId(string id) =>
        LeerParticipantes(id: id).FirstOrDefault();

    public IReadOnlyList<(string Nombre, string Origen)> ListarOrigenes()
    {
        using var conexion = AbrirConexion();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT nombre, origen FROM participantes ORDER BY origen, nombre_clave";
        using var lector = comando.ExecuteReader();
        var participantes = new List<(string Nombre, string Origen)>();
        while (lector.Read())
            participantes.Add((lector.GetString(0), lector.GetString(1)));
        return participantes;
    }

    public bool NombreOcupado(string nombre)
    {
        using var conexion = AbrirConexion();
        return NombreExiste(conexion, null, NormalizarNombre(nombre));
    }

    public void GuardarEfigieCompleta(string id, string svg)
    {
        using var conexion = AbrirConexion();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "UPDATE participantes SET efigie_completa = $svg WHERE id = $id";
        comando.Parameters.AddWithValue("$svg", svg);
        comando.Parameters.AddWithValue("$id", id);
        if (comando.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("No se encontró el participante al guardar su efigie completa.");
    }

    private static bool NombreExiste(SqliteConnection conexion, SqliteTransaction transaccion, string clave)
    {
        using var comando = conexion.CreateCommand();
        comando.Transaction = transaccion;
        comando.CommandText = "SELECT 1 FROM participantes WHERE nombre_clave = $clave LIMIT 1";
        comando.Parameters.AddWithValue("$clave", clave);
        return comando.ExecuteScalar() != null;
    }

    public IReadOnlyList<FiguraCercana> BuscarFigurasCercanas(string id)
    {
        IReadOnlyList<FiguraCercana> figuras = BuscarFigurasCercanas(LeerParticipantes(incluirEfigie: false), id);
        return figuras?.Select(figura =>
            new FiguraCercana(BuscarPorId(figura.Participante.Datos.id)
                ?? throw new InvalidOperationException("No se encontró una figura seleccionada de la comunidad."), figura.Coincidencias))
            .ToList();
    }

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
    public static string NormalizarNombre(string valor) =>
        new string((valor ?? "").Where(caracter => !char.IsWhiteSpace(caracter)).ToArray()).ToUpperInvariant();
    public static bool EsNombreDePrueba(string nombre) =>
        NormalizarNombre(nombre).StartsWith("PRUEBA", StringComparison.Ordinal);

    private SqliteConnection AbrirConexion()
    {
        var conexion = new SqliteConnection(cadenaConexion);
        conexion.Open();
        return conexion;
    }

    private List<ParticipanteComunidad> LeerParticipantes(string id = null, string nombreClave = null, bool incluirEfigie = true)
    {
        using var conexion = AbrirConexion();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT id, nombre, anio, facultad, etapa, estacion, grupal, formato, rol, " +
            (incluirEfigie ? "efigie, efigie_completa" : "'', ''") + " FROM participantes";
        if (id != null)
        {
            comando.CommandText += " WHERE id = $id";
            comando.Parameters.AddWithValue("$id", id);
        }
        else if (nombreClave != null)
        {
            comando.CommandText += " WHERE nombre_clave = $nombreClave";
            comando.Parameters.AddWithValue("$nombreClave", nombreClave);
        }
        comando.CommandText += " ORDER BY orden";
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
            participantes.Add(new ParticipanteComunidad(datos, lector.GetString(9), lector.GetString(10)));
        }
        return participantes;
    }
}
