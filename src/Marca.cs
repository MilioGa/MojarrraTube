using System.IO;

namespace MojarraTube;

/// <summary>Nombre, versión y carpetas de la app en un solo lugar.</summary>
public static class Marca
{
    public const string Nombre = "MojarraTube";
    public static string Version => typeof(Marca).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Documentos\MojarraTube: ajustes, historial, suscripciones, descargas.</summary>
    public static string DirDatos { get; private set; } = "";
    /// <summary>%LOCALAPPDATA%\MojarraTube: componentes (yt-dlp, ffmpeg, deno) y caché de miniaturas.</summary>
    public static string DirLocal { get; private set; } = "";
    public static string DirComponentes => Path.Combine(DirLocal, "componentes");
    public static string DirCache => Path.Combine(DirLocal, "cache");

    public static void Iniciar()
    {
        DirDatos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Nombre);
        DirLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Nombre);
        Directory.CreateDirectory(DirLocal);
        try { Directory.CreateDirectory(DirDatos); }
        catch (Exception e) { Registrar(e); DirDatos = Path.Combine(DirLocal, "datos"); Directory.CreateDirectory(DirDatos); }
        Directory.CreateDirectory(DirComponentes);
        Directory.CreateDirectory(DirCache);
    }

    /// <summary>%LOCALAPPDATA%\MojarraTube\errores.log (siempre existe, aunque Documentos esté en OneDrive o falle).</summary>
    public static string ArchivoErrores => Path.Combine(DirLocal.Length > 0 ? DirLocal
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Nombre), "errores.log");

    public static void Registrar(object? e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ArchivoErrores)!);
            File.AppendAllText(ArchivoErrores, $"[{DateTime.Now:s}] v{Version} {e}\n");
        }
        catch { }
    }
}
