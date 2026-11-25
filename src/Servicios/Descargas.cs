using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MojarraTube;

/// <summary>Una descarga en la lista (con su progreso en vivo).</summary>
public class Descarga : Notificador
{
    public RegistroDescarga R { get; }
    public Descarga(RegistroDescarga r) { R = r; }

    internal CancellationTokenSource? Cts;
    internal bool PausaPedida;

    double _fraccion; string _detalle = ""; bool _indeterminado;
    public double Fraccion { get => _fraccion; set => Poner(ref _fraccion, value); }
    public bool Indeterminado { get => _indeterminado; set => Poner(ref _indeterminado, value); }
    public string Detalle { get => _detalle; set => Poner(ref _detalle, value); }

    public string Titulo => R.Titulo;
    public string Miniatura => R.Miniatura;
    public string Canal => R.Canal;
    public EstadoDescarga Estado
    {
        get => R.Estado;
        set
        {
            if (R.Estado == value) return;
            R.Estado = value;
            Avisar(); Avisar(nameof(EstadoTexto)); Avisar(nameof(Activa)); Avisar(nameof(Terminada)); Avisar(nameof(ConError));
            Avisar(nameof(PuedePausar)); Avisar(nameof(PuedeReanudar)); Avisar(nameof(Icono));
        }
    }
    public bool Activa => R.Estado is EstadoDescarga.Descargando or EstadoDescarga.Preparando or EstadoDescarga.Procesando or EstadoDescarga.EnCola;
    public bool Terminada => R.Estado == EstadoDescarga.Terminada;
    public bool ConError => R.Estado is EstadoDescarga.Error;
    public bool PuedePausar => R.Estado is EstadoDescarga.Descargando or EstadoDescarga.Preparando or EstadoDescarga.EnCola;
    public bool PuedeReanudar => R.Estado is EstadoDescarga.Pausada or EstadoDescarga.Error or EstadoDescarga.Cancelada;
    public string Icono => R.Modo == "audio" ? "" : "";
    public string Formato => R.Modo == "audio"
        ? R.FormatoAudio.ToUpperInvariant() + (R.FormatoAudio == "mp3" && R.Calidad > 0 ? $" · {R.Calidad} kbps" : "")
        : "MP4 · " + YtDlpDatos.NombreCalidad(R.Calidad) + (R.Calidad == 0 ? "" : "");
    public string EstadoTexto => R.Estado switch
    {
        EstadoDescarga.EnCola => "En cola",
        EstadoDescarga.Preparando => "Preparando…",
        EstadoDescarga.Descargando => "Descargando",
        EstadoDescarga.Procesando => R.Modo == "audio" ? "Convirtiendo audio…" : "Uniendo video y audio…",
        EstadoDescarga.Pausada => "En pausa",
        EstadoDescarga.Terminada => "Lista" + (R.Tamano > 0 ? " · " + Texto.Bytes(R.Tamano) : ""),
        EstadoDescarga.Error => R.Error ?? "Error",
        EstadoDescarga.Cancelada => "Cancelada",
        _ => "",
    };
    internal void Refrescar() { Avisar(nameof(EstadoTexto)); Avisar(nameof(Titulo)); Avisar(nameof(Miniatura)); }
}

/// <summary>Cola de descargas con yt-dlp: varias a la vez, pausar, reanudar, cancelar y reintentar.</summary>
public class GestorDescargas : Notificador
{
    public static GestorDescargas Instancia { get; } = new();
    public ObservableCollection<Descarga> Lista { get; } = new();

    public event Action<Descarga>? Terminada;
    public event Action<Descarga>? Fallo;

    int _activas;
    public int Activas { get => _activas; private set { if (Poner(ref _activas, value)) Avisar(nameof(HayActivas)); } }
    public bool HayActivas => _activas > 0;

    public void Cargar()
    {
        foreach (var r in Almacen.Descargas) Lista.Add(new Descarga(r));
        ContarActivas();
    }

    void Guardar() { Almacen.Guardar("descargas"); ContarActivas(); }
    void ContarActivas() => Activas = Lista.Count(d => d.Activa);

    public Descarga Agregar(RegistroDescarga r)
    {
        Almacen.Descargas.Insert(0, r);
        var d = new Descarga(r);
        Lista.Insert(0, d);
        Guardar();
        Siguiente();
        return d;
    }

