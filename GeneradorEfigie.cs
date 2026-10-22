using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Rhino.Geometry;

public class ConfigEfigie
{
    public double RadioBaseTexto { get; set; } = 34.0;
    public double RadioBaseAnio { get; set; } = 27.0;
    public double RadioBaseEstaciones { get; set; } = 14.0;
    public double EscalaCopia { get; set; } = 50.0;
    public int CantidadRayos { get; set; } = 15;
    public int OffsetGrupal { get; set; } = 4;
    public double RadioBaseCarrera { get; set; } = 36.79;
    public double EscalaAnilloMinimo { get; set; } = 75.0;
    public double RadioBaseRol { get; set; } = 25.0;
    public double RadioBaseHistoria { get; set; } = 21.85;
    public double MultiplicadorDistancia { get; set; } = 0.80;
    public double TamanoIcono { get; set; } = 1.70;
    public double EscalaGlobal { get; set; } = 100.0;
}

public class DatosRespuesta
{
    public string id { get; set; } = "";
    public string nombre { get; set; } = "";
    public string anio { get; set; } = "2000";
    public string facultad { get; set; } = "";
    public string etapa { get; set; } = "";
    public string estacion { get; set; } = "";
    public string grupal { get; set; } = "";
    public string formato { get; set; } = "";
    public string rol { get; set; } = "";
    [JsonIgnore]
    public string deviceId { get; set; } = "";
}

public static class GeneradorEfigie
{
    public static string GenerarSVG(DatosRespuesta datos, ConfigEfigie cfg, bool incluirMarcoYNombre = true)
    {
        int anioNum = 2000;
        int.TryParse(new string((datos.anio ?? "").Where(char.IsDigit).ToArray()), out anioNum);

        int etapa = MapearEtapa(datos.etapa ?? "");
        int estacion = MapearEstacion(datos.estacion ?? "");
        bool esGrupal = (datos.grupal ?? "").ToLower().Contains("grupal");
        int formato = MapearFormato(datos.formato ?? "");
        int rol = MapearRol(datos.rol ?? "");

        Point3d centro = new Point3d(0, 0, 0);

        // 1. Llamadas a tus módulos geométricos exactos de RhinoCommon
        GenerarMarco(centro, datos.nombre ?? "", cfg.RadioBaseTexto > 0 ? cfg.RadioBaseTexto : 15.0, out Curve marco, out Point3d ptTxt, out string txtSalida);
        Curve anioCrv = GenerarAnio(anioNum, centro, cfg.RadioBaseAnio > 0 ? cfg.RadioBaseAnio : 17.0);
        List<Curve> cEst = GenerarEstaciones(centro, estacion, cfg.RadioBaseEstaciones > 0 ? cfg.RadioBaseEstaciones : 15.0, cfg.EscalaCopia);

        List<Line> conexiones = new List<Line>();
        if (cEst.Count >= 2)
            conexiones = GenerarConexiones(centro, cEst, esGrupal, cfg.CantidadRayos, cfg.OffsetGrupal);

        List<Curve> cCarr = GenerarCarrera(centro, etapa, cfg.RadioBaseCarrera > 0 ? cfg.RadioBaseCarrera : 45.0, cfg.EscalaAnilloMinimo);
        Curve cRol = GenerarRol(centro, rol, cfg.RadioBaseRol > 0 ? cfg.RadioBaseRol : 18.0);

        List<Curve> cHist = new List<Curve>();
        if (cEst.Count >= 1)
            cHist = GenerarHistoria(centro, cEst, formato, cfg.RadioBaseHistoria > 0 ? cfg.RadioBaseHistoria : 24.85, cfg.MultiplicadorDistancia, cfg.TamanoIcono);

        // 2. Aplicar Escala Global
        double scaleFactor = (cfg.EscalaGlobal > 0 && cfg.EscalaGlobal <= 100) ? (cfg.EscalaGlobal / 100.0) : 1.0;
        Transform xform = Transform.Scale(centro, scaleFactor);

        if (anioCrv != null) anioCrv.Transform(xform);
        foreach (Curve c in cEst) c.Transform(xform);
        for (int k = 0; k < conexiones.Count; k++) {
            Line l = conexiones[k];
            l.Transform(xform);
            conexiones[k] = l;
        }
        foreach (Curve c in cCarr) c.Transform(xform);
        if (cRol != null) cRol.Transform(xform);
        foreach (Curve c in cHist) c.Transform(xform);

        // 3. Empaquetar curvas de Rhino a SVG
        List<Curve> todasLasCurvas = new List<Curve>();
        if (anioCrv != null) todasLasCurvas.Add(anioCrv);
        todasLasCurvas.AddRange(cEst);
        todasLasCurvas.AddRange(cCarr);
        if (cRol != null) todasLasCurvas.Add(cRol);
        todasLasCurvas.AddRange(cHist);

        return ExportarASvg(marco, ptTxt, txtSalida, datos.facultad, anioCrv, cEst, cCarr, cRol, cHist, conexiones, incluirMarcoYNombre);
    }

