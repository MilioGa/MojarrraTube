using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace MojarraTube;

/// <summary>Una pantalla de la app (inicio, búsqueda, video…).</summary>
public interface IVista
{
    /// <summary>Elemento del menú lateral que se marca: inicio, suscripciones, biblioteca, descargas, ajustes o "".</summary>
    string Seccion { get; }
    void AlMostrar();
    void AlOcultar();
}

public partial class MainWindow : Window
{
    public Reproductor Rep { get; }
    readonly List<UserControl> _pila = new();
    UserControl? _actual;
    readonly string[] _argsIniciales;

    public MainWindow(string[] args)
    {
        _argsIniciales = args;
        InitializeComponent();
        Panel.SetZIndex(CapaMenu, 20);
        Panel.SetZIndex(Mini, 30);
        Panel.SetZIndex(CapaCompleta, 50);
        Panel.SetZIndex(CapaDialogo, 60);
        Panel.SetZIndex(Avisos, 70);

        Rep = new Reproductor();
        Rep.PidePantallaCompleta += () => PantallaCompleta(!_completa);
        Rep.PideMini += () => { if (_actual is VistaVideo) Atras(); else IrAMini(); };
        Rep.PideAmpliar += AmpliarMini;
        Rep.PideCerrar += CerrarReproductor;
        Rep.PideSiguiente += () => Siguiente();
        Rep.PideAnterior += Anterior;
        Rep.Terminado += AlTerminarVideo;
        Rep.Motor.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Motor.Estado)) ActualizarTitulo(); };

        var d = GestorDescargas.Instancia;
        d.Cargar();
        d.PropertyChanged += (_, _) => ActualizarInsignia();
        d.Terminada += x =>
        {
            ActualizarInsignia();
            Aviso($"Descarga lista: {x.Titulo}", "", ("Abrir", () => AbrirDescarga(x)), ("Mostrar", () => GestorDescargas.MostrarEnCarpeta(x.R.Archivo)));
        };
        d.Fallo += x => Aviso($"No se pudo descargar «{Recortar(x.Titulo, 40)}». {x.R.Error}", "", ("Ver", () => Navegar(new VistaDescargas())));
        ActualizarInsignia();

        Herramientas.Instancia.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(ActualizarComponentes);
        Almacen.Cambio += () => Dispatcher.BeginInvoke(ActualizarCanales);
        ActualizarCanales();

        Loaded += AlCargar;
        SizeChanged += (_, _) => Responsivo();
        StateChanged += (_, _) => AjustarMaximizada();
        Closing += AlCerrar;
        PreviewKeyDown += Teclado;
        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1) { Atras(); e.Handled = true; }
        };
        Activated += (_, _) => RevisarPortapapeles();
        Drop += Soltar;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        RestaurarVentana();
    }

    void AlCargar(object s, RoutedEventArgs e)
    {
        AjustarMaximizada();
        Responsivo();
        Navegar(new VistaInicio());
        if (!Herramientas.Completo) Herramientas.Instancia.Instalar();
        else if ((DateTime.Now - Almacen.Ajustes.UltimaActualizacion).TotalDays > 7)
            _ = Herramientas.Instancia.ActualizarYtDlp().ContinueWith(_ => Dispatcher.BeginInvoke(() => Almacen.Guardar("ajustes")));
        ActualizarComponentes();
        if (_argsIniciales.Length > 0) AbrirArgumentos(_argsIniciales, false);
        else RevisarPortapapeles();
    }

    void AlCerrar(object? s, CancelEventArgs e)
    {
        Rep.GuardarPosicion(true);
        GuardarVentana();
        GestorDescargas.Instancia.DetenerTodo();
        Almacen.Guardar("descargas", "historial", "ajustes");
        Almacen.Escribir();
        try { Rep.Motor.Dispose(); } catch { }
    }

    // ═════════════ Navegación ═════════════
    public UserControl? Actual => _actual;

    public void Navegar(UserControl vista, bool reemplazar = false)
    {
        if (_actual != null)
        {
            (_actual as IVista)?.AlOcultar();
            if (!reemplazar) { _pila.Add(_actual); if (_pila.Count > 40) _pila.RemoveAt(0); }
        }
        Mostrar(vista, 1);
    }

    public void Atras()
    {
        if (_completa) { PantallaCompleta(false); return; }
        if (CapaDialogo.Visibility == Visibility.Visible) { CerrarDialogo(); return; }
        if (_pila.Count == 0) return;
        var v = _pila[^1];
        _pila.RemoveAt(_pila.Count - 1);
        (_actual as IVista)?.AlOcultar();
        Mostrar(v, -1);
    }

    void Mostrar(UserControl v, int direccion)
    {
        MenuExtra = null;
        Contenido.Children.Clear();
        Contenido.Children.Add(v);
        _actual = v;
        BtnAtras.IsEnabled = _pila.Count > 0;
        var sec = (v as IVista)?.Seccion ?? "";
        NavInicio.IsChecked = sec == "inicio";
        NavSuscripciones.IsChecked = sec == "suscripciones";
        NavBiblioteca.IsChecked = sec == "biblioteca";
        NavDescargas.IsChecked = sec == "descargas";
        NavAjustes.IsChecked = sec == "ajustes";
        NavAyuda.IsChecked = false;
        // Transición: aparece deslizándose un poco
        var mov = new TranslateTransform(0, 14 * direccion);
        v.RenderTransform = mov;
        v.Opacity = 0;
        var dur = TimeSpan.FromMilliseconds(220);
        v.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = new CubicEase() });
        mov.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dur) { EasingFunction = new CubicEase() });
        (v as IVista)?.AlMostrar();
        CerrarMenuFlotante();
        ActualizarTitulo();
    }

    void IrSeccion<T>(Func<T> crear) where T : UserControl
    {
        if (_actual is T) { (_actual as IVista)?.AlMostrar(); return; }
        var previa = _pila.OfType<T>().LastOrDefault();
        Navegar(crear());
    }

    void NavInicio_Click(object s, RoutedEventArgs e) => IrSeccion(() => new VistaInicio());
    void NavSuscripciones_Click(object s, RoutedEventArgs e) => IrSeccion(() => new VistaSuscripciones());
    void NavBiblioteca_Click(object s, RoutedEventArgs e) => IrSeccion(() => new VistaBiblioteca());
    void NavDescargas_Click(object s, RoutedEventArgs e) => IrSeccion(() => new VistaDescargas());
    void NavAjustes_Click(object s, RoutedEventArgs e) => IrSeccion(() => new VistaAjustes());
    void NavAyuda_Click(object s, RoutedEventArgs e) { NavAyuda.IsChecked = false; MostrarAtajos(); }
    void Logo_Click(object s, RoutedEventArgs e) => IrSeccion(() => new VistaInicio());
    void Atras_Click(object s, RoutedEventArgs e) => Atras();
    void Descargas_Click(object s, RoutedEventArgs e) => IrSeccion(() => new VistaDescargas());
    void CanalMenu_Click(object s, RoutedEventArgs e)
    {
        if ((s as FrameworkElement)?.DataContext is Canal c) AbrirCanal(c.Id, c.Nombre);
    }

    string _firmaCanales = "-";
    void ActualizarCanales()
    {
        var lista = Almacen.Suscripciones.OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
        var firma = string.Join("|", lista.Select(c => c.Id));
        if (firma != _firmaCanales) { _firmaCanales = firma; ListaCanales.ItemsSource = lista; }
        var hay = lista.Count > 0 && !_menuCompacto;
        TituloCanales.Visibility = SepCanales.Visibility = ListaCanales.Visibility = hay ? Visibility.Visible : Visibility.Collapsed;
    }

    void ActualizarInsignia()
    {
        var n = GestorDescargas.Instancia.Activas;
        Insignia.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        InsigniaTexto.Text = n > 9 ? "9+" : n.ToString();
        TaskbarItemInfo ??= new TaskbarItemInfo();
        var activas = GestorDescargas.Instancia.Lista.Where(x => x.Estado == EstadoDescarga.Descargando).ToList();
        if (activas.Count > 0)
        {
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
            TaskbarItemInfo.ProgressValue = activas.Average(x => x.Fraccion);
        }
        else TaskbarItemInfo.ProgressState = n > 0 ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.None;
    }

    void ActualizarComponentes()
    {
        var h = Herramientas.Instancia;
        var mostrar = h.Ocupado && (!Herramientas.Completo || h.Mensaje.StartsWith("Descargando") || h.Mensaje.StartsWith("Instalando")) || h.Error != null;
        BarraComponentes.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;
        if (!mostrar) return;
        ComponentesTexto.Text = h.Error ?? (Herramientas.HayYtDlp ? h.Mensaje : "Preparando MojarraTube por primera vez · " + h.Mensaje);
        ComponentesBarra.Value = h.Progreso;
        ComponentesBarra.IsIndeterminate = h.Progreso <= 0;
        ComponentesBarra.Visibility = h.Error != null ? Visibility.Collapsed : Visibility.Visible;
        ComponentesIcono.Text = h.Error != null ? "" : "";
        ComponentesIcono.Foreground = (Brush)FindResource(h.Error != null ? "Aviso" : "Acento");
        BtnReintentarComponentes.Visibility = h.Error != null ? Visibility.Visible : Visibility.Collapsed;
    }

    void ReintentarComponentes_Click(object s, RoutedEventArgs e) => Herramientas.Instancia.Instalar();

    void ActualizarTitulo()
    {
        var t = Marca.Nombre;
        if (Rep.Video != null && Rep.Motor.Reproduciendo) t = Rep.Video.Titulo + " · " + Marca.Nombre;
        Title = t;
    }

    // ═════════════ Diseño adaptable ═════════════
    bool _menuCompacto, _menuOculto;

    public void Responsivo()
    {
        var w = ActualWidth;
        _menuOculto = w < 780;
        _menuCompacto = !_menuOculto && (w < 1240 || Almacen.Ajustes.MenuCompacto);
        if (MenuScroll.Parent == MenuLateral || MenuScroll.Parent == null)
        {
            ColMenu.Width = new GridLength(_menuOculto ? 0 : _menuCompacto ? 84 : 236);
            AplicarMenu(_menuCompacto);
        }
        if (!_menuOculto) CerrarMenuFlotante();
        // Barra superior
        var angosta = w < 640;
        PistaAtajo.Visibility = w < 900 ? Visibility.Collapsed : Visibility.Visible;
        if (Logo.Template.FindName("LogoTexto", Logo) is TextBlock lt) lt.Visibility = angosta ? Visibility.Collapsed : Visibility.Visible;
        ((Grid)CajaBusqueda.Parent).Margin = new Thickness(angosta ? 8 : 20, 0, angosta ? 4 : 20, 0);
        Busqueda.Tag = w < 900 ? "Buscar o pegar un enlace" : "Buscar en YouTube o pegar un enlace de cualquier página";
        Mini.Width = w < 900 ? 300 : 400;
        Mini.Height = Mini.Width * 9 / 16;
    }

    void AplicarMenu(bool compacto)
    {
        var estilo = (Style)FindResource(compacto ? "NavItemCompacto" : "NavItem");
        foreach (var rb in new[] { NavInicio, NavSuscripciones, NavBiblioteca, NavDescargas, NavAjustes, NavAyuda })
            rb.Style = estilo;
        NavAyuda.Content = compacto ? "Atajos" : "Atajos de teclado";
        MenuItems.Margin = compacto ? new Thickness(6, 4, 6, 16) : new Thickness(10, 6, 10, 16);
        ActualizarCanales();
    }

    void Menu_Click(object s, RoutedEventArgs e)
    {
        if (_menuOculto) { AbrirMenuFlotante(); return; }
        Almacen.Ajustes.MenuCompacto = !_menuCompacto;
        Almacen.Guardar("ajustes");
        // En ventanas medianas el menú completo se muestra flotando
        if (!Almacen.Ajustes.MenuCompacto && ActualWidth < 1240) { Almacen.Ajustes.MenuCompacto = true; AbrirMenuFlotante(); return; }
        Responsivo();
    }

    void AbrirMenuFlotante()
    {
        MenuLateral.Child = null;
        HuecoMenuFlotante.Content = MenuScroll;
        AplicarMenu(false);
        TituloCanales.Visibility = SepCanales.Visibility = ListaCanales.Visibility = Almacen.Suscripciones.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CapaMenu.Visibility = Visibility.Visible;
        MenuFlotanteMov.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-260, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase() });
        VeloMenu.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        NavInicio.Focus();
    }

    void CerrarMenuFlotante()
    {
        if (CapaMenu.Visibility != Visibility.Visible) return;
        CapaMenu.Visibility = Visibility.Collapsed;
        HuecoMenuFlotante.Content = null;
        MenuLateral.Child = MenuScroll;
        Responsivo();
    }

    void VeloMenu_Click(object s, RoutedEventArgs e) => CerrarMenuFlotante();

    // ═════════════ Ventana ═════════════
    void Minimizar_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximizar_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Cerrar_Click(object s, RoutedEventArgs e) => Close();

    void AjustarMaximizada()
    {
        Raiz.Margin = WindowState == WindowState.Maximized && !_completa ? new Thickness(7) : new Thickness(0);
        BtnMaximizar.Content = WindowState == WindowState.Maximized ? "" : "";
        BtnMaximizar.ToolTip = WindowState == WindowState.Maximized ? "Restaurar" : "Maximizar";
    }

    static string ArchivoVentana => Path.Combine(Marca.DirLocal, "ventana.txt");
    void RestaurarVentana()
    {
        try
        {
            var p = File.ReadAllText(ArchivoVentana).Split(';').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (p[2] >= MinWidth && p[3] >= MinHeight && p[0] > SystemParameters.VirtualScreenLeft - 50 && p[1] > SystemParameters.VirtualScreenTop - 50)
            { WindowStartupLocation = WindowStartupLocation.Manual; Left = p[0]; Top = p[1]; Width = p[2]; Height = p[3]; }
            if (p.Length > 4 && p[4] == 1) WindowState = WindowState.Maximized;
        }
        catch { }
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width); Height = Math.Min(Height, area.Height);
    }
    void GuardarVentana()
    {
        try
        {
            var r = _completa ? _antesCompleta : RestoreBounds;
            if (r.IsEmpty) return;
            var max = (_completa ? _estadoAntes : WindowState) == WindowState.Maximized ? 1 : 0;
            File.WriteAllText(ArchivoVentana, string.Join(';', new[] { r.Left, r.Top, r.Width, r.Height, max }.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
        catch { }
    }

    // ═════════════ Pantalla completa ═════════════
    bool _completa; Rect _antesCompleta; WindowState _estadoAntes; Panel? _huecoAntes;
    public bool EnPantallaCompleta => _completa;

    public void PantallaCompleta(bool activar)
    {
        if (activar == _completa || (activar && Rep.Video == null)) return;
        _completa = activar;
        var chrome = WindowChrome.GetWindowChrome(this);
        if (activar)
        {
            _estadoAntes = WindowState;
            _antesCompleta = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _huecoAntes = Rep.Parent as Panel;
            Mover(Rep, CapaCompleta);
            CapaCompleta.Visibility = Visibility.Visible;
            Rep.EnPantallaCompleta = true;
            Rep.Compacto = false;
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new Thickness(0);
            var m = Monitor();
            WindowState = WindowState.Normal;
            Raiz.Margin = new Thickness(0);
            Left = m.Left; Top = m.Top; Width = m.Width; Height = m.Height;
            Topmost = true;
        }
        else
        {
            Topmost = false;
            chrome.CaptionHeight = 52;
            chrome.ResizeBorderThickness = new Thickness(6);
            Left = _antesCompleta.Left; Top = _antesCompleta.Top; Width = _antesCompleta.Width; Height = _antesCompleta.Height;
            WindowState = _estadoAntes;
            CapaCompleta.Visibility = Visibility.Collapsed;
            Rep.EnPantallaCompleta = false;
            if (_huecoAntes != null && _huecoAntes != CapaCompleta && (_huecoAntes == HuecoMini || EstaEnArbol(_huecoAntes))) Mover(Rep, _huecoAntes);
            else if (_actual is VistaVideo vv) vv.TomarReproductor();
            else IrAMini();
            Rep.Compacto = Rep.Parent == HuecoMini;
            AjustarMaximizada();
        }
        Rep.Focus();
    }

    bool EstaEnArbol(DependencyObject d)
    {
        while (d != null) { if (d == this) return true; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d); }
        return false;
    }

    Rect Monitor()
    {
        var h = new WindowInteropHelper(this).Handle;
        var mon = MonitorFromWindow(h, 2);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref info))
        {
            var src = PresentationSource.FromVisual(this);
            var m = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var a = m.Transform(new Point(info.rcMonitor.Left, info.rcMonitor.Top));
            var b = m.Transform(new Point(info.rcMonitor.Right, info.rcMonitor.Bottom));
            return new Rect(a, b);
        }
        return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    // ═════════════ Reproductor: dónde se muestra ═════════════
    public static void Mover(FrameworkElement e, Panel destino)
    {
        if (e.Parent == destino) return;
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case ContentControl c: c.Content = null; break;
            case Decorator d: d.Child = null; break;
        }
        destino.Children.Add(e);
    }

    /// <summary>La página de video pide el reproductor para su hueco.</summary>
    public void ColocarReproductor(Panel hueco)
    {
        if (_completa) { _huecoAntes = hueco; return; }
        Mover(Rep, hueco);
        Rep.Compacto = false;
        Mini.Visibility = Visibility.Collapsed;
    }

    public void IrAMini()
    {
        if (Rep.Video == null) return;
        Mover(Rep, HuecoMini);
        Rep.Compacto = true;
        Mini.Visibility = Visibility.Visible;
        Mini.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    void AmpliarMini()
    {
        if (Rep.Video == null) return;
        if (_actual is VistaVideo vv && vv.Video.Clave == Rep.Video.Clave) { vv.TomarReproductor(); return; }
        Navegar(new VistaVideo(Rep.Video, Rep.Info));
    }

    public void CerrarReproductor()
    {
        Rep.Detener();
        Mini.Visibility = Visibility.Collapsed;
        if (_completa) PantallaCompleta(false);
        ActualizarTitulo();
    }

    // ═════════════ Teclado ═════════════
    void Teclado(object s, KeyEventArgs e)
    {
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var enTexto = Keyboard.FocusedElement is TextBox;

        if (CapaDialogo.Visibility == Visibility.Visible)
        {
            if (k == Key.Escape) { CerrarDialogo(); e.Handled = true; }
            return;
        }
        if (k == Key.Escape)
        {
            if (_completa) { PantallaCompleta(false); e.Handled = true; return; }
            if (CapaMenu.Visibility == Visibility.Visible) { CerrarMenuFlotante(); e.Handled = true; return; }
            if (PopupSugerencias.IsOpen) { PopupSugerencias.IsOpen = false; e.Handled = true; return; }
            if (enTexto) { Keyboard.ClearFocus(); FocusManager.SetFocusedElement(this, this); Focus(); e.Handled = true; return; }
        }
        if ((ctrl && k is Key.K or Key.L or Key.E) || (!enTexto && !ctrl && k == Key.OemQuestion && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        { EnfocarBusqueda(); e.Handled = true; return; }
        if ((alt && k == Key.Left) || k == Key.BrowserBack) { Atras(); e.Handled = true; return; }
        if (ctrl && k == Key.J) { IrSeccion(() => new VistaDescargas()); e.Handled = true; return; }
        if (ctrl && k == Key.H) { IrSeccion(() => new VistaBiblioteca()); e.Handled = true; return; }
        if (ctrl && k == Key.OemComma) { IrSeccion(() => new VistaAjustes()); e.Handled = true; return; }
        if (ctrl && k == Key.O) { AbrirArchivo_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
        if (ctrl && k == Key.D && Rep.Video is { EsLocal: false } v) { Descargar(v); e.Handled = true; return; }
        if (ctrl && k == Key.V && !enTexto) { AbrirPortapapeles(); e.Handled = true; return; }
        if (k == Key.F1 || (!enTexto && k == Key.OemQuestion && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) { MostrarAtajos(); e.Handled = true; return; }
        if (k == Key.F11 && Rep.Video != null) { PantallaCompleta(!_completa); e.Handled = true; return; }
        if (enTexto || ctrl || alt) return;

        // Atajos del reproductor: en la página del video, en pantalla completa o con el minirreproductor
        var reproductorActivo = Rep.Video != null && (_completa || _actual is VistaVideo || Mini.Visibility == Visibility.Visible);
        if (!reproductorActivo) return;
        // Espacio/Enter sobre un botón enfocado (fuera del reproductor) activan el botón
        if (k is Key.Space or Key.Enter && Keyboard.FocusedElement is ButtonBase b && !EsHijoDe(b, Rep)) return;
        if (k is Key.Up or Key.Down && Keyboard.FocusedElement is not null && !_completa && _actual is not VistaVideo) return;
        if (Rep.ManejarTecla(e)) e.Handled = true;
    }

    static bool EsHijoDe(DependencyObject d, DependencyObject padre)
    {
        while (d != null) { if (d == padre) return true; d = VisualTreeHelper.GetParent(d); }
        return false;
    }

    // ═════════════ Arrastrar y soltar ═════════════
    void Soltar(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] archivos && archivos.Length > 0) { AbrirArgumentos(archivos, false); return; }
        if (e.Data.GetData(DataFormats.Text) is string t && Texto.EsUrl(t)) AbrirTexto(t);
    }

    public void TemaCambiado(bool claro)
    {
        // Barra de título oscura/clara de Windows (borde de la ventana)
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            int v = claro ? 0 : 1;
            DwmSetWindowAttribute(h, 20, ref v, sizeof(int));
        }
        catch { }
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TemaCambiado(Almacen.Ajustes.Tema == "claro" || (Almacen.Ajustes.Tema == "sistema" && App.SistemaEnClaro()));
    }

    static string Recortar(string s, int n) => s.Length > n ? s[..n] + "…" : s;
}
