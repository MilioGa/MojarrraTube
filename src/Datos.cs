using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MojarraTube;

public class Ajustes
{
    public string Tema { get; set; } = "oscuro";             // oscuro | claro | sistema
    public string CarpetaVideos { get; set; } = "";
    public string CarpetaAudio { get; set; } = "";
    public int CalidadReproduccion { get; set; } = 1080;     // alto máximo; 0 = la mejor
    public int CalidadDescarga { get; set; } = 1080;
    public string FormatoAudio { get; set; } = "mp3";        // mp3 | m4a | opus
    public int DescargasSimultaneas { get; set; } = 2;
    public bool DetectarPortapapeles { get; set; } = true;
    public bool RecordarPosicion { get; set; } = true;
    public bool Reproduccion_Auto { get; set; } = true;      // siguiente de la cola
    public bool GuardarHistorial { get; set; } = true;
    public string CookiesNavegador { get; set; } = "";       // "" | edge | chrome | firefox | brave
    public double Volumen { get; set; } = 80;
    public double Velocidad { get; set; } = 1;
    public bool Silencio { get; set; }
    public bool MenuCompacto { get; set; }
    public DateTime UltimaActualizacion { get; set; }
    public List<string> CarpetasBiblioteca { get; set; } = new();
}

public class EntradaHistorial
{
    public Video Video { get; set; } = new();
    public DateTime Visto { get; set; }
    public double Posicion { get; set; }
}

public enum EstadoDescarga { EnCola, Preparando, Descargando, Procesando, Pausada, Terminada, Error, Cancelada }

public class RegistroDescarga
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Url { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Miniatura { get; set; } = "";
    public string Canal { get; set; } = "";
    public double? Duracion { get; set; }
    public string Modo { get; set; } = "video";               // video | audio
    public int Calidad { get; set; }                          // alto máximo (video) o kbps (audio); 0 = la mejor
    public string FormatoAudio { get; set; } = "mp3";
    public string Carpeta { get; set; } = "";
    public string? Archivo { get; set; }
    public EstadoDescarga Estado { get; set; } = EstadoDescarga.EnCola;
    public string? Error { get; set; }
    public long Tamano { get; set; }
    public DateTime Creada { get; set; } = DateTime.Now;
    public bool EsLista { get; set; }
}

/// <summary>Datos guardados en Documentos\MojarraTube (JSON legibles).</summary>
public static class Almacen
{
    public static Ajustes Ajustes { get; private set; } = new();
    public static List<EntradaHistorial> Historial { get; private set; } = new();
    public static List<Canal> Suscripciones { get; private set; } = new();
    public static List<Video> VerDespues { get; private set; } = new();
    public static List<RegistroDescarga> Descargas { get; private set; } = new();
    public static List<string> Busquedas { get; private set; } = new();

    public static event Action? Cambio;

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static string R(string n) => Path.Combine(Marca.DirDatos, n);

