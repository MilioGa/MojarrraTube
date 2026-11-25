using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MojarraTube;

/// <summary>
/// Carga de imágenes sin trabar la interfaz: &lt;Image local:Img.Fuente="{Binding Miniatura}"/&gt;.
/// Guarda en caché (memoria y disco) y aparece con un fundido suave.
/// Acepta URLs, rutas de imagen y "video:C:\ruta.mp4" (toma un fotograma con FFmpeg).
/// </summary>
public static class Img
{
    public static readonly DependencyProperty FuenteProperty = DependencyProperty.RegisterAttached(
        "Fuente", typeof(string), typeof(Img), new PropertyMetadata(null, (d, e) => _ = Cargar((Image)d, e.NewValue as string)));
    public static string? GetFuente(DependencyObject d) => (string?)d.GetValue(FuenteProperty);
    public static void SetFuente(DependencyObject d, string? v) => d.SetValue(FuenteProperty, v);

    public static readonly DependencyProperty AnchoProperty = DependencyProperty.RegisterAttached(
        "Ancho", typeof(int), typeof(Img), new PropertyMetadata(480));
    public static int GetAncho(DependencyObject d) => (int)d.GetValue(AnchoProperty);
    public static void SetAncho(DependencyObject d, int v) => d.SetValue(AnchoProperty, v);

    static readonly SemaphoreSlim Turnos = new(8);
    static readonly SemaphoreSlim TurnosFfmpeg = new(2);
    static readonly Dictionary<string, ImageSource> Memoria = new();
    static readonly LinkedList<string> Orden = new();
    static string DirImg => Path.Combine(Marca.DirCache, "img");

    static async Task Cargar(Image img, string? fuente)
    {
        img.Source = null;
        if (string.IsNullOrWhiteSpace(fuente)) return;
        var ancho = GetAncho(img);
        var clave = fuente + "|" + ancho;
        if (Memoria.TryGetValue(clave, out var enMemoria)) { img.Source = enMemoria; img.Opacity = 1; return; }
        img.Opacity = 0;
        try
        {
            var bmp = await Task.Run(() => Obtener(fuente, ancho));
            if (bmp == null || GetFuente(img) != fuente) return;
            Recordar(clave, bmp);
            img.Source = bmp;
            img.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase() });
        }
        catch (Exception e) { Marca.Registrar(e); }
    }

    static void Recordar(string clave, ImageSource s)
    {
        Memoria[clave] = s;
        Orden.AddLast(clave);
        while (Orden.Count > 400) { Memoria.Remove(Orden.First!.Value); Orden.RemoveFirst(); }
    }

    static async Task<BitmapSource?> Obtener(string fuente, int ancho)
    {
        byte[]? datos = null;
        if (fuente.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(DirImg);
            var archivo = Path.Combine(DirImg, Hash(fuente) + ".img");
            if (File.Exists(archivo)) datos = await File.ReadAllBytesAsync(archivo);
            else
            {
                await Turnos.WaitAsync();
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    datos = await Red.Cliente.GetByteArrayAsync(fuente, cts.Token);
                    // YouTube devuelve una imagen gris de 120x90 cuando no existe maxres: se intenta otra
                    if (datos.Length < 1500 && fuente.Contains("maxresdefault"))
                        datos = await Red.Cliente.GetByteArrayAsync(fuente.Replace("maxresdefault", "hqdefault"), cts.Token);
                    await File.WriteAllBytesAsync(archivo, datos);
                }
                catch { return null; }
                finally { Turnos.Release(); }
            }
        }
        else if (fuente.StartsWith("video:"))
        {
            var ruta = fuente[6..];
            var archivo = Path.Combine(DirImg, Hash(ruta + File.GetLastWriteTimeUtc(ruta).Ticks) + ".jpg");
            if (!File.Exists(archivo))
            {
                if (!Herramientas.HayFfmpeg) return null;
                await TurnosFfmpeg.WaitAsync();
                try
                {
                    Directory.CreateDirectory(DirImg);
                    // Un fotograma al 10 % del video (o a los 3 s si no se sabe la duración)
                    await Proceso.Ejecutar(Herramientas.Ffmpeg, new[] { "-y", "-loglevel", "error", "-ss", "3", "-i", ruta, "-frames:v", "1", "-vf", "scale=480:-2", archivo }, TimeSpan.FromSeconds(20));
                    if (!File.Exists(archivo))
                        await Proceso.Ejecutar(Herramientas.Ffmpeg, new[] { "-y", "-loglevel", "error", "-i", ruta, "-map", "0:v?", "-frames:v", "1", "-vf", "scale=480:-2", archivo }, TimeSpan.FromSeconds(20));
                }
                catch { }
                finally { TurnosFfmpeg.Release(); }
            }
            if (File.Exists(archivo)) datos = await File.ReadAllBytesAsync(archivo);
        }
        else if (File.Exists(fuente)) datos = await File.ReadAllBytesAsync(fuente);
        if (datos == null || datos.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = ancho;
            bmp.StreamSource = new MemoryStream(datos);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..24];

    public static void BorrarCache()
    {
        Memoria.Clear(); Orden.Clear();
        try { if (Directory.Exists(DirImg)) Directory.Delete(DirImg, true); } catch { }
    }

    public static long TamanoCache()
    {
        try { return Directory.Exists(DirImg) ? new DirectoryInfo(DirImg).EnumerateFiles().Sum(f => f.Length) : 0; } catch { return 0; }
    }
}
