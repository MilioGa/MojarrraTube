using MojarraTube;

int ok = 0, mal = 0;
void Igual<T>(T esperado, T real, string que)
{
    if (EqualityComparer<T>.Default.Equals(esperado, real)) ok++;
    else { mal++; Console.WriteLine($"✗ {que}: esperaba «{esperado}», salió «{real}»"); }
}
void Cierto(bool c, string que) => Igual(true, c, que);

// ── Texto ──
Igual("0:05", Texto.Tiempo(5.7), "tiempo corto");
Igual("1:02:03", Texto.Tiempo(3723), "tiempo con horas");
Igual("12:00", Texto.Tiempo(720), "minutos");
Igual("1.2 M de vistas", Texto.Vistas(1_234_567), "vistas millones");
Igual("15 mil vistas", Texto.Vistas(15_300), "vistas miles");
Igual("1 vista", Texto.Vistas(1), "una vista");
Igual("hace 3 días", Texto.Hace(new DateTime(2026, 1, 10), new DateTime(2026, 1, 13, 1, 0, 0)), "hace días");
Igual("hace 1 mes", Texto.Hace(new DateTime(2026, 1, 1), new DateTime(2026, 2, 5)), "hace mes");
Igual("hace 2 años", Texto.Hace(new DateTime(2024, 1, 1), new DateTime(2026, 2, 5)), "hace años");
Igual("1.5 MB", Texto.Bytes(1.5 * 1024 * 1024), "bytes");
Cierto(Texto.EsUrl("https://vimeo.com/123"), "url https");
Cierto(Texto.EsUrl("youtu.be/dQw4w9WgXcQ"), "url sin esquema");
Cierto(!Texto.EsUrl("gatos graciosos"), "texto no es url");
Cierto(!Texto.EsUrl("hola.mundo cosas"), "texto con espacio");
Igual("dQw4w9WgXcQ", Texto.IdYouTube("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s"), "id watch");
Igual("dQw4w9WgXcQ", Texto.IdYouTube("https://youtu.be/dQw4w9WgXcQ?si=abc"), "id corto");
Igual("dQw4w9WgXcQ", Texto.IdYouTube("https://www.youtube.com/shorts/dQw4w9WgXcQ"), "id shorts");
Igual("dQw4w9WgXcQ", Texto.IdYouTube("m.youtube.com/watch?v=dQw4w9WgXcQ"), "id móvil sin esquema");
Igual<string?>(null, Texto.IdYouTube("https://www.youtube.com/@canal"), "canal no es video");
Igual<string?>(null, Texto.IdYouTube("https://vimeo.com/123"), "vimeo no es youtube");
Igual("PL123abc", Texto.ListaYouTube("https://www.youtube.com/playlist?list=PL123abc"), "lista");
Igual<double?>(42, Texto.InicioEnUrl("https://youtu.be/dQw4w9WgXcQ?t=42"), "t=42");
Igual<double?>(90, Texto.InicioEnUrl("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=1m30s"), "t=1m30s");
Igual("a_b_c", Texto.NombreArchivo("a/b:c"), "nombre archivo");

