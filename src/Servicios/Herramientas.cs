using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace MojarraTube;

/// <summary>
/// Componentes externos que se descargan la primera vez (y se actualizan solos):
/// yt-dlp (extrae y descarga videos), Deno (lo necesita yt-dlp para YouTube) y FFmpeg (une video+audio y convierte a MP3).
/// </summary>
public class Herramientas : Notificador
{
    public static Herramientas Instancia { get; } = new();

    const string UrlYtDlp = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    const string UrlDeno = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";
    const string UrlFfmpeg = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    public static string YtDlp => Path.Combine(Marca.DirComponentes, "yt-dlp.exe");
    public static string Deno => Path.Combine(Marca.DirComponentes, "deno.exe");
    public static string Ffmpeg => Path.Combine(Marca.DirComponentes, "ffmpeg.exe");
    public static string Ffprobe => Path.Combine(Marca.DirComponentes, "ffprobe.exe");

    public static bool HayYtDlp => File.Exists(YtDlp);
    public static bool HayFfmpeg => File.Exists(Ffmpeg);
    public static bool HayDeno => File.Exists(Deno);
    public static bool Completo => HayYtDlp && HayFfmpeg && HayDeno;

    TaskCompletionSource _ytDlpListo = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource _ffmpegListo = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task? _instalacion;

    bool _ocupado; string _mensaje = ""; double _progreso; string? _error;
    public bool Ocupado { get => _ocupado; private set => Poner(ref _ocupado, value); }
    public string Mensaje { get => _mensaje; private set => Poner(ref _mensaje, value); }
    public double Progreso { get => _progreso; private set => Poner(ref _progreso, value); }
    public string? Error { get => _error; private set => Poner(ref _error, value); }

    /// <summary>Espera a que yt-dlp esté instalado (lo instala si hace falta).</summary>
    public async Task EsperarYtDlp(CancellationToken ct = default)
    {
        if (HayYtDlp) return;
        _ = Instalar();
        await _ytDlpListo.Task.WaitAsync(ct);
    }

    public async Task EsperarFfmpeg(CancellationToken ct = default)
    {
        if (HayFfmpeg && HayYtDlp) return;
        _ = Instalar();
        await _ffmpegListo.Task.WaitAsync(ct);
    }

    /// <summary>Descarga lo que falte. Si ya está corriendo, no hace nada.</summary>
    public Task Instalar(bool forzar = false)
    {
        if (_instalacion is { IsCompleted: false }) return _instalacion;
        if (_ytDlpListo.Task.IsFaulted) _ytDlpListo = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_ffmpegListo.Task.IsFaulted) _ffmpegListo = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return _instalacion = Task.Run(() => InstalarAsync(forzar));
    }

    async Task InstalarAsync(bool forzar)
    {
        Ocupado = true; Error = null;
        try
        {
            if (forzar || !HayYtDlp)
                await Bajar("yt-dlp", UrlYtDlp, YtDlp, null);
            if (HayYtDlp) _ytDlpListo.TrySetResult();

            if (forzar || !HayDeno)
                await Bajar("Deno (motor de JavaScript para YouTube)", UrlDeno, null, z => Extraer(z, "deno.exe", Deno));

            if (forzar || !HayFfmpeg)
                await Bajar("FFmpeg (para unir video y audio)", UrlFfmpeg, null, z =>
                {
                    Extraer(z, "/bin/ffmpeg.exe", Ffmpeg);
                    Extraer(z, "/bin/ffprobe.exe", Ffprobe);
                });
            if (HayFfmpeg && HayYtDlp) _ffmpegListo.TrySetResult();
            Mensaje = "Componentes listos";
            Avisar(nameof(Version));
        }
        catch (Exception e)
        {
            Marca.Registrar(e);
            Error = "No se pudieron descargar los componentes. Revisa tu conexión a internet y vuelve a intentarlo.";
            Mensaje = Error;
            var ex = new ErrorYtDlp(Error, e.Message);
            if (!HayYtDlp) _ytDlpListo.TrySetException(ex);
            if (!HayFfmpeg || !HayYtDlp) _ffmpegListo.TrySetException(ex);
        }
        finally { Ocupado = false; Progreso = 0; }
    }

    async Task Bajar(string nombre, string url, string? destino, Action<string>? alTerminarZip)
    {
        Mensaje = $"Descargando {nombre}…";
        Progreso = 0;
        var archivo = destino ?? Path.Combine(Marca.DirComponentes, Path.GetFileName(new Uri(url).AbsolutePath));
        await Red.Archivo(url, archivo, (b, t) =>
        {
            Progreso = t > 0 ? (double)b / t : 0;
            Mensaje = t > 0 ? $"Descargando {nombre}… {Texto.Bytes(b)} de {Texto.Bytes(t)}" : $"Descargando {nombre}… {Texto.Bytes(b)}";
        });
        if (alTerminarZip != null)
        {
            Mensaje = $"Instalando {nombre}…";
            try { alTerminarZip(archivo); }
            finally { try { File.Delete(archivo); } catch { } }
        }
    }

    static void Extraer(string zip, string terminaEn, string destino)
    {
        using var z = ZipFile.OpenRead(zip);
        var e = z.Entries.FirstOrDefault(x => x.FullName.Replace('\\', '/').EndsWith(terminaEn, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"{terminaEn} no está en {Path.GetFileName(zip)}");
        e.ExtractToFile(destino + ".tmp", true);
        File.Move(destino + ".tmp", destino, true);
    }

    /// <summary>Actualiza yt-dlp (YouTube cambia seguido). Automático una vez por semana.</summary>
    public async Task<string> ActualizarYtDlp()
    {
        if (!HayYtDlp) { await Instalar(); return "Componentes instalados"; }
        Ocupado = true; Mensaje = "Buscando actualizaciones de yt-dlp…";
        try
        {
            var r = await Proceso.Ejecutar(YtDlp, new[] { "-U" }, TimeSpan.FromMinutes(3));
            Almacen.Ajustes.UltimaActualizacion = DateTime.Now;
            var salida = (r.Salida + r.Errores).Trim();
            var ultima = salida.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
            Mensaje = ultima.Contains("up to date", StringComparison.OrdinalIgnoreCase) ? "yt-dlp ya está al día"
                    : ultima.Contains("Updated", StringComparison.OrdinalIgnoreCase) ? "yt-dlp se actualizó" : ultima;
            Avisar(nameof(Version));
            return Mensaje;
        }
        catch (Exception e) { Marca.Registrar(e); return Mensaje = "No se pudo actualizar yt-dlp"; }
        finally { Ocupado = false; }
    }

    public string Version
    {
        get
        {
            var partes = new List<string>();
            partes.Add(HayYtDlp ? "yt-dlp " + (Leer(YtDlp, "--version") ?? "instalado") : "yt-dlp: falta");
            partes.Add(HayFfmpeg ? "FFmpeg instalado" : "FFmpeg: falta");
            partes.Add(HayDeno ? (Leer(Deno, "--version")?.Split('\n')[0] ?? "Deno instalado") : "Deno: falta");
            return string.Join("   ·   ", partes);
        }
    }

    static string? Leer(string exe, string arg)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, arg) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var s = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return s.Trim();
        }
        catch { return null; }
    }
}