    private static string ExportarASvg(
        Curve marco,
        Point3d ptTxt,
        string texto,
        string facultad,
        Curve anio,
        List<Curve> estaciones,
        List<Curve> carrera,
        Curve rol,
        List<Curve> historia,
        List<Line> conexiones,
        bool incluirMarcoYNombre)
    {
        var ic = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        // ViewBox centrado en (0,0) con tamaño suficiente para cubrir RadioBaseCarrera (45) + margen
        sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"-75 -75 150 150\" width=\"100%\" height=\"100%\" style=\"background:#ffffff;\">");
        sb.AppendLine("<g fill=\"none\" stroke=\"#000000\" stroke-width=\"0.4\" stroke-linecap=\"round\" stroke-linejoin=\"round\">");

        // En Rhino +Y va hacia arriba; en SVG +Y va hacia abajo, por eso invertimos Y (-p.Y)
        if (incluirMarcoYNombre && marco != null)
            sb.AppendLine($"  <path d=\"{CurvaAPathSvg(marco)}\" stroke-width=\"0.3\" />");

        AgregarGrupoCurvas(sb, "anio", anio == null ? new List<Curve>() : new List<Curve> { anio });
        AgregarGrupoCurvas(sb, "estaciones", estaciones);
        AgregarGrupoCurvas(sb, "carrera", carrera, ObtenerColorFacultad(facultad));
        AgregarGrupoCurvas(sb, "rol", rol == null ? new List<Curve>() : new List<Curve> { rol });
        AgregarGrupoCurvas(sb, "historia", historia);

        sb.AppendLine("  <g data-capa=\"conexiones\">");
        foreach (var l in conexiones)
        {
            sb.AppendLine(string.Format(ic, "    <line x1=\"{0:F3}\" y1=\"{1:F3}\" x2=\"{2:F3}\" y2=\"{3:F3}\" />",
                l.FromX, -l.FromY, l.ToX, -l.ToY));
        }
        sb.AppendLine("  </g>");

        sb.AppendLine("</g>");
        if (incluirMarcoYNombre && !string.IsNullOrEmpty(texto))
        {
            sb.AppendLine(string.Format(ic, "<text x=\"{0:F3}\" y=\"{1:F3}\" font-family=\"sans-serif\" font-size=\"3\" fill=\"#000000\">{2}</text>",
                ptTxt.X, -ptTxt.Y, System.Net.WebUtility.HtmlEncode(texto)));
        }
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static void AgregarGrupoCurvas(StringBuilder sb, string nombre, IEnumerable<Curve> curvas, string stroke = null)
    {
        string atributoStroke = stroke == null ? "" : $" stroke=\"{stroke}\"";
        sb.AppendLine($"  <g data-capa=\"{nombre}\"{atributoStroke}>");
        foreach (var curva in curvas)
        {
            if (curva != null)
                sb.AppendLine($"    <path d=\"{CurvaAPathSvg(curva)}\" />");
        }
        sb.AppendLine("  </g>");
    }