    /// <summary>Arranca las descargas en cola hasta el límite de descargas simultáneas.</summary>
    void Siguiente()
    {
        var limite = Math.Clamp(Almacen.Ajustes.DescargasSimultaneas, 1, 6);
        var corriendo = Lista.Count(d => d.Estado is EstadoDescarga.Descargando or EstadoDescarga.Preparando or EstadoDescarga.Procesando);
        foreach (var d in Lista.Where(d => d.Estado == EstadoDescarga.EnCola).Reverse().ToList())
        {
            if (corriendo >= limite) break;
            corriendo++;
            _ = Ejecutar(d);
        }
        ContarActivas();
    }

    async Task Ejecutar(Descarga d)
    {
        var ui = Application.Current.Dispatcher;
        d.Estado = EstadoDescarga.Preparando;
        d.Indeterminado = true;
        d.Detalle = Herramientas.HayFfmpeg ? "Conectando…" : "Esperando componentes…";
        d.PausaPedida = false;
        d.R.Error = null;
        d.Cts = new CancellationTokenSource();
        var ct = d.Cts.Token;
        var errores = new List<string>();
        try
        {
            await Herramientas.Instancia.EsperarFfmpeg(ct);
            Directory.CreateDirectory(d.R.Carpeta);
            var args = YtDlpDatos.ArgsDescarga(d.R, Almacen.Ajustes, Herramientas.Ffmpeg);
            var ultimo = 0.0;
            var r = await Proceso.Ejecutar(Herramientas.YtDlp, args, null, ct, linea =>
            {
                var p = YtDlpDatos.LeerProgreso(linea);
                if (p != null)
                {
                    var ahora = Environment.TickCount64;
                    if (ahora - ultimo < 200 && p.Fraccion < 1) return;
                    ultimo = ahora;
                    ui.BeginInvoke(() =>
                    {
                        if (d.Estado is EstadoDescarga.Preparando or EstadoDescarga.Procesando) d.Estado = EstadoDescarga.Descargando;
                        d.Indeterminado = p.Total <= 0;
                        d.Fraccion = p.Fraccion;
                        var partes = new List<string>();
                        if (p.Cantidad > 1) partes.Add($"Video {p.Indice} de {p.Cantidad}");
                        partes.Add(p.Total > 0 ? $"{Texto.Bytes(p.Descargado)} de {Texto.Bytes(p.Total)}" : Texto.Bytes(p.Descargado));
                        if (p.Velocidad > 0) partes.Add(Texto.Bytes(p.Velocidad) + "/s");
                        if (p.Eta is { } eta) partes.Add("faltan " + Texto.Tiempo(eta));
                        d.Detalle = string.Join("  ·  ", partes);
                        if (p.Fraccion >= 1) { d.Estado = EstadoDescarga.Procesando; d.Indeterminado = true; d.Detalle = ""; }
                    });
                    return;
                }
                if (YtDlpDatos.LeerArchivo(linea) is { } archivo)
                    ui.BeginInvoke(() => { d.R.Archivo = archivo; });
            }, e => { if (e.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) lock (errores) errores.Add(e); });

            if (r.Codigo == 0 || (d.R.EsLista && d.R.Archivo != null))
            {
                d.Estado = EstadoDescarga.Terminada;
                d.Fraccion = 1; d.Indeterminado = false; d.Detalle = "";
                try { if (d.R.Archivo != null && File.Exists(d.R.Archivo)) d.R.Tamano = new FileInfo(d.R.Archivo).Length; } catch { }
                if (d.R.EsLista && d.R.Archivo != null) d.R.Archivo = Path.GetDirectoryName(d.R.Archivo);
                d.Refrescar();
                Terminada?.Invoke(d);
            }
            else
            {
                d.R.Error = YtDlpDatos.Explicar(string.Join("\n", errores) + "\n" + r.Errores);
                d.Estado = EstadoDescarga.Error;
                d.Indeterminado = false; d.Detalle = "";
                Fallo?.Invoke(d);
            }
        }
        catch (OperationCanceledException)
        {
            d.Estado = d.PausaPedida ? EstadoDescarga.Pausada : EstadoDescarga.Cancelada;
            d.Indeterminado = false;
            if (!d.PausaPedida) { d.Detalle = ""; d.Fraccion = 0; BorrarParciales(d); }
            else d.Detalle = d.Fraccion > 0 ? $"{d.Fraccion:P0} descargado" : "";
        }
        catch (Exception e)
        {
            Marca.Registrar(e);
            d.R.Error = e is System.ComponentModel.Win32Exception ? "No se pudo abrir yt-dlp. Reinstala los componentes en Ajustes." : "Error inesperado: " + e.Message;
            d.Estado = EstadoDescarga.Error;
            d.Indeterminado = false;
            Fallo?.Invoke(d);
        }
        finally
        {
            d.Cts?.Dispose(); d.Cts = null;
            Guardar();
            Siguiente();
        }
    }