// ── Búsqueda (modo --flat-playlist) ──
var busqueda = """
{"id":"gatos","title":"gatos","_type":"playlist","entries":[
 {"_type":"url","ie_key":"Youtube","id":"aaaaaaaaaaa","url":"https://www.youtube.com/watch?v=aaaaaaaaaaa","title":"Gato toca piano","duration":125.0,"channel":"Michi TV","channel_id":"UCmichi","view_count":1500000,"thumbnails":[{"url":"https://i.ytimg.com/vi/aaaaaaaaaaa/hq720.jpg","height":202,"width":360}],"live_status":null},
 {"_type":"url","ie_key":"YoutubeTab","id":"UCcanal","url":"https://www.youtube.com/channel/UCcanal","title":"Canal de Gatos","channel":"Canal de Gatos","channel_id":"UCcanal","thumbnails":[{"url":"//yt3.googleusercontent.com/abc=s88","height":88,"width":88}]},
 {"_type":"url","ie_key":"YoutubeTab","id":"PLxyz","url":"https://www.youtube.com/playlist?list=PLxyz","title":"Lista de gatos","channel":"Michi TV"},
 {"_type":"url","ie_key":"Youtube","id":"bbbbbbbbbbb","url":"https://www.youtube.com/shorts/bbbbbbbbbbb","title":"Short","duration":null,"live_status":"is_live","channel":"X"}
]}
""";
var p = YtDlpDatos.LeerPagina(busqueda);
Igual(4, p.Videos.Count, "entradas de búsqueda");
Igual(TipoItem.Video, p.Videos[0].Tipo, "video");
Igual("https://i.ytimg.com/vi/aaaaaaaaaaa/mqdefault.jpg", p.Videos[0].Miniatura, "miniatura youtube");
Igual("2:05", p.Videos[0].DuracionTexto, "duración");
Igual(TipoItem.Canal, p.Videos[1].Tipo, "canal");
Igual("https://yt3.googleusercontent.com/abc=s88", p.Videos[1].Miniatura, "avatar con https");
Igual(TipoItem.Lista, p.Videos[2].Tipo, "lista");
Cierto(p.Videos[3].EnVivo, "en vivo");
Igual("EN VIVO", p.Videos[3].DuracionTexto, "texto en vivo");

// ── Canal con pestañas anidadas ──
var canal = """
{"id":"UCmichi","channel":"Michi TV","channel_id":"UCmichi","title":"Michi TV - Videos","channel_follower_count":250000,
 "thumbnails":[{"url":"https://yt3/banner","id":"banner_uncropped"},{"url":"https://yt3/avatar","id":"avatar_uncropped"}],
 "entries":[{"_type":"url","ie_key":"Youtube","id":"ccccccccccc","title":"Video 1","duration":61},{"_type":"url","ie_key":"Youtube","id":"ddddddddddd","title":"Video 2"}]}
""";
var pc = YtDlpDatos.LeerPagina(canal);
Igual("Michi TV", pc.Titulo, "nombre canal");
Igual("https://yt3/avatar", pc.Avatar, "avatar canal");
Igual("https://yt3/banner", pc.Banner, "banner canal");
Igual(2, pc.Videos.Count, "videos canal");
Igual("Michi TV", pc.Videos[1].Canal, "canal heredado");
Igual("UCmichi", pc.Videos[1].CanalId, "id canal heredado");

