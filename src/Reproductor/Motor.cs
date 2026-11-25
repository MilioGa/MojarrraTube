using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace MojarraTube;

public enum EstadoMotor { Detenido, Cargando, Reproduciendo, Pausado, Terminado, Error }

public record PistaMedio(int Id, string Nombre);

/// <summary>
/// Motor de reproducción propio basado en libVLC. Los fotogramas se dibujan en una imagen de WPF
/// (memoria compartida, sin copias), así los controles pueden ir encima del video con transparencias y animaciones.
/// </summary>
public sealed class Motor : Notificador, IDisposable
{
    static LibVLC? _vlc;
    static LibVLC Vlc => _vlc ??= CrearVlc();

    static LibVLC CrearVlc()
    {
        Core.Initialize(Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64"));
        return new LibVLC(
            "--no-video-title-show", "--no-osd", "--no-snapshot-preview", "--no-stats", "--quiet",
            "--network-caching=1500", "--file-caching=400", "--live-caching=1500",
            "--avcodec-hw=any", "--audio-time-stretch", "--no-lua", "--http-reconnect");
    }

    readonly MediaPlayer _mp;
    readonly Dispatcher _ui;
    Media? _media;
    readonly DispatcherTimer _reloj;

    // Delegados guardados en campos para que el recolector no los libere mientras VLC los usa
    readonly MediaPlayer.LibVLCVideoFormatCb _cbFormato;
    readonly MediaPlayer.LibVLCVideoCleanupCb _cbLimpiar;
    readonly MediaPlayer.LibVLCVideoLockCb _cbBloquear;
    readonly MediaPlayer.LibVLCVideoDisplayCb _cbMostrar;

    sealed class Lienzo
    {
        public IntPtr Seccion, Vista;
        public int Ancho, Alto;
        public InteropBitmap? Bmp;
    }
    Lienzo? _lienzo;
    int _pintando;

    public Motor()
    {
        _ui = Application.Current.Dispatcher;
        _mp = new MediaPlayer(Vlc) { EnableHardwareDecoding = true, EnableKeyInput = false, EnableMouseInput = false };
        _cbFormato = Formato; _cbLimpiar = Limpiar; _cbBloquear = Bloquear; _cbMostrar = Mostrar;
        _mp.SetVideoFormatCallbacks(_cbFormato, _cbLimpiar);
        _mp.SetVideoCallbacks(_cbBloquear, null, _cbMostrar);

        _mp.Playing += (_, _) => EnUi(() => { Estado = EstadoMotor.Reproduciendo; Error = null; ActualizarDuracion(); });
        _mp.Paused += (_, _) => EnUi(() => Estado = EstadoMotor.Pausado);
        _mp.Stopped += (_, _) => EnUi(() => { if (Estado is not EstadoMotor.Terminado and not EstadoMotor.Error) Estado = EstadoMotor.Detenido; });
        _mp.EndReached += (_, _) => EnUi(() => { Estado = EstadoMotor.Terminado; Terminado?.Invoke(); });
        _mp.EncounteredError += (_, _) => EnUi(() => { Estado = EstadoMotor.Error; Error ??= "No se pudo reproducir este video."; FalloReproduccion?.Invoke(); });
        _mp.Buffering += (_, e) => EnUi(() =>
        {
            Buffer = e.Cache;
            if (e.Cache < 100 && Estado is EstadoMotor.Reproduciendo or EstadoMotor.Cargando) Cargando = true;
            else if (e.Cache >= 100) Cargando = false;
        });
        _mp.LengthChanged += (_, _) => EnUi(ActualizarDuracion);
        _mp.ESAdded += (_, _) => EnUi(() => Avisar(nameof(Subtitulos)));
        _mp.Vout += (_, e) => EnUi(() => TieneVideo = e.Count > 0);

        _reloj = new DispatcherTimer(DispatcherPriority.Render, _ui) { Interval = TimeSpan.FromMilliseconds(200) };
        _reloj.Tick += (_, _) =>
        {
            if (_buscando) return;
            var t = _mp.Time;
            if (t >= 0) { _posicion = t / 1000.0; Avisar(nameof(Posicion)); }
        };
        _reloj.Start();
    }

    void EnUi(Action a) => _ui.BeginInvoke(a);

    // ───────────── Estado ─────────────
    EstadoMotor _estado = EstadoMotor.Detenido;
    public EstadoMotor Estado
    {
        get => _estado;
        private set
        {
            if (!Poner(ref _estado, value)) return;
            if (value != EstadoMotor.Cargando && value != EstadoMotor.Reproduciendo) Cargando = false;
            Avisar(nameof(Reproduciendo));
        }
    }
    public bool Reproduciendo => _estado is EstadoMotor.Reproduciendo or EstadoMotor.Cargando;

    bool _cargando; public bool Cargando { get => _cargando; private set => Poner(ref _cargando, value); }
    float _buffer; public float Buffer { get => _buffer; private set => Poner(ref _buffer, value); }
    string? _error; public string? Error { get => _error; set => Poner(ref _error, value); }
    bool _tieneVideo; public bool TieneVideo { get => _tieneVideo; private set => Poner(ref _tieneVideo, value); }
    ImageSource? _imagen; public ImageSource? Imagen { get => _imagen; private set => Poner(ref _imagen, value); }

    double _posicion; public double Posicion => _posicion;
    double _duracion; public double Duracion { get => _duracion; private set => Poner(ref _duracion, value); }
    bool _buscando;

    public event Action? Terminado;
    public event Action? FalloReproduccion;

    void ActualizarDuracion()
    {
        var l = _mp.Length;
        if (l > 0) Duracion = l / 1000.0;
        Avisar(nameof(PuedeBuscar));
    }
    public bool PuedeBuscar => _mp.IsSeekable || _duracion > 0;

    // ───────────── Abrir ─────────────
    /// <summary>Reproduce una URL o un archivo. <paramref name="audio"/>: pista de audio aparte (YouTube separa video y audio).</summary>
    public void Abrir(string fuente, string? audio = null, IDictionary<string, string>? cabeceras = null, double inicio = 0)
    {
        Estado = EstadoMotor.Cargando;
        Cargando = true;
        Error = null;
        TieneVideo = false;
        Duracion = 0; _posicion = inicio; Avisar(nameof(Posicion));
        var vlc = Vlc;
        EnSegundoPlano(() =>
        {
            var anterior = _media;
            Media m = File.Exists(fuente) ? new Media(vlc, fuente, FromType.FromPath) : new Media(vlc, new Uri(fuente));
            if (!string.IsNullOrEmpty(audio)) m.AddOption(":input-slave=" + audio);
            if (cabeceras != null)
            {
                if (cabeceras.TryGetValue("User-Agent", out var ua)) m.AddOption(":http-user-agent=" + ua);
                if (cabeceras.TryGetValue("Referer", out var re)) m.AddOption(":http-referrer=" + re);
            }
            if (inicio > 1) m.AddOption(":start-time=" + inicio.ToString("0.###", CultureInfo.InvariantCulture));
            _media = m;
            _mp.Play(m);
            _mp.SetRate((float)_velocidad);
            AplicarVolumen();
            anterior?.Dispose();
        });
    }

    public void Detener()
    {
        Estado = EstadoMotor.Detenido;
        EnSegundoPlano(() => { _mp.Stop(); _media?.Dispose(); _media = null; });
        Imagen = null;
        Duracion = 0; _posicion = 0; Avisar(nameof(Posicion));
    }

    // Las llamadas que pueden bloquear (Play/Stop) van en un solo hilo aparte, en orden
    Task _cadena = Task.CompletedTask;
    readonly object _cadenaCerrojo = new();
    void EnSegundoPlano(Action a)
    {
        lock (_cadenaCerrojo)
            _cadena = _cadena.ContinueWith(_ =>
            {
                try { a(); } catch (Exception e) { Marca.Registrar(e); EnUi(() => { Error = "No se pudo abrir: " + e.Message; Estado = EstadoMotor.Error; }); }
            }, TaskScheduler.Default);
    }

    // ───────────── Controles ─────────────
    public void Alternar()
    {
        if (_estado == EstadoMotor.Terminado) { Buscar(0); EnSegundoPlano(() => _mp.Play()); return; }
        if (_estado == EstadoMotor.Pausado) EnSegundoPlano(() => _mp.SetPause(false));
        else if (Reproduciendo) EnSegundoPlano(() => _mp.SetPause(true));
    }
    public void Reproducir() { if (_estado == EstadoMotor.Pausado) EnSegundoPlano(() => _mp.SetPause(false)); }
    public void Pausar() { if (Reproduciendo) EnSegundoPlano(() => _mp.SetPause(true)); }

    public void Buscar(double segundos)
    {
        if (_duracion > 0) segundos = Math.Clamp(segundos, 0, Math.Max(0, _duracion - 0.3));
        else segundos = Math.Max(0, segundos);
        _posicion = segundos; Avisar(nameof(Posicion));
        _buscando = true;
        var ms = (long)(segundos * 1000);
        EnSegundoPlano(() =>
        {
            if (_mp.State == VLCState.Ended) { _mp.Stop(); _mp.Play(); }
            _mp.Time = ms;
        });
        // Dejar que VLC alcance la nueva posición antes de leerla de nuevo
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        t.Tick += (_, _) => { t.Stop(); _buscando = false; };
        t.Start();
        if (_estado == EstadoMotor.Terminado) Estado = EstadoMotor.Pausado;
    }

    public void Saltar(double delta) => Buscar(_posicion + delta);

    double _volumen = 80;
    public double Volumen
    {
        get => _volumen;
        set { value = Math.Clamp(value, 0, 150); if (Poner(ref _volumen, value)) { if (value > 0 && _silencio) Silencio = false; AplicarVolumen(); } }
    }
    bool _silencio;
    public bool Silencio { get => _silencio; set { if (Poner(ref _silencio, value)) AplicarVolumen(); } }
    void AplicarVolumen()
    {
        var v = (int)Math.Round(_volumen); var s = _silencio;
        Task.Run(() => { try { _mp.Volume = v; _mp.Mute = s; } catch { } });
    }

    double _velocidad = 1;
    public double Velocidad
    {
        get => _velocidad;
        set { value = Math.Clamp(value, 0.25, 3); if (Poner(ref _velocidad, value)) { var r = (float)value; Task.Run(() => _mp.SetRate(r)); } }
    }

    public List<PistaMedio> Subtitulos =>
        _mp.SpuDescription.Select(d => new PistaMedio(d.Id, d.Id < 0 ? "Desactivados" : d.Name)).ToList();
    public int SubtituloActual => _mp.Spu;
    public void ElegirSubtitulo(int id) => Task.Run(() => _mp.SetSpu(id));
    public void AgregarSubtitulo(string ruta) => Task.Run(() => _mp.AddSlave(MediaSlaveType.Subtitle, new Uri(ruta).AbsoluteUri, true));

    public List<PistaMedio> PistasAudio =>
        _mp.AudioTrackDescription.Where(d => d.Id >= 0).Select(d => new PistaMedio(d.Id, d.Name)).ToList();
    public int AudioActual => _mp.AudioTrack;
    public void ElegirAudio(int id) => Task.Run(() => _mp.SetAudioTrack(id));

    /// <summary>Guarda el fotograma actual como PNG.</summary>
    public bool Captura(string ruta)
    {
        if (_lienzo?.Bmp is not { } b) return false;
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(b));
        using var f = File.Create(ruta);
        enc.Save(f);
        return true;
    }