    public void Pausar(Descarga d)
    {
        if (d.Estado == EstadoDescarga.EnCola) { d.Estado = EstadoDescarga.Pausada; Guardar(); return; }
        d.PausaPedida = true;
        d.Cts?.Cancel();
    }

    public void Reanudar(Descarga d)
    {
        if (!d.PuedeReanudar) return;
        d.Estado = EstadoDescarga.EnCola;
        d.Detalle = "En cola";
        Guardar();
        Siguiente();
    }

    public void Cancelar(Descarga d)
    {
        if (d.Cts != null) { d.PausaPedida = false; d.Cts.Cancel(); }
        else if (d.Estado is EstadoDescarga.EnCola or EstadoDescarga.Pausada) { d.Estado = EstadoDescarga.Cancelada; BorrarParciales(d); Guardar(); }
    }

    public void Quitar(Descarga d)
    {
        if (d.Cts != null) Cancelar(d);
        Lista.Remove(d);
        Almacen.Descargas.Remove(d.R);
        Guardar();
    }

    /// <summary>Vuelve a poner una descarga quitada (para "Deshacer").</summary>
    public void Restaurar(Descarga d, int indice)
    {
        indice = Math.Clamp(indice, 0, Lista.Count);
        Lista.Insert(indice, d);
        Almacen.Descargas.Insert(Math.Clamp(indice, 0, Almacen.Descargas.Count), d.R);
        Guardar();
    }

    public void LimpiarTerminadas()
    {
        foreach (var d in Lista.Where(x => x.Estado is EstadoDescarga.Terminada or EstadoDescarga.Cancelada).ToList())
        {
            Lista.Remove(d);
            Almacen.Descargas.Remove(d.R);
        }
        Guardar();
    }

    public void PausarTodas() { foreach (var d in Lista.Where(x => x.PuedePausar).ToList()) Pausar(d); }
    public void ReanudarTodas() { foreach (var d in Lista.Where(x => x.Estado == EstadoDescarga.Pausada).ToList()) Reanudar(d); }

    /// <summary>Al cerrar la app: se detienen y quedan en pausa para seguir después.</summary>
    public void DetenerTodo()
    {
        foreach (var d in Lista.Where(x => x.Cts != null).ToList()) { d.PausaPedida = true; d.Cts?.Cancel(); d.R.Estado = EstadoDescarga.Pausada; }
    }

    static void BorrarParciales(Descarga d)
    {
        // Archivos .part / .ytdl de esta descarga (llevan el id del video entre corchetes)
        try
        {
            var id = Texto.IdYouTube(d.R.Url);
            if (id == null || !Directory.Exists(d.R.Carpeta)) return;
            foreach (var f in Directory.EnumerateFiles(d.R.Carpeta, $"*[{id}]*"))
                if (f.EndsWith(".part") || f.EndsWith(".ytdl") || f.Contains(".part-Frag") || f.Contains(".temp."))
                    File.Delete(f);
        }
        catch { }
    }

    public static void AbrirArchivo(string? ruta)
    {
        if (string.IsNullOrEmpty(ruta)) return;
        try { Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true }); } catch { }
    }

    public static void MostrarEnCarpeta(string? ruta)
    {
        if (string.IsNullOrEmpty(ruta)) return;
        try
        {
            if (File.Exists(ruta)) Process.Start("explorer.exe", $"/select,\"{ruta}\"");
            else if (Directory.Exists(ruta)) Process.Start("explorer.exe", $"\"{ruta}\"");
        }
        catch { }
    }
}