// ── Información completa y elección de formatos ──
var info = """
{"id":"aaaaaaaaaaa","title":"Gato toca piano","channel":"Michi TV","channel_id":"UCmichi","duration":125,"view_count":1500000,"like_count":20000,
 "upload_date":"20250102","description":"Hola 0:30 parte dos https://ejemplo.com","channel_follower_count":250000,"extractor_key":"Youtube","webpage_url":"https://www.youtube.com/watch?v=aaaaaaaaaaa",
 "chapters":[{"start_time":0,"end_time":30,"title":"Intro"},{"start_time":30,"end_time":125,"title":"Piano"}],
 "formats":[
  {"format_id":"sb0","ext":"mhtml","vcodec":"none","acodec":"none","protocol":"mhtml","url":"https://sb"},
  {"format_id":"139","ext":"m4a","vcodec":"none","acodec":"mp4a.40.5","abr":48,"protocol":"https","url":"https://a139","format_note":"low"},
  {"format_id":"140","ext":"m4a","vcodec":"none","acodec":"mp4a.40.2","abr":129,"protocol":"https","url":"https://a140","format_note":"medium","filesize":2000000},
  {"format_id":"251","ext":"webm","vcodec":"none","acodec":"opus","abr":135,"protocol":"https","url":"https://a251","format_note":"medium"},
  {"format_id":"18","ext":"mp4","vcodec":"avc1.42001E","acodec":"mp4a.40.2","height":360,"width":640,"tbr":500,"protocol":"https","url":"https://v18"},
  {"format_id":"136","ext":"mp4","vcodec":"avc1.4d401f","acodec":"none","height":720,"width":1280,"fps":30,"tbr":1500,"protocol":"https","url":"https://v136","filesize":20000000},
  {"format_id":"247","ext":"webm","vcodec":"vp9","acodec":"none","height":720,"width":1280,"fps":30,"tbr":1200,"protocol":"https","url":"https://v247"},
  {"format_id":"137","ext":"mp4","vcodec":"avc1.640028","acodec":"none","height":1080,"width":1920,"fps":30,"tbr":4000,"protocol":"https","url":"https://v137","filesize":50000000},
  {"format_id":"313","ext":"webm","vcodec":"vp9","acodec":"none","height":2160,"width":3840,"fps":30,"tbr":18000,"protocol":"https","url":"https://v313"},
  {"format_id":"96","ext":"mp4","vcodec":"avc1","acodec":"mp4a","height":1080,"width":1920,"protocol":"m3u8_native","url":"https://hls96"}
 ]}
""";
var iv = YtDlpDatos.LeerInfo(info);
Igual("Gato toca piano", iv.Video.Titulo, "título info");
Igual(2, iv.Capitulos.Count, "capítulos");
Igual(new DateTime(2025, 1, 2), iv.Video.Fecha, "fecha subida");
var (v1080, a1080) = YtDlpDatos.ElegirReproduccion(iv.Formatos, 1080);
Igual("137", v1080?.Id, "video 1080 h264");
Igual("140", a1080?.Id, "audio m4a");
var (v720, _) = YtDlpDatos.ElegirReproduccion(iv.Formatos, 720);
Igual("136", v720?.Id, "720 prefiere h264");
var (vMax, _) = YtDlpDatos.ElegirReproduccion(iv.Formatos, 0);
Igual("313", vMax?.Id, "la mejor = 4K");
var (v240, a240) = YtDlpDatos.ElegirReproduccion(iv.Formatos, 240);
Igual("18", v240?.Id, "si no hay más bajo, el completo de 360");
Igual<string?>(null, a240?.Id, "completo sin audio aparte");
Igual("2160,1080,720,360", string.Join(",", YtDlpDatos.Calidades(iv.Formatos)), "calidades");
Igual(52000000L, YtDlpDatos.TamanoEstimado(iv.Formatos, 1080, 125), "tamaño 1080");
Igual("4K", YtDlpDatos.NombreCalidad(2160), "nombre 4K");
Igual(1080, YtDlpDatos.NormalizarAlto(1920, 1080), "vertical 1080x1920");

// Sitio genérico: un solo archivo sin códecs conocidos
var generico = """{"id":"x1","title":"Clip","url":"https://cdn.ejemplo.com/clip.mp4","ext":"mp4","extractor_key":"Generic","webpage_url":"https://ejemplo.com/clip"}""";
var ig = YtDlpDatos.LeerInfo(generico);
var (vg, ag) = YtDlpDatos.ElegirReproduccion(ig.Formatos, 1080);
Igual("https://cdn.ejemplo.com/clip.mp4", vg?.Url, "genérico reproducible");
Igual<string?>(null, ag?.Url, "genérico sin audio aparte");

// Lista
var lista = """{"_type":"playlist","id":"PLxyz","title":"Mi lista","channel":"Michi TV","webpage_url":"https://www.youtube.com/playlist?list=PLxyz","entries":[{"_type":"url","ie_key":"Youtube","id":"aaaaaaaaaaa","title":"Uno"},{"_type":"url","ie_key":"Youtube","id":"bbbbbbbbbbb","title":"Dos"}]}""";
var il = YtDlpDatos.LeerInfo(lista);
Cierto(il.EsLista, "es lista");
Igual(2, il.Entradas.Count, "entradas lista");
Igual("Mi lista", il.TituloLista, "título lista");