    private static string ObtenerColorFacultad(string facultad)
    {
        return facultad switch
        {
            "Ciencias Económicas" => "#6064A3",
            "Derecho" => "#7C171F",
            "Filosofía y Letras" => "#459BDA",
            "Ingeniería y Ciencias Agrarias" => "#84B444",
            "Psicología y Psicopedagogía" => "#CE2D7B",
            "Música" => "#F9DB52",
            "Ciencias Médicas" => "#6EB392",
            "Ciencias Sociales" => "#E27646",
            "Teología" => "#85A8C8",
            "Staff / Docentes" => "#133463",
            "Arquitectura y Diseño" => "#8B1C31",
            _ => "#000000"
        };
    }

    private static string CurvaAPathSvg(Curve crv)
    {
        var ic = CultureInfo.InvariantCulture;
        // Convertimos cualquier NurbsCurve/Arc/Circle de Rhino en una polilínea de alta precisión para SVG
        PolylineCurve plc = crv.ToPolyline(0.05, 0.05, 0.01, 0);
        if (plc != null && plc.TryGetPolyline(out Polyline pl) && pl.Count > 0)
        {
            var sb = new StringBuilder();
            sb.Append(string.Format(ic, "M {0:F3} {1:F3}", pl[0].X, -pl[0].Y));
            for (int i = 1; i < pl.Count; i++)
                sb.Append(string.Format(ic, " L {0:F3} {1:F3}", pl[i].X, -pl[i].Y));
            return sb.ToString();
        }
        return "";
    }

    // --- TUS MÉTODOS AUXILIARES Y GEOMÉTRICOS ORIGINALES ---
    static int MapearEtapa(string txt) {
        if (txt.Contains("1")) return 1; if (txt.Contains("2")) return 2;
        if (txt.Contains("3")) return 3; if (txt.Contains("4")) return 4;
        if (txt.Contains("5")) return 5; if (txt.Contains("6")) return 6;
        if (txt.Contains("No Docente")) return 8;
        if (txt.Contains("Docente")) return 7;
        return 1;
    }

    static int MapearEstacion(string txt) {
        string t = txt.ToLower();
        if (t.Contains("primavera")) return 1; if (t.Contains("verano")) return 2;
        if (t.Contains("otoño")) return 3; if (t.Contains("invierno")) return 4;
        return 1;
    }

    static int MapearFormato(string txt) {
        string t = txt.ToLower();
        if (t.Contains("película") || t.Contains("pelicula")) return 1;
        if (t.Contains("serie")) return 2; if (t.Contains("libro")) return 3;
        if (t.Contains("videojuego")) return 4; if (t.Contains("teatro")) return 5;
        return 1;
    }

    static int MapearRol(string txt) {
        string t = txt.ToLower();
        if (t.Contains("proponer")) return 1; if (t.Contains("organizar")) return 2;
        if (t.Contains("materializar")) return 3; if (t.Contains("mejorar")) return 4;
        return 1;
    }

    static void GenerarMarco(Point3d Centro, string Texto, double RadioBase, out Curve Marco, out Point3d PuntoTexto, out string TextoSalida) {
        double margen = RadioBase * 1.3;
        Point3d p0 = new Point3d(Centro.X - margen, Centro.Y - margen, Centro.Z);
        Point3d p1 = new Point3d(Centro.X + margen, Centro.Y - margen, Centro.Z);
        Point3d p2 = new Point3d(Centro.X + margen, Centro.Y + margen, Centro.Z);
        Point3d p3 = new Point3d(Centro.X - margen, Centro.Y + margen, Centro.Z);
        Marco = new Polyline(new Point3d[] { p0, p1, p2, p3, p0 }).ToNurbsCurve();
        PuntoTexto = new Point3d(p0.X + (RadioBase * 0.1), p0.Y + (RadioBase * 0.1), p0.Z);
        TextoSalida = Texto;
    }