    public static void Cargar()
    {
        Ajustes = Leer<Ajustes>("ajustes.json") ?? new();
        Historial = Leer<List<EntradaHistorial>>("historial.json") ?? new();
        Suscripciones = Leer<List<Canal>>("suscripciones.json") ?? new();
        VerDespues = Leer<List<Video>>("ver-despues.json") ?? new();
        Descargas = Leer<List<RegistroDescarga>>("descargas.json") ?? new();
        Busquedas = Leer<List<string>>("busquedas.json") ?? new();
        if (string.IsNullOrEmpty(Ajustes.CarpetaVideos))
            Ajustes.CarpetaVideos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Marca.Nombre);
        if (string.IsNullOrEmpty(Ajustes.CarpetaAudio))
            Ajustes.CarpetaAudio = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Marca.Nombre);
        // Descargas que quedaron a medias al cerrar: se pueden reanudar
        foreach (var d in Descargas.Where(d => d.Estado is EstadoDescarga.Descargando or EstadoDescarga.Preparando or EstadoDescarga.Procesando))
            d.Estado = EstadoDescarga.Pausada;
    }

    static T? Leer<T>(string nombre) where T : class
    {
        try
        {
            var r = R(nombre);
            if (!File.Exists(r)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(r), Json);
        }
        catch (Exception e) { Marca.Registrar(e); return null; }
    }

    static readonly Dictionary<string, Func<object>> Pendientes = new();
    static Timer? _temporizador;
    static readonly object Cerrojo = new();
    static SynchronizationContext? _ui;

    /// <summary>Guarda (agrupando cambios seguidos) y avisa a las vistas. Se llama desde la interfaz.</summary>
    public static void Guardar(params string[] que) => Guardar(true, que);

    /// <summary>Con <paramref name="avisar"/> en false no se refrescan las vistas (p. ej. al guardar la posición cada pocos segundos).</summary>
    public static void Guardar(bool avisar, params string[] que)
    {
        _ui ??= SynchronizationContext.Current;
        lock (Cerrojo)
        {
            foreach (var q in que.Length == 0 ? new[] { "ajustes" } : que)
                Pendientes[q] = q switch
                {
                    "ajustes" => () => Ajustes,
                    "historial" => () => Historial,
                    "suscripciones" => () => Suscripciones,
                    "verdespues" => () => VerDespues,
                    "descargas" => () => Descargas,
                    "busquedas" => () => Busquedas,
                    _ => throw new ArgumentException(q),
                };
            _temporizador ??= new Timer(_ => { if (_ui != null) _ui.Post(_ => Escribir(false), null); else Escribir(true); }, null, Timeout.Infinite, Timeout.Infinite);
            _temporizador.Change(500, Timeout.Infinite);
        }
        if (avisar) Cambio?.Invoke();
    }

    /// <summary>Convierte a JSON en el hilo de la interfaz (las listas solo cambian ahí) y escribe en segundo plano.</summary>
    public static void Escribir(bool esperar = true)
    {
        List<KeyValuePair<string, Func<object>>> lista;
        lock (Cerrojo) { lista = Pendientes.ToList(); Pendientes.Clear(); }
        var textos = new List<(string nombre, string texto)>();
        foreach (var (k, f) in lista)
        {
            try { textos.Add((k == "verdespues" ? "ver-despues.json" : k + ".json", JsonSerializer.Serialize(f(), Json))); }
            catch (Exception e) { Marca.Registrar(e); }
        }
        void Escribe()
        {
            lock (Disco)
                foreach (var (nombre, texto) in textos)
                {
                    try
                    {
                        var tmp = R(nombre + ".tmp");
                        File.WriteAllText(tmp, texto);
                        File.Move(tmp, R(nombre), true);
                    }
                    catch (Exception e) { Marca.Registrar(e); }
                }
        }
        if (esperar) Escribe(); else Task.Run(Escribe);
    }
    static readonly object Disco = new();

    // ── Historial y posiciones ──
    public static void AgregarHistorial(Video v, double posicion = 0)
    {
        if (!Ajustes.GuardarHistorial) return;
        {
            var previa = Historial.FirstOrDefault(h => h.Video.Clave == v.Clave);
            if (previa != null) { Historial.Remove(previa); if (posicion == 0) posicion = previa.Posicion; }
            Historial.Insert(0, new EntradaHistorial { Video = v.Copia(), Visto = DateTime.Now, Posicion = posicion });
            if (Historial.Count > 2000) Historial.RemoveRange(2000, Historial.Count - 2000);
        }
        Guardar("historial");
    }

    public static void GuardarPosicion(Video v, double pos, double duracion)
    {
        if (!Ajustes.RecordarPosicion || !Ajustes.GuardarHistorial) return;
        var h = Historial.FirstOrDefault(x => x.Video.Clave == v.Clave);
        if (h == null) return;
        // Al final (o casi al principio) se olvida la posición
        h.Posicion = pos < 15 || (duracion > 0 && pos > duracion - 20) ? 0 : pos;
        if (duracion > 0) h.Video.Duracion ??= duracion;
        Guardar(false, "historial");
    }

    public static double Posicion(Video v) => Historial.FirstOrDefault(x => x.Video.Clave == v.Clave)?.Posicion ?? 0;

    /// <summary>Marca en cada video cuánto se ha visto (para la barrita de progreso de las miniaturas).</summary>
    public static void MarcarProgreso(IEnumerable<Video> videos)
    {
        var mapa = new Dictionary<string, EntradaHistorial>();
        foreach (var h in Historial) mapa.TryAdd(h.Video.Clave, h);
        foreach (var v in videos)
            if (mapa.TryGetValue(v.Clave, out var h) && h.Posicion > 0 && (v.Duracion ?? h.Video.Duracion) is > 0 and var d)
                v.Progreso = Math.Clamp(h.Posicion / d, 0, 1);
    }

    public static void AgregarBusqueda(string q)
    {
        q = q.Trim();
        if (q.Length == 0 || !Ajustes.GuardarHistorial) return;
        Busquedas.RemoveAll(b => b.Equals(q, StringComparison.OrdinalIgnoreCase));
        Busquedas.Insert(0, q);
        if (Busquedas.Count > 30) Busquedas.RemoveRange(30, Busquedas.Count - 30);
        Guardar("busquedas");
    }

    // ── Suscripciones y ver después ──
    public static bool Suscrito(string canalId) => !string.IsNullOrEmpty(canalId) && Suscripciones.Any(s => s.Id == canalId);

    public static bool AlternarSuscripcion(Canal c)
    {
        var s = Suscripciones.FirstOrDefault(x => x.Id == c.Id);
        if (s != null) Suscripciones.Remove(s); else Suscripciones.Add(c);
        Guardar("suscripciones");
        return s == null;
    }

    public static bool EnVerDespues(Video v) => VerDespues.Any(x => x.Clave == v.Clave);

    public static bool AlternarVerDespues(Video v)
    {
        var e = VerDespues.FirstOrDefault(x => x.Clave == v.Clave);
        if (e != null) VerDespues.Remove(e); else VerDespues.Insert(0, v.Copia());
        Guardar("verdespues");
        return e == null;
    }
}
