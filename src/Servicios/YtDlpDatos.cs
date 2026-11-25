using System.Globalization;
using System.Text.Json;

namespace MojarraTube;

/// <summary>Lógica pura sobre yt-dlp: leer su JSON, elegir formatos, armar argumentos y leer el progreso.</summary>
public static class YtDlpDatos
{
    // ───────────── Lectura del JSON ─────────────
    static string S(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static double? D(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static long? L(JsonElement e, string n) => D(e, n) is { } d ? (long)d : null;
    static bool B(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    static string Https(string u) => u.StartsWith("//") ? "https:" + u : u;

    static string PrimeroNoVacio(params string[] s) => s.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "";

    /// <summary>Miniatura de tamaño mediano (≈ 320–720 px de ancho) de la lista "thumbnails".</summary>
    public static string ElegirMiniatura(JsonElement e, int anchoIdeal = 480)
    {
        if (e.TryGetProperty("thumbnails", out var th) && th.ValueKind == JsonValueKind.Array && th.GetArrayLength() > 0)
        {
            var lista = th.EnumerateArray().Select(t => (url: Https(S(t, "url")), w: D(t, "width") ?? 0)).Where(t => t.url.Length > 0).ToList();
            var conAncho = lista.Where(t => t.w > 0).OrderBy(t => Math.Abs(t.w - anchoIdeal)).ToList();
            if (conAncho.Count > 0) return conAncho[0].url;
            if (lista.Count > 0) return lista[^1].url;
        }
        return Https(S(e, "thumbnail"));
    }

    static DateTime? Fecha(JsonElement e)
    {
        var ts = D(e, "timestamp") ?? D(e, "release_timestamp");
        if (ts is { } t && t > 0) return DateTimeOffset.FromUnixTimeSeconds((long)t).LocalDateTime;
        return Texto.FechaYtDlp(S(e, "upload_date")) ?? Texto.FechaYtDlp(S(e, "release_date"));
    }

    /// <summary>Una entrada de búsqueda, canal o lista (modo --flat-playlist) o un video completo.</summary>
    public static Video LeerVideo(JsonElement e)
    {
        var url = S(e, "url");
        var webpage = S(e, "webpage_url");
        var id = S(e, "id");
        var ie = PrimeroNoVacio(S(e, "ie_key"), S(e, "extractor_key"));
        var v = new Video
        {
            Id = id,
            Titulo = PrimeroNoVacio(S(e, "title"), S(e, "fulltitle"), id),
            Canal = PrimeroNoVacio(S(e, "channel"), S(e, "uploader"), S(e, "playlist_uploader")),
            CanalId = PrimeroNoVacio(S(e, "channel_id"), S(e, "uploader_id")),
            Duracion = D(e, "duration"),
            Vistas = L(e, "view_count"),
            Fecha = Fecha(e),
            EnVivo = S(e, "live_status") == "is_live" || B(e, "is_live"),
            Url = PrimeroNoVacio(webpage, url),
        };
        bool esYt = ie.StartsWith("Youtube", StringComparison.OrdinalIgnoreCase) || Texto.EsYouTube(v.Url);
        if (ie == "YoutubeTab" || (esYt && (v.Url.Contains("/channel/") || v.Url.Contains("/@") || v.Url.Contains("list="))))
        {
            v.Tipo = v.Url.Contains("list=") || id.StartsWith("PL") || id.StartsWith("OL") ? TipoItem.Lista : TipoItem.Canal;
            if (v.Tipo == TipoItem.Canal)
            {
                if (string.IsNullOrEmpty(v.CanalId) && id.StartsWith("UC")) v.CanalId = id;
                if (string.IsNullOrEmpty(v.Canal)) v.Canal = v.Titulo;
                v.Vistas = null;
            }
            v.Miniatura = ElegirMiniatura(e, 240);
            if (v.Tipo == TipoItem.Canal && L(e, "channel_follower_count") is { } seg) v.Vistas = null;
            return v;
        }
        if (esYt && id.Length == 11)
        {
            v.Url = "https://www.youtube.com/watch?v=" + id;
            v.Miniatura = $"https://i.ytimg.com/vi/{id}/mqdefault.jpg";
        }
        else v.Miniatura = ElegirMiniatura(e);
        return v;
    }

    public static Pagina LeerPagina(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var p = new Pagina
        {
            Titulo = PrimeroNoVacio(S(r, "channel"), S(r, "uploader"), S(r, "title")),
            CanalId = PrimeroNoVacio(S(r, "channel_id"), S(r, "id")),
            Seguidores = L(r, "channel_follower_count"),
            Descripcion = S(r, "description"),
        };
        if (r.TryGetProperty("thumbnails", out var th) && th.ValueKind == JsonValueKind.Array)
            foreach (var t in th.EnumerateArray())
            {
                var id = S(t, "id");
                if (id == "avatar_uncropped") p.Avatar = Https(S(t, "url"));
                else if (id == "banner_uncropped") p.Banner = Https(S(t, "url"));
            }
        if (r.TryGetProperty("entries", out var en) && en.ValueKind == JsonValueKind.Array)
            foreach (var e in en.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                // Pestañas anidadas de un canal (Videos, Shorts…): se aplanan
                if (S(e, "_type") == "playlist" && e.TryGetProperty("entries", out var sub) && sub.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in sub.EnumerateArray()) if (s.ValueKind == JsonValueKind.Object) p.Videos.Add(LeerVideo(s));
                    continue;
                }
                var v = LeerVideo(e);
                if (string.IsNullOrEmpty(v.Canal) && v.Tipo == TipoItem.Video) v.Canal = p.Titulo;
                if (string.IsNullOrEmpty(v.CanalId) && v.Tipo == TipoItem.Video) v.CanalId = p.CanalId;
                p.Videos.Add(v);
            }
        return p;
    }

    public static Formato LeerFormato(JsonElement f)
    {
        var fm = new Formato
        {
            Id = S(f, "format_id"),
            Ext = S(f, "ext"),
            VCodec = f.TryGetProperty("vcodec", out var vc) && vc.ValueKind == JsonValueKind.String ? vc.GetString() ?? "" : "",
            ACodec = f.TryGetProperty("acodec", out var ac) && ac.ValueKind == JsonValueKind.String ? ac.GetString() ?? "" : "",
            Alto = (int)(D(f, "height") ?? 0),
            Ancho = (int)(D(f, "width") ?? 0),
            Fps = D(f, "fps") ?? 0,
            Tbr = D(f, "tbr") ?? 0,
            Abr = D(f, "abr") ?? 0,
            Tamano = L(f, "filesize") ?? L(f, "filesize_approx") ?? 0,
            Protocolo = S(f, "protocol"),
            Url = S(f, "url"),
            Nota = S(f, "format_note"),
            Idioma = S(f, "language"),
        };
        if (f.TryGetProperty("http_headers", out var h) && h.ValueKind == JsonValueKind.Object)
            foreach (var p in h.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) fm.Cabeceras[p.Name] = p.Value.GetString() ?? "";
        if (fm.Protocolo == "" && fm.Url.StartsWith("http")) fm.Protocolo = fm.Url.Contains(".m3u8") ? "m3u8_native" : "https";
        return fm;
    }

    public static InfoVideo LeerInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var info = new InfoVideo
        {
            Descripcion = S(r, "description"),
            Seguidores = L(r, "channel_follower_count"),
            MeGusta = L(r, "like_count"),
            Extractor = PrimeroNoVacio(S(r, "extractor_key"), S(r, "extractor")),
        };
        if (S(r, "_type") == "playlist")
        {
            info.EsLista = true;
            info.TituloLista = S(r, "title");
            info.Video = new Video { Id = S(r, "id"), Titulo = S(r, "title"), Canal = PrimeroNoVacio(S(r, "channel"), S(r, "uploader")), Url = S(r, "webpage_url"), Tipo = TipoItem.Lista };
            if (r.TryGetProperty("entries", out var en) && en.ValueKind == JsonValueKind.Array)
                foreach (var e in en.EnumerateArray()) if (e.ValueKind == JsonValueKind.Object) info.Entradas.Add(LeerVideo(e));
            info.Video.Miniatura = info.Entradas.FirstOrDefault()?.Miniatura ?? ElegirMiniatura(r);
            return info;
        }
        info.Video = LeerVideo(r);
        if (info.Video.EsYouTube && info.Video.Id.Length == 11) info.Video.Miniatura = $"https://i.ytimg.com/vi/{info.Video.Id}/hqdefault.jpg";
        var grande = ElegirMiniatura(r, 1280);
        if (!string.IsNullOrEmpty(grande)) info.Video.Miniatura = grande;
        if (r.TryGetProperty("formats", out var fs) && fs.ValueKind == JsonValueKind.Array)
            foreach (var f in fs.EnumerateArray()) info.Formatos.Add(LeerFormato(f));
        else if (S(r, "url").Length > 0)
            info.Formatos.Add(LeerFormato(r));
        if (r.TryGetProperty("chapters", out var ch) && ch.ValueKind == JsonValueKind.Array)
            foreach (var c in ch.EnumerateArray())
                info.Capitulos.Add(new Capitulo { Inicio = D(c, "start_time") ?? 0, Fin = D(c, "end_time") ?? 0, Titulo = S(c, "title") });
        return info;
    }

    // ───────────── Elegir formatos para reproducir ─────────────
    static int RangoCodec(string vcodec, int alto)
    {
        vcodec = vcodec.ToLowerInvariant();
        // h264 se decodifica en cualquier equipo; para más de 1080p YouTube solo ofrece VP9/AV1
        if (vcodec.StartsWith("avc") || vcodec.StartsWith("h264")) return alto > 1080 ? 1 : 3;
        if (vcodec.StartsWith("vp9") || vcodec.StartsWith("vp09")) return 2;
        if (vcodec.StartsWith("av01")) return alto > 1080 ? 2 : 1;
        return 0;
    }

    static bool Reproducible(Formato f) => f.Url.Length > 0 && (f.EsDirecto || f.EsHls) && f.Ext != "mhtml" && !f.Id.StartsWith("sb");

    static bool EsAudioOriginal(Formato f) =>
        f.Nota.Contains("original", StringComparison.OrdinalIgnoreCase) || f.Nota.Contains("default", StringComparison.OrdinalIgnoreCase);

    public static Formato? MejorAudio(IEnumerable<Formato> formatos, bool preferirM4a = true) =>
        formatos.Where(f => Reproducible(f) && f.TieneAudio && !f.TieneVideo)
                .OrderByDescending(f => EsAudioOriginal(f))
                .ThenByDescending(f => !f.Nota.Contains("drc", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => preferirM4a && (f.Ext == "m4a" || f.ACodec.StartsWith("mp4a")))
                .ThenByDescending(f => f.EsDirecto)
                .ThenByDescending(f => f.Abr > 0 ? f.Abr : f.Tbr)
                .FirstOrDefault();

    /// <summary>
    /// Elige qué reproducir: video (+ audio aparte si YouTube los separa) con el alto máximo pedido (0 = el mejor).
    /// </summary>
    public static (Formato? video, Formato? audio) ElegirReproduccion(IReadOnlyList<Formato> formatos, int altoMax)
    {
        static IOrderedEnumerable<Formato> Ordenar(IEnumerable<Formato> l, int max) =>
            l.OrderByDescending(f => max <= 0 || f.Alto <= max)
             .ThenByDescending(f => max <= 0 || f.Alto <= max ? f.Alto : -f.Alto)
             .ThenByDescending(f => f.EsDirecto)
             .ThenByDescending(f => RangoCodec(f.VCodec, f.Alto))
             .ThenByDescending(f => f.Fps)
             .ThenByDescending(f => f.Tbr);

        var soloVideo = formatos.Where(f => Reproducible(f) && f.TieneVideo && !f.TieneAudio && f.VCodec != "").ToList();
        var completos = formatos.Where(f => Reproducible(f) && f.TieneVideo && f.TieneAudio).ToList();
        var audio = MejorAudio(formatos);

        var v = soloVideo.Count > 0 ? Ordenar(soloVideo, altoMax).First() : null;
        var c = completos.Count > 0 ? Ordenar(completos, altoMax).First() : null;

        if (v != null && audio != null && (c == null || Cabe(v, altoMax) && (!Cabe(c, altoMax) || v.Alto > c.Alto || (v.Alto == c.Alto && v.EsDirecto && !c.EsDirecto))))
            return (v, audio);
        if (c != null) return (c, null);
        if (v != null) return (v, audio);
        if (audio != null) return (null, audio);
        return (formatos.LastOrDefault(Reproducible), null);

        static bool Cabe(Formato f, int max) => max <= 0 || f.Alto <= max;
    }

    /// <summary>Altos disponibles (2160, 1440, 1080…) de mayor a menor.</summary>
    public static List<int> Calidades(IEnumerable<Formato> formatos) =>
        formatos.Where(f => f.TieneVideo && f.Alto > 0 && f.Ext != "mhtml" && !f.Id.StartsWith("sb"))
                .Select(f => NormalizarAlto(f.Alto, f.Ancho)).Distinct().OrderByDescending(a => a).ToList();

    /// <summary>Videos verticales u horizontales con barras: se usa el lado corto como "p".</summary>
    public static int NormalizarAlto(int alto, int ancho)
    {
        var lado = ancho > 0 && ancho < alto ? ancho : alto;
        int[] std = { 4320, 2160, 1440, 1080, 720, 480, 360, 240, 144 };
        foreach (var s in std) if (lado >= s * 0.9) return s;
        return lado;
    }

    public static string NombreCalidad(int alto) => alto switch
    {
        >= 4320 => "8K",
        >= 2160 => "4K",
        >= 1440 => "1440p (2K)",
        0 => "Automática",
        _ => alto + "p",
    };

    /// <summary>Tamaño aproximado de descargar a cierta calidad (video + audio).</summary>
    public static long TamanoEstimado(IReadOnlyList<Formato> formatos, int altoMax, double? duracion)
    {
        var v = formatos.Where(f => f.TieneVideo && f.Alto > 0 && (altoMax <= 0 || NormalizarAlto(f.Alto, f.Ancho) <= altoMax))
                        .OrderByDescending(f => NormalizarAlto(f.Alto, f.Ancho)).ThenByDescending(f => RangoCodec(f.VCodec, f.Alto) == 3).ThenByDescending(f => f.Tbr).FirstOrDefault();
        var a = v != null && v.TieneAudio ? null : MejorAudio(formatos);
        return Tam(v, duracion) + Tam(a, duracion);
        static long Tam(Formato? f, double? d) => f == null ? 0 : f.Tamano > 0 ? f.Tamano : d is > 0 && f.Tbr > 0 ? (long)(f.Tbr * 1000 / 8 * d.Value) : 0;
    }

    // ───────────── Argumentos ─────────────
    public static List<string> ArgsComunes(Ajustes a)
    {
        var l = new List<string> { "--ignore-config", "--no-warnings", "--no-color" };
        if (!string.IsNullOrEmpty(a.CookiesNavegador)) { l.Add("--cookies-from-browser"); l.Add(a.CookiesNavegador); }
        return l;
    }

    public const string MarcaProgreso = "MTPROG ";
    public const string MarcaArchivo = "MTFILE ";

    public static List<string> ArgsDescarga(RegistroDescarga d, Ajustes a, string ffmpeg)
    {
        var l = ArgsComunes(a);
        l.AddRange(new[]
        {
            "--newline", "--progress",
            "--progress-template", "download:" + MarcaProgreso + "%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s|%(info.playlist_index)s|%(info.n_entries)s",
            "--print", "after_move:" + MarcaArchivo + "%(filepath)s",
            "--ffmpeg-location", ffmpeg,
            "-P", d.Carpeta,
            "-o", d.EsLista ? "%(playlist_title).80B/%(playlist_index)03d - %(title).120B [%(id)s].%(ext)s" : "%(title).150B [%(id)s].%(ext)s",
            "--windows-filenames", "--no-mtime", "--embed-metadata", "--embed-thumbnail",
            "--retries", "10", "--fragment-retries", "10", "--concurrent-fragments", "4",
            d.EsLista ? "--yes-playlist" : "--no-playlist",
        });
        if (d.EsLista) l.Add("--ignore-errors");
        if (d.Modo == "audio")
        {
            var fmt = d.FormatoAudio is "m4a" or "opus" or "mp3" or "wav" or "flac" ? d.FormatoAudio : "mp3";
            l.Add("-f");
            l.Add(fmt switch { "m4a" => "ba[ext=m4a]/ba/b", "opus" => "ba[acodec=opus]/ba/b", _ => "ba/b" });
            l.AddRange(new[] { "-x", "--audio-format", fmt });
            l.Add("--audio-quality");
            l.Add(fmt == "mp3" && d.Calidad > 0 ? d.Calidad + "K" : "0");
            if (fmt is "wav") l.Remove("--embed-thumbnail");
        }
        else
        {
            l.AddRange(new[] { "-f", "bv*+ba/b", "-S", (d.Calidad > 0 ? $"res:{d.Calidad}," : "") + "vcodec:h264,acodec:m4a", "--merge-output-format", "mp4", "--embed-chapters" });
        }
        l.Add(d.Url);
        return l;
    }

    public record Progreso(long Descargado, long Total, double Velocidad, double? Eta, int Indice, int Cantidad)
    {
        public double Fraccion => Total > 0 ? Math.Clamp((double)Descargado / Total, 0, 1) : 0;
    }

    public static Progreso? LeerProgreso(string linea)
    {
        var i = linea.IndexOf(MarcaProgreso, StringComparison.Ordinal);
        if (i < 0) return null;
        var p = linea[(i + MarcaProgreso.Length)..].Split('|');
        if (p.Length < 5) return null;
        static double N(string s) => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var total = (long)N(p[1]);
        if (total <= 0) total = (long)N(p[2]);
        double? eta = N(p[4]) is > 0 and var e ? e : null;
        return new Progreso((long)N(p[0]), total, N(p[3]), eta, p.Length > 5 ? (int)N(p[5]) : 0, p.Length > 6 ? (int)N(p[6]) : 0);
    }

    public static string? LeerArchivo(string linea) =>
        linea.StartsWith(MarcaArchivo, StringComparison.Ordinal) ? linea[MarcaArchivo.Length..].Trim() : null;

    // ───────────── Errores en palabras claras ─────────────
    public static string Explicar(string errores)
    {
        var lineas = errores.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)).ToList();
        var e = lineas.LastOrDefault() ?? errores.Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
        bool Tiene(params string[] s) => s.Any(x => e.Contains(x, StringComparison.OrdinalIgnoreCase));
        if (e.Length == 0) return "Algo salió mal y no hubo detalles. Vuelve a intentarlo.";
        if (Tiene("Unsupported URL", "is not a valid URL", "No video formats found", "Unable to extract"))
            return "No se encontró un video en ese enlace. Revisa que la página tenga un video y que el enlace esté completo.";
        if (Tiene("Private video")) return "Este video es privado.";
        if (Tiene("members-only", "Join this channel")) return "Este video es solo para miembros del canal.";
        if (Tiene("Sign in to confirm your age", "age-restricted", "inappropriate for some users"))
            return "Este video tiene restricción de edad. Elige tu navegador en Ajustes › Sesión para usar tu cuenta.";
        if (Tiene("not a bot", "Sign in to confirm"))
            return "YouTube pidió confirmar que no eres un robot. Actualiza los componentes en Ajustes o elige tu navegador en Ajustes › Sesión.";
        if (Tiene("Video unavailable", "This video is unavailable", "has been removed", "account associated with this video has been terminated"))
            return "Este video ya no está disponible.";
        if (Tiene("not available in your country", "geo restrict", "geo-restrict")) return "Este video no está disponible en tu país.";
        if (Tiene("Premieres in", "live event will begin", "This live event")) return "Este video aún no se estrena.";
        if (Tiene("HTTP Error 403")) return "El servidor rechazó la descarga (error 403). Actualiza los componentes en Ajustes y vuelve a intentarlo.";
        if (Tiene("HTTP Error 429", "Too Many Requests")) return "Demasiadas solicitudes seguidas. Espera unos minutos y vuelve a intentarlo.";
        if (Tiene("getaddrinfo", "Failed to resolve", "Connection refused", "timed out", "Network is unreachable", "No connection", "Errno 110", "Errno 11001"))
            return "No hay conexión a internet o el sitio no responde.";
        if (Tiene("No space left", "Errno 28")) return "No hay espacio suficiente en el disco.";
        if (Tiene("Permission denied", "Errno 13")) return "No se puede escribir en la carpeta de descargas. Elige otra en Ajustes.";
        if (Tiene("ffmpeg", "ffprobe")) return "Falta FFmpeg o falló al procesar el archivo. Reinstala los componentes en Ajustes.";
        if (Tiene("cookies", "could not copy", "Could not find") && Tiene("browser", "cookie"))
            return "No se pudieron leer las cookies del navegador. Ciérralo y vuelve a intentarlo, o elige otro en Ajustes › Sesión.";
        if (Tiene("DRM")) return "Este video está protegido (DRM) y no se puede reproducir ni descargar.";
        var limpio = e.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ? e[6..].Trim() : e;
        if (limpio.StartsWith('[')) limpio = limpio[(limpio.IndexOf(']') + 1)..].Trim();
        return "No se pudo completar: " + (limpio.Length > 220 ? limpio[..220] + "…" : limpio);
    }
}