    static Curve GenerarAnio(int Anio, Point3d Centro, double RadioBase) {
        int N = Anio % 100;
        if (Anio < 2000) {
            List<Curve> petalos = new List<Curve>(); double offsetAngulo = -Math.PI / 2.0;
            List<Point3d> ptsBase = new List<Point3d>();
            for(int i = 0; i < N; i++) ptsBase.Add(new Point3d(Centro.X + RadioBase * Math.Cos(offsetAngulo + (2.0 * Math.PI * i / N)), Centro.Y + RadioBase * Math.Sin(offsetAngulo + (2.0 * Math.PI * i / N)), Centro.Z));
            for (int i = 0; i < N; i++) {
                Point3d A = ptsBase[i]; Point3d B = ptsBase[(i + 1) % N];
                Point3d M = new Point3d((A.X + B.X) / 2.0, (A.Y + B.Y) / 2.0, (A.Z + B.Z) / 2.0);
                Vector3d dirOutward = M - Centro; dirOutward.Unitize();
                petalos.Add(new Arc(A, M + (dirOutward * (A.DistanceTo(B) / 2.0)), B).ToNurbsCurve());
            }
            Curve[] joined = Curve.JoinCurves(petalos); return (joined != null && joined.Length > 0) ? joined[0] : null;
        } else {
            if (N == 0) return new Circle(Centro, RadioBase).ToNurbsCurve();
            else if (N == 1) {
                List<Point3d> pts = new List<Point3d>(); double anguloInicio = -Math.PI / 2.0 - Math.PI / 4.0; double anguloFin = -Math.PI / 2.0 + 5.0 * Math.PI / 4.0;
                for (int i = 0; i <= 50; i++) { double t = anguloInicio + (anguloFin - anguloInicio) * (i / 50.0); pts.Add(new Point3d(Centro.X + RadioBase * Math.Cos(t), Centro.Y + RadioBase * Math.Sin(t), Centro.Z)); }
                pts.Add(pts[0]); return new Polyline(pts).ToNurbsCurve();
            } else if (N == 2) {
                List<Point3d> pts = new List<Point3d>();
                for (int i = 0; i <= 25; i++) pts.Add(new Point3d(Centro.X + RadioBase * Math.Cos(-Math.PI / 4.0 + (Math.PI / 2.0) * (i / 25.0)), Centro.Y + RadioBase * Math.Sin(-Math.PI / 4.0 + (Math.PI / 2.0) * (i / 25.0)), Centro.Z));
                for (int i = 0; i <= 25; i++) pts.Add(new Point3d(Centro.X + RadioBase * Math.Cos(3.0 * Math.PI / 4.0 + (Math.PI / 2.0) * (i / 25.0)), Centro.Y + RadioBase * Math.Sin(3.0 * Math.PI / 4.0 + (Math.PI / 2.0) * (i / 25.0)), Centro.Z));
                pts.Add(pts[0]); return new Polyline(pts).ToNurbsCurve();
            } else {
                List<Point3d> pts = new List<Point3d>();
                for(int i = 0; i <= N; i++) pts.Add(new Point3d(Centro.X + RadioBase * Math.Cos(-Math.PI / 2.0 + (2.0 * Math.PI * i / N)), Centro.Y + RadioBase * Math.Sin(-Math.PI / 2.0 + (2.0 * Math.PI * i / N)), Centro.Z));
                return new Polyline(pts).ToNurbsCurve();
            }
        }
    }