    // ───────────── Video en memoria compartida ─────────────
    uint Formato(ref IntPtr opaque, IntPtr chroma, ref uint ancho, ref uint alto, ref uint pitches, ref uint lineas)
    {
        // RV32 = BGRA de 32 bits, lo que WPF dibuja directo (Bgr32)
        Marshal.Copy(Encoding.ASCII.GetBytes("RV32"), 0, chroma, 4);
        // Videos 4K se escalan a 1440p para no saturar la memoria; la pantalla rara vez lo nota
        double w = ancho, h = alto;
        var escala = Math.Min(1.0, Math.Min(2560.0 / w, 1440.0 / h));
        var aw = (uint)(Math.Round(w * escala / 2) * 2);
        var ah = (uint)(Math.Round(h * escala / 2) * 2);
        if (aw == 0 || ah == 0) { aw = ancho; ah = alto; }
        ancho = aw; alto = ah;
        pitches = ancho * 4;
        lineas = alto;
        var tam = (uint)(pitches * lineas);
        var l = new Lienzo { Ancho = (int)ancho, Alto = (int)alto };
        l.Seccion = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, 0x04 /*PAGE_READWRITE*/, 0, tam, null);
        l.Vista = MapViewOfFile(l.Seccion, 0xF001F /*FILE_MAP_ALL_ACCESS*/, 0, 0, (UIntPtr)tam);
        _lienzo = l;
        _ui.BeginInvoke(() =>
        {
            if (_lienzo != l) return;
            l.Bmp = (InteropBitmap)Imaging.CreateBitmapSourceFromMemorySection(l.Seccion, l.Ancho, l.Alto, PixelFormats.Bgr32, l.Ancho * 4, 0);
            Imagen = l.Bmp;
            TieneVideo = true;
        });
        return 1;
    }

    void Limpiar(ref IntPtr opaque)
    {
        var l = _lienzo;
        _lienzo = null;
        if (l == null) return;
        _ui.BeginInvoke(() =>
        {
            if (Imagen == l.Bmp && _lienzo == null) Imagen = null;
            l.Bmp = null;
            UnmapViewOfFile(l.Vista);
            CloseHandle(l.Seccion);
        });
    }

    IntPtr Bloquear(IntPtr opaque, IntPtr planos)
    {
        var l = _lienzo;
        Marshal.WriteIntPtr(planos, l?.Vista ?? IntPtr.Zero);
        return IntPtr.Zero;
    }

    void Mostrar(IntPtr opaque, IntPtr imagen)
    {
        // Un solo repintado pendiente a la vez: si la interfaz va atrasada, se salta fotogramas en vez de acumularlos
        if (Interlocked.Exchange(ref _pintando, 1) == 1) return;
        _ui.BeginInvoke(DispatcherPriority.Render, () =>
        {
            Interlocked.Exchange(ref _pintando, 0);
            _lienzo?.Bmp?.Invalidate();
        });
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFileMapping(IntPtr hFile, IntPtr lpAttributes, uint flProtect, uint dwMaximumSizeHigh, uint dwMaximumSizeLow, string? lpName);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    public void Dispose()
    {
        _reloj.Stop();
        try { _mp.Stop(); } catch { }
        _mp.Dispose();
        _media?.Dispose();
    }
}