// ── Argumentos de descarga ──
var aj = new Ajustes();
var rv = new RegistroDescarga { Url = "https://youtu.be/aaaaaaaaaaa", Modo = "video", Calidad = 720, Carpeta = "C:\\Videos" };
var argsV = YtDlpDatos.ArgsDescarga(rv, aj, "C:\\ff\\ffmpeg.exe");
Cierto(argsV.Contains("res:720,vcodec:h264,acodec:m4a"), "orden de formatos 720");
Cierto(argsV.Contains("--merge-output-format") && argsV.Contains("mp4"), "salida mp4");
Cierto(argsV.Contains("--no-playlist"), "sin lista");
Igual("https://youtu.be/aaaaaaaaaaa", argsV[^1], "url al final");
var ra = new RegistroDescarga { Url = "u", Modo = "audio", FormatoAudio = "mp3", Calidad = 192, Carpeta = "C:\\M" };
var argsA = YtDlpDatos.ArgsDescarga(ra, new Ajustes { CookiesNavegador = "firefox" }, "ff");
Cierto(argsA.Contains("-x") && argsA.Contains("mp3") && argsA.Contains("192K"), "mp3 192");
Cierto(argsA.Contains("--cookies-from-browser") && argsA.Contains("firefox"), "cookies");
var rl = new RegistroDescarga { Url = "u", Modo = "video", EsLista = true, Carpeta = "C:\\V" };
Cierto(YtDlpDatos.ArgsDescarga(rl, aj, "ff").Contains("--yes-playlist"), "lista completa");

// ── Progreso y archivo final ──
var pr = YtDlpDatos.LeerProgreso("MTPROG 5242880|10485760|NA|1048576.0|5|NA|NA");
Igual(0.5, pr?.Fraccion, "progreso 50 %");
Igual<double?>(5, pr?.Eta, "eta");
var pr2 = YtDlpDatos.LeerProgreso("MTPROG 100|None|2000|NA|NA|3|10");
Igual(2000L, pr2?.Total, "total estimado");
Igual(3, pr2?.Indice, "índice de lista");
Igual(10, pr2?.Cantidad, "tamaño de lista");
Igual<YtDlpDatos.Progreso?>(null, YtDlpDatos.LeerProgreso("[download] Destination: x"), "línea normal");
Igual("C:\\V\\a.mp4", YtDlpDatos.LeerArchivo("MTFILE C:\\V\\a.mp4"), "archivo final");

// ── Errores en palabras claras ──
Cierto(YtDlpDatos.Explicar("ERROR: [youtube] abc: Private video. Sign in if you've been granted access").Contains("privado"), "error privado");
Cierto(YtDlpDatos.Explicar("ERROR: Unsupported URL: https://ejemplo.com").Contains("No se encontró un video"), "error url");
Cierto(YtDlpDatos.Explicar("ERROR: [youtube] x: Sign in to confirm you’re not a bot").Contains("robot"), "error bot");
Cierto(YtDlpDatos.Explicar("ERROR: unable to download video data: HTTP Error 403: Forbidden").Contains("403"), "error 403");
Cierto(YtDlpDatos.Explicar("").Length > 0, "error vacío");

// ── Feed RSS de un canal ──
var rss = """
<?xml version="1.0" encoding="UTF-8"?>
<feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns:media="http://search.yahoo.com/mrss/" xmlns="http://www.w3.org/2005/Atom">
 <title>Michi TV</title>
 <entry><id>yt:video:eeeeeeeeeee</id><yt:videoId>eeeeeeeeeee</yt:videoId><yt:channelId>UCmichi</yt:channelId><title>Nuevo video</title>
  <link rel="alternate" href="https://www.youtube.com/watch?v=eeeeeeeeeee"/><author><name>Michi TV</name></author><published>2026-11-20T10:00:00+00:00</published>
  <media:group><media:title>Nuevo video</media:title><media:community><media:statistics views="1234"/></media:community></media:group></entry>
</feed>
""";
var feed = YtDlp.LeerFeed(rss.Trim(), "UCmichi");
Igual(1, feed.Count, "feed entradas");
Igual("Nuevo video", feed[0].Titulo, "feed título");
Igual<long?>(1234, feed[0].Vistas, "feed vistas");
Igual("Michi TV", feed[0].Canal, "feed canal");

Console.WriteLine($"\n{ok} pruebas bien, {mal} mal");
return mal == 0 ? 0 : 1;