    static List<Curve> GenerarEstaciones(Point3d Centro, int Estacion, double RadioBase, double EscalaCopia) {
        double activacion = 1.0; int subdivisiones = 20; int grado = 1;
        if(Estacion==1){ activacion = 0.15; subdivisiones = 18; grado = 2; }
        else if(Estacion==2){ activacion = 1.0; subdivisiones = 20; grado = 3; }
        else if(Estacion==3){ activacion = 0.5; subdivisiones = 10; grado = 1; }
        else if(Estacion==4){ activacion = 0.33; subdivisiones = 16; grado = 1; }

        List<Point3d> pts = new List<Point3d>(); double distanciaMovimiento = RadioBase * activacion;
        double rotacion = -Math.PI / 2.0;
        for (int i = 0; i < subdivisiones; i++) {
            double radioActual = (i % 2 == 0) ? (RadioBase + distanciaMovimiento) : RadioBase;
            pts.Add(new Point3d(Centro.X + radioActual * Math.Cos(rotacion + 2.0 * Math.PI * i / subdivisiones), Centro.Y + radioActual * Math.Sin(rotacion + 2.0 * Math.PI * i / subdivisiones), Centro.Z));
        }
        pts.Add(pts[0]); Curve curvaFinal = (grado == 1) ? new PolylineCurve(pts) : Curve.CreateInterpolatedCurve(pts, grado, CurveKnotStyle.UniformPeriodic);
        List<Curve> resultados = new List<Curve>(){ curvaFinal };
        if (EscalaCopia > 0.001) { Curve curvaCopia = curvaFinal.DuplicateCurve(); curvaCopia.Transform(Transform.Scale(Centro, EscalaCopia / 100.0)); resultados.Add(curvaCopia); }
        return resultados;
    }

    static List<Line> GenerarConexiones(Point3d Centro, List<Curve> CurvasEstaciones, bool Grupal, int CantidadRayos, int OffsetGrupal) {
        Curve cExt = CurvasEstaciones[0]; Curve cInt = CurvasEstaciones[1];
        double maxRadius = cExt.GetBoundingBox(true).Diagonal.Length + 10.0;
        List<Point3d> ptsExt = new List<Point3d>(), ptsInt = new List<Point3d>();
        for (int i = 0; i < CantidadRayos; i++) {
            Vector3d dir = new Vector3d(Math.Cos(-Math.PI/2.0 + 2.0 * Math.PI * i / CantidadRayos), Math.Sin(-Math.PI/2.0 + 2.0 * Math.PI * i / CantidadRayos), 0);
            LineCurve ray = new LineCurve(Centro, Centro + dir * maxRadius);
            var xExt = Rhino.Geometry.Intersect.Intersection.CurveCurve(cExt, ray, 0.001, 0.001);
            ptsExt.Add((xExt != null && xExt.Count > 0) ? xExt[0].PointA : cExt.PointAtStart);
            var xInt = Rhino.Geometry.Intersect.Intersection.CurveCurve(cInt, ray, 0.001, 0.001);
            ptsInt.Add((xInt != null && xInt.Count > 0) ? xInt[0].PointA : cInt.PointAtStart);
        }
        List<Line> lineas = new List<Line>();
        for (int i = 0; i < ptsExt.Count; i++) {
            if (!Grupal) lineas.Add(new Line(ptsInt[i], ptsExt[i]));
            else lineas.Add(new Line(ptsInt[i], ptsExt[(i + (OffsetGrupal % ptsExt.Count == 0 ? 1 : OffsetGrupal % ptsExt.Count)) % ptsExt.Count]));
        }
        return lineas;
    }

