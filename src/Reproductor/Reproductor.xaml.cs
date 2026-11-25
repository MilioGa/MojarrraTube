using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MojarraTube;

public partial class Reproductor : UserControl
{
    public Motor Motor { get; }
    public Video? Video { get; private set; }
    public InfoVideo? Info { get; private set; }

    int _calidadPedida;          // alto máximo elegido (0 = la mejor)
    bool _soloAudio;
    Formato? _fVideo, _fAudio;
    bool _probadoAlterno;
    double _ultimoGuardado;
    readonly DispatcherTimer _ocultar;
    readonly Storyboard _giro;
    DispatcherTimer? _clicSimple;

    public event Action? PideSiguiente, PideAnterior, PidePantallaCompleta, PideMini, PideAmpliar, PideCerrar, Terminado;

    public Reproductor()
    {
        InitializeComponent();
        Motor = new Motor
        {
            Volumen = Almacen.Ajustes.Volumen,
            Silencio = Almacen.Ajustes.Silencio,
            Velocidad = 1,
        };
        Motor.PropertyChanged += Motor_Cambio;
        Motor.Terminado += () =>
        {
            if (Video != null) Almacen.GuardarPosicion(Video, 0, Motor.Duracion);
            MostrarControles(true);
            Terminado?.Invoke();
        };
        Motor.FalloReproduccion += Fallo;
        Vol.Value = Math.Min(100, Motor.Volumen);
        ActualizarVolumen();

        Barra.Buscar += s => { Motor.Buscar(s); MostrarControles(); };
        Barra.Previsualizar += s => TiempoTexto.Text = $"{Texto.Tiempo(s)} / {Texto.Tiempo(Motor.Duracion)}";

        _ocultar = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _ocultar.Tick += (_, _) => { _ocultar.Stop(); if (Motor.Reproduciendo && !Barra.IsMouseCaptured && !MenuAbierto) OcultarControles(); };

        _giro = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9));
        Storyboard.SetTarget(anim, Carga);
        Storyboard.SetTargetProperty(anim, new PropertyPath("Children[1].RenderTransform.Angle"));
        _giro.Children.Add(anim);

        MouseMove += (_, _) => MostrarControles();
        MouseLeave += (_, _) => { if (Motor.Reproduciendo) _ocultar.Start(); };
        MouseWheel += (_, e) => { if (!Compacto) { CambiarVolumen(e.Delta > 0 ? 5 : -5); e.Handled = true; } };
        Raiz.MouseLeftButtonDown += Raiz_Clic;
        ActualizarModo();
    }

    bool MenuAbierto => ContextMenu?.IsOpen == true || _menu?.IsOpen == true;

    // ───────────── Cargar ─────────────
    public void Cargar(Video v, InfoVideo? info, double inicio = 0)
    {
        GuardarPosicion(true);
        Video = v;
        Info = info;
        _probadoAlterno = false;
        _soloAudio = v.SoloAudio;
        _calidadPedida = Almacen.Ajustes.CalidadReproduccion;
        PanelError.Visibility = Visibility.Collapsed;
        TituloArriba.Text = v.Titulo;
        Barra.Capitulos = info?.Capitulos ?? new();
        CapituloTexto.Text = "";
        var portada = v.EsLocal ? "video:" + v.Ruta : (info?.Video.Miniatura is { Length: > 0 } m ? m : v.Miniatura);
        Img.SetFuente(Portada, portada);
        Img.SetFuente(Fondo, portada);
        Portada.Visibility = Visibility.Visible;
        Lienzo.Source = null;
        if (inicio <= 0) inicio = Text_InicioGuardado(v);
        if (v.EsLocal)
        {
            EtiquetaCalidad.Visibility = Visibility.Collapsed;
            Motor.Abrir(v.Ruta!, null, null, inicio);
        }
        else Reproducir(inicio);
        MostrarControles(true);
        Focus();
    }

    static double Text_InicioGuardado(Video v) => Almacen.Ajustes.RecordarPosicion ? Almacen.Posicion(v) : 0;

    void Reproducir(double inicio)
    {
        if (Info == null) return;
        var formatos = Info.Formatos;
        if (_soloAudio)
        {
            var a = YtDlpDatos.MejorAudio(formatos);
            (_fVideo, _fAudio) = (a, null);
        }
        else (_fVideo, _fAudio) = YtDlpDatos.ElegirReproduccion(formatos, _calidadPedida);
        if (_fVideo == null && _fAudio != null) (_fVideo, _fAudio) = (_fAudio, null);
        if (_fVideo == null)
        {
            MostrarError("No se encontró un formato que se pueda reproducir en este enlace.");
            return;
        }
        ActualizarEtiquetaCalidad();
        var cab = _fVideo.Cabeceras.Count > 0 ? _fVideo.Cabeceras : _fAudio?.Cabeceras;
        Motor.Abrir(_fVideo.Url, _fAudio?.Url, cab, inicio);
    }

    void ActualizarEtiquetaCalidad()
    {
        var alto = _fVideo != null && _fVideo.TieneVideo ? YtDlpDatos.NormalizarAlto(_fVideo.Alto, _fVideo.Ancho) : 0;
        EtiquetaCalidad.Visibility = _soloAudio || alto >= 720 ? Visibility.Visible : Visibility.Collapsed;
        EtiquetaCalidadTexto.Text = _soloAudio ? "AUDIO" : alto >= 2160 ? "4K" : alto >= 1440 ? "2K" : "HD";
    }

    /// <summary>Si falla (p. ej. YouTube rechaza el video por separado), se prueba con un formato completo de menor calidad.</summary>
    void Fallo()
    {
        if (Video != null && !Video.EsLocal && Info != null && !_probadoAlterno)
        {
            _probadoAlterno = true;
            var completo = Info.Formatos.Where(f => f.TieneVideo && f.TieneAudio && f.Url.Length > 0 && (f.EsDirecto || f.EsHls))
                                        .OrderByDescending(f => f.EsDirecto).ThenByDescending(f => f.Alto).FirstOrDefault();
            if (completo != null && completo != _fVideo)
            {
                (_fVideo, _fAudio) = (completo, null);
                ActualizarEtiquetaCalidad();
                Motor.Abrir(completo.Url, null, completo.Cabeceras, Motor.Posicion);
                return;
            }
        }
        MostrarError(Video?.EsLocal == true
            ? "No se pudo reproducir este archivo. Puede estar dañado o usar un formato no compatible."
            : "No se pudo reproducir este video. Vuelve a intentarlo; si sigue fallando, actualiza los componentes en Ajustes.");
    }

    void MostrarError(string texto)
    {
        ErrorTexto.Text = texto;
        PanelError.Visibility = Visibility.Visible;
        BtnNavegadorError.Visibility = Video?.EsLocal == true ? Visibility.Collapsed : Visibility.Visible;
        Carga.Visibility = Visibility.Collapsed;
        _giro.Stop();
    }

    Action? _recargar;

    /// <summary>Muestra la portada y el indicador de carga mientras se obtiene la información del video.</summary>
    public void Preparar(Video v, Action? recargar)
    {
        _recargar = recargar;
        if (Video?.Clave != v.Clave || Info == null)
        {
            GuardarPosicion(true);
            Motor.Detener();
            Info = null;
        }
        Video = v;
        TituloArriba.Text = v.Titulo;
        PanelError.Visibility = Visibility.Collapsed;
        var portada = v.EsLocal ? "video:" + v.Ruta : v.Miniatura;
        Img.SetFuente(Portada, portada);
        Img.SetFuente(Fondo, portada);
        Portada.Visibility = Visibility.Visible;
        Lienzo.Source = null;
        Carga.Visibility = Visibility.Visible;
        _giro.Begin();
        MostrarControles(true);
    }

    public void MostrarFallo(string mensaje, Action reintentar)
    {
        _recargar = reintentar;
        MostrarError(mensaje);
    }

    void Reintentar_Click(object s, RoutedEventArgs e)
    {
        PanelError.Visibility = Visibility.Collapsed;
        _probadoAlterno = false;
        if (Video?.EsLocal == true) Motor.Abrir(Video.Ruta!, null, null, Motor.Posicion);
        else if (_recargar != null) { var pos = Motor.Posicion; Info = null; _recargar(); }
    }

    void Navegador_Click(object s, RoutedEventArgs e)
    {
        if (Video == null) return;
        Motor.Pausar();
        try { Process.Start(new ProcessStartInfo(Video.UrlCompleta) { UseShellExecute = true }); } catch { }
    }

    public void Detener()
    {
        GuardarPosicion(true);
        Motor.Detener();
        Video = null; Info = null;
        Lienzo.Source = null;
        Img.SetFuente(Portada, null);
        Img.SetFuente(Fondo, null);
    }

    public void GuardarPosicion(bool forzar = false)
    {
        if (Video == null || Motor.Duracion <= 0 || Video.EnVivo) return;
        if (!forzar && Math.Abs(Motor.Posicion - _ultimoGuardado) < 5) return;
        _ultimoGuardado = Motor.Posicion;
        Almacen.GuardarPosicion(Video, Motor.Posicion, Motor.Duracion);
        Video.Progreso = Motor.Duracion > 0 ? Motor.Posicion / Motor.Duracion : 0;
    }

    // ───────────── Reacción al motor ─────────────
    void Motor_Cambio(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Motor.Posicion):
            case nameof(Motor.Duracion):
                var dur = Motor.Duracion; var pos = Motor.Posicion;
                var cargado = dur > 0 && Motor.Buffer < 100 ? pos / dur : (dur > 0 ? Math.Min(1, (pos + 30) / dur) : 0);
                Barra.Actualizar(pos, dur, cargado);
                TiempoTexto.Text = Video?.EnVivo == true && dur <= 0 ? "● EN VIVO" : dur > 0 ? $"{Texto.Tiempo(pos)} / {Texto.Tiempo(dur)}" : Texto.Tiempo(pos);
                var cap = Info?.Capitulos.LastOrDefault(c => c.Inicio <= pos);
                CapituloTexto.Text = cap != null ? "•  " + cap.Titulo : "";
                if (Motor.Reproduciendo) GuardarPosicion();
                break;
            case nameof(Motor.Imagen):
                Lienzo.Source = Motor.Imagen;
                break;
            case nameof(Motor.TieneVideo):
                Portada.Visibility = Motor.TieneVideo ? Visibility.Collapsed : Visibility.Visible;
                break;
            case nameof(Motor.Estado):
            case nameof(Motor.Cargando):
                var cargando = Motor.Estado == EstadoMotor.Cargando || Motor.Cargando;
                if (cargando && Carga.Visibility != Visibility.Visible) { Carga.Visibility = Visibility.Visible; _giro.Begin(); }
                else if (!cargando && Carga.Visibility == Visibility.Visible) { Carga.Visibility = Visibility.Collapsed; _giro.Stop(); }
                var icono = Motor.Estado == EstadoMotor.Terminado ? "" : Motor.Reproduciendo ? "" : "";
                BtnPlay.Content = icono; CentroMini.Content = icono;
                BtnPlay.ToolTip = Motor.Reproduciendo ? "Pausar (K)" : "Reproducir (K)";
                if (Motor.Estado == EstadoMotor.Reproduciendo) PanelError.Visibility = Visibility.Collapsed;
                if (!Motor.Reproduciendo) MostrarControles(true);
                else _ocultar.Start();
                if (Motor.Estado == EstadoMotor.Pausado) GuardarPosicion(true);
                break;
        }
    }

    // ───────────── Controles visibles / ocultos ─────────────
    bool _controlesVisibles = true;
    void MostrarControles(bool fijar = false)
    {
        if (!_controlesVisibles)
        {
            _controlesVisibles = true;
            Controles.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            Controles.IsHitTestVisible = true;
            Cursor = null;
        }
        _ocultar.Stop();
        if (!fijar && Motor.Reproduciendo) _ocultar.Start();
    }

    void OcultarControles()
    {
        if (!_controlesVisibles) return;
        _controlesVisibles = false;
        Controles.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
        Controles.IsHitTestVisible = false;
        Barra.Ocultar();
        if (EnPantallaCompleta) Cursor = Cursors.None;
    }

    // ───────────── Modos: normal, mini, pantalla completa ─────────────
    bool _compacto, _completa;
    public bool Compacto { get => _compacto; set { _compacto = value; ActualizarModo(); } }
    public bool EnPantallaCompleta { get => _completa; set { _completa = value; ActualizarModo(); } }
    bool _haySig, _hayAnt;
    public bool HaySiguiente { get => _haySig; set { _haySig = value; ActualizarModo(); } }
    public bool HayAnterior { get => _hayAnt; set { _hayAnt = value; ActualizarModo(); } }

    void ActualizarModo()
    {
        Arriba.Visibility = _compacto || _completa ? Visibility.Visible : Visibility.Collapsed;
        BotonesMini.Visibility = _compacto ? Visibility.Visible : Visibility.Collapsed;
        CentroMini.Visibility = _compacto ? Visibility.Visible : Visibility.Collapsed;
        FilaBotones.Visibility = _compacto ? Visibility.Collapsed : Visibility.Visible;
        Arriba.Padding = _compacto ? new Thickness(12, 6, 4, 26) : new Thickness(18, 14, 10, 40);
        TituloArriba.FontSize = _compacto ? 13 : 17;
        Abajo.Padding = _compacto ? new Thickness(8, 20, 8, 4) : new Thickness(14, 36, 14, 8);
        BtnCompleta.Content = _completa ? "" : "";
        BtnCompleta.ToolTip = _completa ? "Salir de pantalla completa (F)" : "Pantalla completa (F)";
        BtnMini.Visibility = _completa ? Visibility.Collapsed : Visibility.Visible;
        BtnSiguiente.Visibility = _haySig ? Visibility.Visible : Visibility.Collapsed;
        BtnAnterior.Visibility = _hayAnt ? Visibility.Visible : Visibility.Collapsed;
        if (!_completa) Cursor = null;
    }

    // ───────────── Clics ─────────────
    void Raiz_Clic(object s, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && EsDeControl(d)) return;
        Focus();
        if (Compacto) { MostrarControles(); return; }
        if (e.ClickCount == 2)
        {
            _clicSimple?.Stop();
            PidePantallaCompleta?.Invoke();
            return;
        }
        // Un clic: pausar/reproducir (se espera un poco por si es doble clic)
        _clicSimple?.Stop();
        _clicSimple = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(230) };
        _clicSimple.Tick += (_, _) => { _clicSimple.Stop(); Alternar(); };
        _clicSimple.Start();
    }

    bool EsDeControl(DependencyObject d)
    {
        while (d != null && d != Raiz)
        {
            if (d is ButtonBase || d is BarraTiempo || d is Slider || d == PanelError) return true;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    public void Alternar()
    {
        if (Motor.Estado == EstadoMotor.Error) return;
        var ibaA = !Motor.Reproduciendo;
        Motor.Alternar();
        Destellar(ibaA ? "" : "");
    }

    void Alternar_Click(object s, RoutedEventArgs e) => Alternar();
    void Siguiente_Click(object s, RoutedEventArgs e) => PideSiguiente?.Invoke();
    void Anterior_Click(object s, RoutedEventArgs e) => PideAnterior?.Invoke();
    void Completa_Click(object s, RoutedEventArgs e) => PidePantallaCompleta?.Invoke();
    void Mini_Click(object s, RoutedEventArgs e) => PideMini?.Invoke();
    void Ampliar_Click(object s, RoutedEventArgs e) => PideAmpliar?.Invoke();
    void CerrarMini_Click(object s, RoutedEventArgs e) => PideCerrar?.Invoke();

    void Destellar(string icono)
    {
        DestelloIcono.Text = icono;
        var dur = TimeSpan.FromMilliseconds(500);
        Destello.BeginAnimation(OpacityProperty, new DoubleAnimation(0.95, 0, dur) { EasingFunction = new CubicEase() });
        DestelloEscala.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.7, 1.25, dur) { EasingFunction = new CubicEase() });
        DestelloEscala.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.7, 1.25, dur) { EasingFunction = new CubicEase() });
    }

    void Avisar(string texto)
    {
        AvisoTexto.Text = texto;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200))));
        Aviso.BeginAnimation(OpacityProperty, a);
    }

    // ───────────── Volumen ─────────────
    bool _ajustandoVol;
    void Vol_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ajustandoVol || Motor == null) return;
        Motor.Volumen = e.NewValue;
        if (e.NewValue > 0) Motor.Silencio = false;
        ActualizarVolumen();
        GuardarVolumen();
    }

    void CambiarVolumen(double delta)
    {
        Motor.Volumen = Math.Clamp(Motor.Volumen + delta, 0, 100);
        if (Motor.Volumen > 0) Motor.Silencio = false;
        _ajustandoVol = true; Vol.Value = Motor.Volumen; _ajustandoVol = false;
        ActualizarVolumen();
        GuardarVolumen();
        Avisar($"Volumen {Motor.Volumen:0} %");
        MostrarControles();
    }

    void Silencio_Click(object s, RoutedEventArgs e) => AlternarSilencio();
    void AlternarSilencio()
    {
        Motor.Silencio = !Motor.Silencio;
        ActualizarVolumen();
        GuardarVolumen();
        Avisar(Motor.Silencio ? "Silenciado" : $"Volumen {Motor.Volumen:0} %");
    }

    void ActualizarVolumen()
    {
        BtnVolumen.Content = Motor.Silencio || Motor.Volumen == 0 ? "" : Motor.Volumen < 34 ? "" : Motor.Volumen < 67 ? "" : "";
        BtnVolumen.ToolTip = Motor.Silencio ? "Activar sonido (M)" : "Silenciar (M)";
    }

    void GuardarVolumen()
    {
        Almacen.Ajustes.Volumen = Motor.Volumen;
        Almacen.Ajustes.Silencio = Motor.Silencio;
        Almacen.Guardar("ajustes");
    }

    void Volumen_Enter(object s, MouseEventArgs e) => Vol.BeginAnimation(WidthProperty, new DoubleAnimation(80, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase() });
    void Volumen_Leave(object s, MouseEventArgs e)
    {
        if (Vol.IsMouseCaptureWithin) return;
        Vol.BeginAnimation(WidthProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase() });
    }

    // ───────────── Menús: calidad, velocidad, subtítulos ─────────────
    ContextMenu? _menu;
    void AbrirMenu(ContextMenu m, UIElement junto)
    {
        _menu = m;
        m.PlacementTarget = junto;
        m.Placement = PlacementMode.Top;
        m.Closed += (_, _) => { if (Motor.Reproduciendo) _ocultar.Start(); };
        m.IsOpen = true;
        MostrarControles(true);
    }

    static MenuItem Item(string texto, Action alClic, bool marcado = false, string? icono = null, string? derecha = null)
    {
        var mi = new MenuItem { Header = texto, Tag = marcado ? "" : icono ?? "", InputGestureText = derecha ?? "" };
        mi.Click += (_, _) => alClic();
        return mi;
    }

    void Ajustes_Click(object s, RoutedEventArgs e)
    {
        var m = new ContextMenu();
        if (Video?.EsLocal != true && Info != null)
        {
            var actual = _soloAudio ? "Solo audio" : _fVideo is { TieneVideo: true } f ? YtDlpDatos.NombreCalidad(YtDlpDatos.NormalizarAlto(f.Alto, f.Ancho)) : "Automática";
            m.Items.Add(Item("Calidad", MenuCalidad, icono: "", derecha: actual + "  ›"));
        }
        m.Items.Add(Item("Velocidad", MenuVelocidad, icono: "", derecha: (Motor.Velocidad == 1 ? "Normal" : Motor.Velocidad + "×") + "  ›"));
        var pistas = Motor.PistasAudio;
        if (pistas.Count > 1) m.Items.Add(Item("Pista de audio", MenuAudio, icono: "", derecha: "›"));
        if (Video?.EsLocal == true) m.Items.Add(Item("Cargar subtítulos…", CargarSubtitulos, icono: ""));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Guardar captura", Captura, icono: ""));
        AbrirMenu(m, BtnAjustes);
    }

    void MenuCalidad()
    {
        if (Info == null) return;
        var m = new ContextMenu();
        var alturas = YtDlpDatos.Calidades(Info.Formatos);
        var actual = _fVideo is { TieneVideo: true } f ? YtDlpDatos.NormalizarAlto(f.Alto, f.Ancho) : -1;
        foreach (var a in alturas)
        {
            var alto = a;
            m.Items.Add(Item(YtDlpDatos.NombreCalidad(alto), () => CambiarCalidad(alto, false), !_soloAudio && actual == alto));
        }
        if (YtDlpDatos.MejorAudio(Info.Formatos) != null)
        {
            m.Items.Add(new Separator());
            m.Items.Add(Item("Solo audio (ahorra datos)", () => CambiarCalidad(0, true), _soloAudio));
        }
        AbrirMenu(m, BtnAjustes);
    }

    void CambiarCalidad(int alto, bool soloAudio)
    {
        _calidadPedida = alto;
        _soloAudio = soloAudio;
        _probadoAlterno = false;
        var pos = Motor.Posicion;
        Reproducir(pos);
        Avisar(soloAudio ? "Solo audio" : YtDlpDatos.NombreCalidad(alto));
    }

    void MenuVelocidad()
    {
        var m = new ContextMenu();
        foreach (var v in new[] { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 3 })
        {
            var vel = v;
            m.Items.Add(Item(vel == 1 ? "Normal" : vel + "×", () => { Motor.Velocidad = vel; Avisar(vel == 1 ? "Velocidad normal" : $"Velocidad {vel}×"); }, Math.Abs(Motor.Velocidad - vel) < 0.01));
        }
        AbrirMenu(m, BtnAjustes);
    }

    void MenuAudio()
    {
        var m = new ContextMenu();
        var actual = Motor.AudioActual;
        foreach (var p in Motor.PistasAudio) { var id = p.Id; m.Items.Add(Item(p.Nombre, () => Motor.ElegirAudio(id), id == actual)); }
        AbrirMenu(m, BtnAjustes);
    }

    void Subtitulos_Click(object s, RoutedEventArgs e)
    {
        var m = new ContextMenu();
        var actual = Motor.SubtituloActual;
        foreach (var p in Motor.Subtitulos) { var id = p.Id; m.Items.Add(Item(p.Nombre, () => Motor.ElegirSubtitulo(id), id == actual)); }
        if (Video?.EsLocal == true) { m.Items.Add(new Separator()); m.Items.Add(Item("Cargar archivo de subtítulos…", CargarSubtitulos, icono: "")); }
        AbrirMenu(m, BtnSubtitulos);
    }

    void AlternarSubtitulos()
    {
        var subs = Motor.Subtitulos.Where(x => x.Id >= 0).ToList();
        if (subs.Count == 0) { Avisar("Este video no tiene subtítulos"); return; }
        if (Motor.SubtituloActual >= 0) { Motor.ElegirSubtitulo(-1); Avisar("Subtítulos desactivados"); }
        else { Motor.ElegirSubtitulo(subs[0].Id); Avisar("Subtítulos: " + subs[0].Nombre); }
    }

    void CargarSubtitulos()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = "Elegir subtítulos", Filter = "Subtítulos|*.srt;*.vtt;*.ass;*.ssa;*.sub|Todos los archivos|*.*" };
        if (d.ShowDialog() == true) { Motor.AgregarSubtitulo(d.FileName); Avisar("Subtítulos cargados"); }
    }

    void Captura()
    {
        var carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Marca.Nombre);
        Directory.CreateDirectory(carpeta);
        var nombre = Texto.NombreArchivo(Video?.Titulo ?? "captura") + $" {Texto.Tiempo(Motor.Posicion).Replace(':', '.')}.png";
        var ruta = Path.Combine(carpeta, nombre);
        if (Motor.Captura(ruta))
            App.Ventana?.Aviso("Captura guardada en Imágenes › MojarraTube", "", ("Mostrar", () => GestorDescargas.MostrarEnCarpeta(ruta)));
        else Avisar("No hay imagen para capturar");
    }

    public void ActualizarSubtitulosVisibles() =>
        BtnSubtitulos.Visibility = Motor.Subtitulos.Any(x => x.Id >= 0) || Video?.EsLocal == true ? Visibility.Visible : Visibility.Collapsed;

    // ───────────── Teclado ─────────────
    /// <summary>Atajos del reproductor (iguales a los de YouTube). Devuelve true si la tecla se usó.</summary>
    public bool ManejarTecla(KeyEventArgs e)
    {
        if (Video == null) return false;
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return false;
        switch (e.Key)
        {
            case Key.Space:
            case Key.K:
            case Key.MediaPlayPause:
                Alternar(); return true;
            case Key.J: Motor.Saltar(-10); Avisar("−10 s"); MostrarControles(); return true;
            case Key.L: Motor.Saltar(10); Avisar("+10 s"); MostrarControles(); return true;
            case Key.Left: Motor.Saltar(-5); Avisar("−5 s"); MostrarControles(); return true;
            case Key.Right: Motor.Saltar(5); Avisar("+5 s"); MostrarControles(); return true;
            case Key.Up: CambiarVolumen(5); return true;
            case Key.Down: CambiarVolumen(-5); return true;
            case Key.M: AlternarSilencio(); return true;
            case Key.F: PidePantallaCompleta?.Invoke(); return true;
            case Key.I: if (Compacto) PideAmpliar?.Invoke(); else if (!EnPantallaCompleta) PideMini?.Invoke(); return true;
            case Key.C: AlternarSubtitulos(); return true;
            case Key.Home: Motor.Buscar(0); return true;
            case Key.End: if (Motor.Duracion > 0) Motor.Buscar(Motor.Duracion - 1); return true;
            case Key.N when shift: case Key.MediaNextTrack: PideSiguiente?.Invoke(); return true;
            case Key.P when shift: case Key.MediaPreviousTrack: PideAnterior?.Invoke(); return true;
            case Key.OemPeriod when shift: case Key.OemComma when shift:
                {
                    double[] v = { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 3 };
                    var i = Array.FindIndex(v, x => Math.Abs(x - Motor.Velocidad) < 0.01);
                    if (i < 0) i = 3;
                    i = Math.Clamp(i + (e.Key == Key.OemPeriod ? 1 : -1), 0, v.Length - 1);
                    Motor.Velocidad = v[i];
                    Avisar(v[i] == 1 ? "Velocidad normal" : $"Velocidad {v[i]}×");
                    return true;
                }
            case Key.OemPeriod when Motor.Estado == EstadoMotor.Pausado: Motor.Saltar(1.0 / 30); return true;
            case Key.OemComma when Motor.Estado == EstadoMotor.Pausado: Motor.Saltar(-1.0 / 30); return true;
        }
        if (e.Key is >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9)
        {
            var n = e.Key >= Key.NumPad0 ? e.Key - Key.NumPad0 : e.Key - Key.D0;
            if (Motor.Duracion > 0) { Motor.Buscar(Motor.Duracion * n / 10.0); MostrarControles(); }
            return true;
        }
        return false;
    }
}
