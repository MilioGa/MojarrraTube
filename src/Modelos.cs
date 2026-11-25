using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MojarraTube;

public abstract class Notificador : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Avisar([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    protected bool Poner<T>(ref T campo, T valor, [CallerMemberName] string? p = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor)) return false;
        campo = valor; Avisar(p); return true;
    }
}

public enum TipoItem { Video, Canal, Lista }

/// <summary>Un video (de YouTube, de otra página o un archivo local) tal como se muestra en una tarjeta.</summary>
public class Video : Notificador
{
    public string Id { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Canal { get; set; } = "";
    public string CanalId { get; set; } = "";
    public string Url { get; set; } = "";
    public string Miniatura { get; set; } = "";
    public double? Duracion { get; set; }
    public long? Vistas { get; set; }
    public DateTime? Fecha { get; set; }
    public bool EnVivo { get; set; }
    public TipoItem Tipo { get; set; } = TipoItem.Video;
    /// <summary>Ruta del archivo si es un video local (descargado o abierto desde el equipo).</summary>
    public string? Ruta { get; set; }
    public bool SoloAudio { get; set; }

    double _progreso;
    /// <summary>Cuánto se ha visto (0–1), para la barrita roja de la miniatura.</summary>
    [JsonIgnore] public double Progreso { get => _progreso; set { if (Poner(ref _progreso, value)) Avisar(nameof(HayProgreso)); } }
    [JsonIgnore] public bool HayProgreso => _progreso > 0.01;

    [JsonIgnore] public bool EsLocal => !string.IsNullOrEmpty(Ruta);
    [JsonIgnore] public bool EsYouTube => Texto.EsYouTube(Url) || (!EsLocal && Id.Length == 11 && string.IsNullOrEmpty(Url));
    [JsonIgnore] public string Clave => EsLocal ? "file:" + Ruta : (!string.IsNullOrEmpty(Id) ? Id : Url);
    [JsonIgnore] public string DuracionTexto => EnVivo ? "EN VIVO" : Duracion is > 0 ? Texto.Tiempo(Duracion.Value) : "";
    [JsonIgnore] public bool HayDuracion => EnVivo || Duracion is > 0;
    [JsonIgnore] public string Meta
    {
        get
        {
            var partes = new List<string>();
            if (Vistas is { } v) partes.Add(Texto.Vistas(v));
            if (Fecha is { } f) partes.Add(Texto.Hace(f));
            return string.Join("  •  ", partes);
        }
    }
    [JsonIgnore] public string Inicial => string.IsNullOrWhiteSpace(Canal) ? "?" : Canal.Trim()[0].ToString().ToUpperInvariant();
    [JsonIgnore] public bool EsCanal => Tipo == TipoItem.Canal;
    [JsonIgnore] public bool EsLista => Tipo == TipoItem.Lista;
    [JsonIgnore] public string UrlCompleta => !string.IsNullOrEmpty(Url) ? Url : (Id.Length == 11 ? "https://www.youtube.com/watch?v=" + Id : Id);

    public Video Copia() => (Video)MemberwiseClone();
}

public class Canal
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Avatar { get; set; } = "";
    public string Url { get; set; } = "";
    [JsonIgnore] public string Inicial => string.IsNullOrWhiteSpace(Nombre) ? "?" : Nombre.Trim()[0].ToString().ToUpperInvariant();
    [JsonIgnore] public string UrlCompleta => !string.IsNullOrEmpty(Url) ? Url : "https://www.youtube.com/channel/" + Id;
}

/// <summary>Un formato disponible de un video (salida de yt-dlp).</summary>
public class Formato
{
    public string Id { get; set; } = "";
    public string Ext { get; set; } = "";
    public string VCodec { get; set; } = "";
    public string ACodec { get; set; } = "";
    public int Alto { get; set; }
    public int Ancho { get; set; }
    public double Fps { get; set; }
    public double Tbr { get; set; }
    public double Abr { get; set; }
    public long Tamano { get; set; }
    public string Protocolo { get; set; } = "";
    public string Url { get; set; } = "";
    public string Nota { get; set; } = "";
    public string Idioma { get; set; } = "";
    public Dictionary<string, string> Cabeceras { get; set; } = new();

    /// <summary>"" = desconocido (sitios genéricos): se asume que puede tener video y audio.</summary>
    public bool TieneVideo => VCodec != "none" && Ext != "mhtml" && !(VCodec == "" && ACodec != "" && ACodec != "none" && Alto == 0);
    public bool TieneAudio => ACodec != "none";
    public bool EsDirecto => Protocolo is "https" or "http";
    public bool EsHls => Protocolo.StartsWith("m3u8");
}

public class Capitulo
{
    public double Inicio { get; set; }
    public double Fin { get; set; }
    public string Titulo { get; set; } = "";
    public string InicioTexto => Texto.Tiempo(Inicio);
}

/// <summary>Toda la información de un video para reproducirlo o descargarlo.</summary>
public class InfoVideo
{
    public Video Video { get; set; } = new();
    public string Descripcion { get; set; } = "";
    public long? Seguidores { get; set; }
    public long? MeGusta { get; set; }
    public List<Formato> Formatos { get; set; } = new();
    public List<Capitulo> Capitulos { get; set; } = new();
    public string Extractor { get; set; } = "";
    /// <summary>Si el enlace es una lista o canal: sus videos.</summary>
    public List<Video> Entradas { get; set; } = new();
    public bool EsLista { get; set; }
    public string TituloLista { get; set; } = "";
    public string? UrlDirecta { get; set; }
}

public class Pagina
{
    public List<Video> Videos { get; set; } = new();
    public string Titulo { get; set; } = "";
    public string Avatar { get; set; } = "";
    public string Banner { get; set; } = "";
    public string CanalId { get; set; } = "";
    public long? Seguidores { get; set; }
    public string Descripcion { get; set; } = "";
}