    static List<Curve> GenerarCarrera(Point3d Centro, int Etapa, double RadioBase, double EscalaAnilloMinimo) {
        List<Curve> curvasSalida = new List<Curve>();
        if (Etapa >= 1 && Etapa <= 6) {
            double radioMinimo = RadioBase * (EscalaAnilloMinimo / 100.0);
            if (Etapa == 1) curvasSalida.Add(new Circle(Centro, RadioBase).ToNurbsCurve());
            else {
                double espaciado = (RadioBase - radioMinimo) / (Etapa - 1);
                for (int i = 0; i < Etapa; i++) curvasSalida.Add(new Circle(Centro, Math.Max(0.1, RadioBase - (i * espaciado))).ToNurbsCurve());
            }
        } else if (Etapa == 7 || Etapa == 8) {
            double amplitud = RadioBase * 0.15; List<Point3d> pts = new List<Point3d>();
            double rotacion = -Math.PI / 2.0;
            for (int i = 0; i < 36; i++) {
                double radioActual = RadioBase + (i % 2 == 0 ? amplitud : (Etapa == 7 ? -(amplitud * 0.5) : 0));
                pts.Add(new Point3d(Centro.X + radioActual * Math.Cos(rotacion + 2.0 * Math.PI * i / 36), Centro.Y + radioActual * Math.Sin(rotacion + 2.0 * Math.PI * i / 36), Centro.Z));
            }
            pts.Add(pts[0]);
            if (Etapa == 8) curvasSalida.Add(new Polyline(pts).ToNurbsCurve());
            else { Curve onditas = Curve.CreateInterpolatedCurve(pts, 3, CurveKnotStyle.UniformPeriodic); curvasSalida.Add(onditas != null ? onditas : new Polyline(pts).ToNurbsCurve()); }
        }
        return curvasSalida;
    }

    static Curve GenerarRol(Point3d Centro, int Rol, double RadioBase) {
        double radioIcono = RadioBase * 0.20; List<Point3d> pts = new List<Point3d>();
        double rot = -Math.PI / 2.0;
        if (Rol == 1) for (int i = 0; i <= 100; i++) pts.Add(new Point3d(Centro.X + radioIcono * Math.Pow(Math.Cos(rot + 2.0 * Math.PI * i / 100.0), 3), Centro.Y + radioIcono * Math.Pow(Math.Sin(rot + 2.0 * Math.PI * i / 100.0), 3), Centro.Z));
        else if (Rol == 2) for (int i = 0; i <= 6; i++) pts.Add(new Point3d(Centro.X + radioIcono * Math.Cos(rot + Math.PI / 6.0 + (2.0 * Math.PI * i / 6.0)), Centro.Y + radioIcono * Math.Sin(rot + Math.PI / 6.0 + (2.0 * Math.PI * i / 6.0)), Centro.Z));
        else if (Rol == 3) for (int i = 0; i <= 3; i++) pts.Add(new Point3d(Centro.X + radioIcono * Math.Cos(rot + (2.0 * Math.PI * i / 3.0)), Centro.Y + radioIcono * Math.Sin(rot + (2.0 * Math.PI * i / 3.0)), Centro.Z));
        else if (Rol == 4) for (int i = 0; i <= 100; i++) pts.Add(new Point3d(Centro.X + radioIcono * Math.Cos(2.0 * (2.0 * Math.PI * i / 100.0)) * Math.Cos(rot + 2.0 * Math.PI * i / 100.0), Centro.Y + radioIcono * Math.Cos(2.0 * (2.0 * Math.PI * i / 100.0)) * Math.Sin(rot + 2.0 * Math.PI * i / 100.0), Centro.Z));
        return new Polyline(pts).ToNurbsCurve();
    }

