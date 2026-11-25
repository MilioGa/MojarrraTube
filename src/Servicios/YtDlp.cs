using System.Text.Json;
using System.Xml.Linq;

namespace MojarraTube;

public class ErrorYtDlp(string mensaje, string detalle) : Exception(mensaje)
{
    public string Detalle { get; } = detalle;
}

/// <summary>Búsquedas, canales e información de videos con yt-dlp; sugerencias y suscripciones por la web.</summary>
public static class YtDlp
{
    static async Task<string> Json(IEnumerable<string> args, CancellationToken ct, int segundos = 60)
    {
        await Herramientas.Instancia.EsperarYtDlp(ct);
        var todos = YtDlpDatos.ArgsComunes(Almacen.Ajustes).Concat(args).ToList();
        ResultadoProceso r;
        try { r = await Proceso.Ejecutar(Herramientas.YtDlp, todos, TimeSpan.FromSeconds(segundos), ct); }
        catch (TimeoutException) { throw new ErrorYtDlp("La página tardó demasiado en responder. Vuelve a intentarlo.", ""); }
        var salida = r.Salida.Trim();
        // Con --ignore-errors puede haber JSON aunque haya errores
        var inicio = salida.IndexOf('{');
        if (inicio >= 0) return salida[inicio..];
        throw new ErrorYtDlp(YtDlpDatos.Explicar(r.Errores), r.Errores);
    }

    public enum Orden { Relevancia, Recientes, Vistas }

    public static async Task<List<Video>> Buscar(string consulta, int cantidad, Orden orden, CancellationToken ct)
    {
        var url = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(consulta) + orden switch
        {
            Orden.Recientes => "&sp=CAI%253D",
            Orden.Vistas => "&sp=CAM%253D",
            _ => "",
        };
        var json = await Json(new[] { "--flat-playlist", "-J", "--playlist-end", cantidad.ToString(), url }, ct);
        var p = YtDlpDatos.LeerPagina(json);
        return p.Videos;
    }

    public static async Task<Pagina> Canal(string idOUrl, int cantidad, CancellationToken ct)
    {
        var url = idOUrl.StartsWith("http") ? idOUrl.TrimEnd('/') : "https://www.youtube.com/channel/" + idOUrl;
        var esCanalYt = Texto.EsYouTube(url) && !url.Contains("list=") && (url.Contains("/channel/") || url.Contains("/@") || url.Contains("/c/") || url.Contains("/user/"));
        if (esCanalYt && !url.EndsWith("/videos")) url += "/videos";
        var json = await Json(new[] { "--flat-playlist", "-J", "--playlist-end", cantidad.ToString(), url }, ct);
        var p = YtDlpDatos.LeerPagina(json);
        if (p.Titulo.EndsWith(" - Videos")) p.Titulo = p.Titulo[..^9];
        return p;
    }

    /// <summary>Información completa de un video (formatos, capítulos…). Con <paramref name="lista"/> devuelve los videos de una lista.</summary>
    public static async Task<InfoVideo> Info(string url, CancellationToken ct, bool lista = false)
    {
        var args = new List<string> { "-J" };
        if (lista) { args.Add("--flat-playlist"); args.Add("--yes-playlist"); }
        else args.Add("--no-playlist");
        args.Add(url);
        var json = await Json(args, ct, lista ? 120 : 60);
        var info = YtDlpDatos.LeerInfo(json);
        if (string.IsNullOrEmpty(info.Video.Url)) info.Video.Url = url;
        return info;
    }

    /// <summary>Sugerencias de búsqueda de YouTube mientras se escribe.</summary>
    public static async Task<List<string>> Sugerencias(string q, CancellationToken ct)
    {
        try
        {
            var t = await Red.Texto("https://suggestqueries.google.com/complete/search?client=firefox&ds=yt&hl=es&q=" + Uri.EscapeDataString(q), ct, 5);
            using var d = JsonDocument.Parse(t);
            return d.RootElement[1].EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Take(8).ToList();
        }
        catch { return new(); }
    }

    static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    static readonly XNamespace Yt = "http://www.youtube.com/xml/schemas/2015";
    static readonly XNamespace Media = "http://search.yahoo.com/mrss/";

    /// <summary>Últimos videos de un canal por su feed RSS (rápido, sin yt-dlp).</summary>
    public static async Task<List<Video>> Feed(string canalId, CancellationToken ct)
    {
        var xml = await Red.Texto("https://www.youtube.com/feeds/videos.xml?channel_id=" + Uri.EscapeDataString(canalId), ct, 15);
        return LeerFeed(xml, canalId);
    }

    public static List<Video> LeerFeed(string xml, string canalId)
    {
        var doc = XDocument.Parse(xml);
        var lista = new List<Video>();
        foreach (var e in doc.Root!.Elements(Atom + "entry"))
        {
            var id = (string?)e.Element(Yt + "videoId") ?? "";
            if (id.Length != 11) continue;
            var link = e.Elements(Atom + "link").Select(l => (string?)l.Attribute("href") ?? "").FirstOrDefault() ?? "";
            var g = e.Element(Media + "group");
            long? vistas = long.TryParse((string?)g?.Element(Media + "community")?.Element(Media + "statistics")?.Attribute("views"), out var n) ? n : null;
            lista.Add(new Video
            {
                Id = id,
                Titulo = (string?)e.Element(Atom + "title") ?? "",
                Canal = (string?)e.Element(Atom + "author")?.Element(Atom + "name") ?? "",
                CanalId = canalId,
                Url = link.Contains("/shorts/") ? link : "https://www.youtube.com/watch?v=" + id,
                Miniatura = $"https://i.ytimg.com/vi/{id}/mqdefault.jpg",
                Fecha = DateTime.TryParse((string?)e.Element(Atom + "published"), out var f) ? f : null,
                Vistas = vistas,
            });
        }
        return lista;
    }
}
