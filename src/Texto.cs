using System.Globalization;
using System.Text.RegularExpressions;

namespace MojarraTube;

/// <summary>Formatos de texto y reconocimiento de enlaces (sin dependencias de WPF: se prueba aparte).</summary>
public static partial class Texto
{
    static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    public static string Tiempo(double segundos)
    {
        if (double.IsNaN(segundos) || segundos < 0) segundos = 0;
        var t = TimeSpan.FromSeconds(Math.Floor(segundos));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static string Numero(long n)
    {
        if (n >= 1_000_000_000) return (n / 1e9).ToString(n < 10e9 ? "0.#" : "0", Es) + " mil M";
        if (n >= 1_000_000) return (n / 1e6).ToString(n < 10e6 ? "0.#" : "0", Es) + " M";
        if (n >= 1_000) return (n / 1e3).ToString(n < 10e3 ? "0.#" : "0", Es) + " mil";
        return n.ToString(Es);
    }

    public static string Vistas(long n) => n == 1 ? "1 vista" : Numero(n) + (n >= 1_000_000 ? " de vistas" : " vistas");
    public static string Suscriptores(long n) => Numero(n) + (n >= 1_000_000 ? " de" : "") + (n == 1 ? " suscriptor" : " suscriptores");

    public static string Hace(DateTime fecha, DateTime? ahora = null)
    {
        var d = (ahora ?? DateTime.Now) - fecha;
        if (d.TotalSeconds < 0) return "hace un momento";
        static string P(int n, string uno, string varios) => $"hace {n} {(n == 1 ? uno : varios)}";
        if (d.TotalMinutes < 1) return "hace un momento";
        if (d.TotalHours < 1) return P((int)d.TotalMinutes, "minuto", "minutos");
        if (d.TotalDays < 1) return P((int)d.TotalHours, "hora", "horas");
        if (d.TotalDays < 7) return P((int)d.TotalDays, "día", "días");
        if (d.TotalDays < 30) return P((int)(d.TotalDays / 7), "semana", "semanas");
        if (d.TotalDays < 365) return P(Math.Max(1, (int)(d.TotalDays / 30.4)), "mes", "meses");
        return P((int)(d.TotalDays / 365.25), "año", "años");
    }

    public static string Bytes(double b)
    {
        if (b <= 0) return "";
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return b.ToString(i == 0 ? "0" : b < 10 ? "0.0" : "0", Es) + " " + u[i];
    }

    public static bool EsUrl(string? s)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s) || s.Contains(' ')) return false;
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(s, UriKind.Absolute, out _);
        // "youtube.com/…", "vimeo.com/123", "youtu.be/…"
        return RegexDominio().IsMatch(s);
    }

    public static string NormalizarUrl(string s)
    {
        s = s.Trim();
        return s.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? s : "https://" + s;
    }

    public static bool EsYouTube(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (!Uri.TryCreate(NormalizarUrl(url), UriKind.Absolute, out var u)) return false;
        var h = u.Host.ToLowerInvariant();
        return h == "youtu.be" || h == "youtube.com" || h.EndsWith(".youtube.com") || h == "youtube-nocookie.com" || h.EndsWith(".youtube-nocookie.com");
    }

    /// <summary>Id de 11 caracteres de un enlace de video de YouTube (watch, youtu.be, shorts, live, embed), o null.</summary>
    public static string? IdYouTube(string? url)
    {
        if (!EsYouTube(url)) return null;
        var u = new Uri(NormalizarUrl(url!));
        string? id = null;
        if (u.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)) id = u.AbsolutePath.Trim('/').Split('/')[0];
        else
        {
            var m = RegexRuta().Match(u.AbsolutePath);
            if (m.Success) id = m.Groups[1].Value;
            else if (u.AbsolutePath.TrimEnd('/') == "/watch") id = Parametro(u.Query, "v");
        }
        return id != null && RegexId().IsMatch(id) ? id : null;
    }

    /// <summary>Id de lista (list=…) de un enlace de YouTube, o null.</summary>
    public static string? ListaYouTube(string? url)
    {
        if (!EsYouTube(url)) return null;
        var l = Parametro(new Uri(NormalizarUrl(url!)).Query, "list");
        return string.IsNullOrEmpty(l) ? null : l;
    }

    /// <summary>Tiempo de inicio (t=1m30s, t=90, start=90) de un enlace, en segundos.</summary>
    public static double? InicioEnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(NormalizarUrl(url), UriKind.Absolute, out var u)) return null;
        var t = Parametro(u.Query, "t") ?? Parametro(u.Query, "start");
        if (string.IsNullOrEmpty(t)) return null;
        if (double.TryParse(t.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
        var m = RegexHms().Match(t);
        if (!m.Success) return null;
        double V(int g) => m.Groups[g].Success ? double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
        return V(1) * 3600 + V(2) * 60 + V(3);
    }

    public static string? Parametro(string query, string nombre)
    {
        foreach (var par in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = par.IndexOf('=');
            var k = i < 0 ? par : par[..i];
            if (k == nombre) return i < 0 ? "" : Uri.UnescapeDataString(par[(i + 1)..].Replace('+', ' '));
        }
        return null;
    }

    /// <summary>Nombre de archivo válido en Windows.</summary>
    public static string NombreArchivo(string s)
    {
        var malos = new HashSet<char>("<>:\"/\\|?*");
        var r = new string(s.Select(c => c < 32 || malos.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return r.Length == 0 ? "video" : r.Length > 150 ? r[..150] : r;
    }

    /// <summary>"20240131" → fecha.</summary>
    public static DateTime? FechaYtDlp(string? s) =>
        s != null && DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    [GeneratedRegex(@"^(www\.)?[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}(/\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RegexDominio();
    [GeneratedRegex(@"^/(?:shorts|live|embed|v|e)/([A-Za-z0-9_-]{11})")]
    private static partial Regex RegexRuta();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")]
    private static partial Regex RegexId();
    [GeneratedRegex(@"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$")]
    private static partial Regex RegexHms();
}