    static List<Curve> GenerarHistoria(Point3d Centro, List<Curve> CurvasEstaciones, int Formato, double RadioBase, double MultiplicadorDistancia, double TamanoIcono) {
        Curve cMayor = CurvasEstaciones.OrderByDescending(c => c.GetBoundingBox(true).Diagonal.Length).First();
        int res = 200;
        cMayor.DivideByCount(res, true, out Point3d[] pts); List<Vector3d> direcciones = new List<Vector3d>();
        if (pts != null && pts.Length > 0) {
            double[] dists = pts.Select(p => p.DistanceTo(Centro)).ToArray();
            for (int i = 0; i < pts.Length; i++) {
                if (dists[i] <= dists[(i - 1 + res) % res] && dists[i] <= dists[(i - 2 + res) % res] && dists[i] <= dists[(i + 1) % res] && dists[i] <= dists[(i + 2) % res] && (dists[i] < dists[(i - 2 + res) % res] || dists[i] < dists[(i + 2) % res])) {
                    Vector3d dir = pts[i] - Centro; dir.Unitize();
                    if (!direcciones.Any(v => Vector3d.VectorAngle(v, dir) < 0.1)) direcciones.Add(dir);
                }
            }
        }
        if (direcciones.Count == 0) for(int i = 0; i < 8; i++) direcciones.Add(new Vector3d(Math.Cos(-Math.PI/2.0 + 2.0 * Math.PI * i / 8.0), Math.Sin(-Math.PI/2.0 + 2.0 * Math.PI * i / 8.0), 0));

        List<Curve> iconosSalida = new List<Curve>(); double distanciaFinal = RadioBase * MultiplicadorDistancia;
        foreach (Vector3d dir in direcciones) {
            Vector3d perp = new Vector3d(-dir.Y, dir.X, 0); Point3d a = Centro + (dir * distanciaFinal);
            if (Formato == 1) {
                iconosSalida.Add(new Polyline(new[]{a, a+perp*TamanoIcono+dir*(TamanoIcono*.6), a+perp*TamanoIcono-dir*(TamanoIcono*.6), a}).ToNurbsCurve());
                iconosSalida.Add(new Polyline(new[]{a, a-perp*TamanoIcono+dir*(TamanoIcono*.6), a-perp*TamanoIcono-dir*(TamanoIcono*.6), a}).ToNurbsCurve());
            } else if (Formato == 2) {
                Point3d c1 = a + perp * (TamanoIcono * .4), c2 = a - perp * (TamanoIcono * .4);
                iconosSalida.Add(new Polyline(new[]{c1+dir*TamanoIcono, c1+perp*(TamanoIcono*.6), c1-dir*TamanoIcono, c1-perp*(TamanoIcono*.6), c1+dir*TamanoIcono}).ToNurbsCurve());
                iconosSalida.Add(new Polyline(new[]{c2+dir*TamanoIcono, c2+perp*(TamanoIcono*.6), c2-dir*TamanoIcono, c2-perp*(TamanoIcono*.6), c2+dir*TamanoIcono}).ToNurbsCurve());
            } else if (Formato == 3) {
                iconosSalida.Add(new Polyline(new[]{a+dir*TamanoIcono+perp*TamanoIcono, a-dir*(TamanoIcono*.5), a+dir*TamanoIcono-perp*TamanoIcono}).ToNurbsCurve());
            } else if (Formato == 4) {
                iconosSalida.Add(new LineCurve(a+dir*TamanoIcono, a-dir*TamanoIcono));
                iconosSalida.Add(new LineCurve(a+perp*TamanoIcono, a-perp*TamanoIcono));
            } else if (Formato == 5) {
                Point3d stage = a + dir * (TamanoIcono * .5);
                iconosSalida.Add(new LineCurve(stage+perp*TamanoIcono, stage-perp*TamanoIcono));
                List<Point3d> arc1 = new List<Point3d>(), arc2 = new List<Point3d>();
                for(int j = 0; j <= 12; j++) {
                    Vector3d arcDir = -dir * Math.Sin(Math.PI * j / 12) + perp * Math.Cos(Math.PI * j / 12);
                    arc1.Add(stage + arcDir * TamanoIcono); arc2.Add(stage + arcDir * (TamanoIcono * .5));
                }
                iconosSalida.Add(new Polyline(arc1).ToNurbsCurve());
                iconosSalida.Add(new Polyline(arc2).ToNurbsCurve());
            }
        }
        return iconosSalida;
    }
}