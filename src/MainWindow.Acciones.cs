using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MojarraTube;

public record Sugerencia(string Texto, string Icono, bool Reciente);

public partial class MainWindow
{
    /// <summary>La vista actual puede agregar opciones al menú de las tarjetas (p. ej. "Quitar del historial").</summary>
    public Func<Video, List<(string, string, Action)>>? MenuExtra { get; set; }

    // ═════════════ Buscador ═════════════
    CancellationTokenSource? _ctsSug;
    bool _cambiandoTexto;

    public void EnfocarBusqueda()
    {
        if (_completa) PantallaCompleta(false);
        Busqueda.Focus();
        Busqueda.SelectAll();
    }

    void Busqueda_Foco(object s, KeyboardFocusChangedEventArgs e)
    {
        CajaBusqueda.BorderBrush = (Brush)FindResource("Acento");
        IconoBusqueda.Foreground = (Brush)FindResource("Texto");
        MostrarSugerencias();
    }

    void Busqueda_SinFoco(object s, KeyboardFocusChangedEventArgs e)
    {
        CajaBusqueda.BorderBrush = (Brush)FindResource("Linea");
        IconoBusqueda.Foreground = (Brush)FindResource("Tenue");
    }

    void Busqueda_TextChanged(object s, TextChangedEventArgs e)
    {
        BtnLimpiar.Visibility = Busqueda.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PistaAtajo.Visibility = Busqueda.Text.Length > 0 || ActualWidth < 900 ? Visibility.Collapsed : Visibility.Visible;
        if (_cambiandoTexto || !Busqueda.IsKeyboardFocused) return;
        MostrarSugerencias();
    }

    async void MostrarSugerencias()
    {
        var q = Busqueda.Text.Trim();
        _ctsSug?.Cancel();
        var cts = _ctsSug = new CancellationTokenSource();
        var lista = new List<Sugerencia>();
        if (Texto.EsUrl(q))
        {
            lista.Add(new Sugerencia(Texto.EsYouTube(q) ? "Abrir este enlace de YouTube" : "Abrir el video de este enlace", "", false));
            lista.Add(new Sugerencia("Descargar desde este enlace", "", false));
            Pintar(lista, q);
            return;
        }
        lista.AddRange(Almacen.Busquedas.Where(b => q.Length == 0 || b.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)).Take(q.Length == 0 ? 8 : 3).Select(b => new Sugerencia(b, "", true)));
        Pintar(lista, q);
        if (q.Length == 0) return;
        try { await Task.Delay(140, cts.Token); } catch { return; }
        var web = await YtDlp.Sugerencias(q, cts.Token);
        if (cts.IsCancellationRequested) return;
        foreach (var w in web) if (!lista.Any(x => x.Texto.Equals(w, StringComparison.CurrentCultureIgnoreCase))) lista.Add(new Sugerencia(w, "", false));
        Pintar(lista.Take(10).ToList(), q);
    }

    void Pintar(List<Sugerencia> lista, string q)
    {
        ListaSugerencias.ItemsSource = lista;
        PopupSugerencias.IsOpen = lista.Count > 0 && Busqueda.IsKeyboardFocused;
    }

    void Busqueda_PreviewKeyDown(object s, KeyEventArgs e)
    {
        if (!PopupSugerencias.IsOpen) return;
        var n = ListaSugerencias.Items.Count;
        if (e.Key == Key.Down || e.Key == Key.Up)
        {
            var i = ListaSugerencias.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
            if (i < -1) i = n - 1;
            if (i >= n) i = -1;
            ListaSugerencias.SelectedIndex = i;
            if (i >= 0 && ListaSugerencias.SelectedItem is Sugerencia sg && sg.Icono != "" && sg.Icono != "")
            {
                _cambiandoTexto = true;
                Busqueda.Text = sg.Texto;
                Busqueda.CaretIndex = Busqueda.Text.Length;
                _cambiandoTexto = false;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && ListaSugerencias.SelectedItem is Sugerencia { Reciente: true } r && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            Almacen.Busquedas.Remove(r.Texto);
            Almacen.Guardar("busquedas");
            MostrarSugerencias();
            e.Handled = true;
        }
    }

    void Busqueda_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (PopupSugerencias.IsOpen && ListaSugerencias.SelectedItem is Sugerencia sg) { UsarSugerencia(sg); e.Handled = true; return; }
        Buscar(Busqueda.Text);
        e.Handled = true;
    }

    void Sugerencia_Clic(object s, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is Sugerencia sg) UsarSugerencia(sg);
    }

    void UsarSugerencia(Sugerencia sg)
    {
        PopupSugerencias.IsOpen = false;
        var q = Busqueda.Text.Trim();
        if (sg.Icono == "") { Descargar(new Video { Url = Texto.NormalizarUrl(q), Titulo = q }); return; }
        if (sg.Icono == "") { Buscar(q); return; }
        Buscar(sg.Texto);
    }

    void Limpiar_Click(object s, RoutedEventArgs e) { Busqueda.Clear(); Busqueda.Focus(); }
    void BuscarMovil_Click(object s, RoutedEventArgs e) => EnfocarBusqueda();

    /// <summary>Busca en YouTube, o abre el enlace si lo que se escribió es una dirección.</summary>
    public void Buscar(string q)
    {
        q = q.Trim();
        PopupSugerencias.IsOpen = false;
        if (q.Length == 0) return;
        if (Texto.EsUrl(q)) { AbrirTexto(q); Keyboard.ClearFocus(); Focus(); return; }
        _cambiandoTexto = true; Busqueda.Text = q; _cambiandoTexto = false;
        Almacen.AgregarBusqueda(q);
        Keyboard.ClearFocus();
        Focus();
        if (_actual is VistaBusqueda vb && vb.Consulta == q) { vb.Recargar(); return; }
        Navegar(new VistaBusqueda(q));
    }

    // ═════════════ Abrir videos, canales, enlaces y archivos ═════════════
    public void Abrir(Video v)
    {
        if (v.EsCanal) { AbrirCanal(v.CanalId, v.Canal, v.Url); return; }
        if (v.EsLista) { Navegar(new VistaCanal(v.UrlCompleta, v.Titulo, esLista: true)); return; }
        AbrirVideo(v);
    }

    public void AbrirVideo(Video v, InfoVideo? info = null)
    {
        if (_completa) PantallaCompleta(false);
        // Si ya suena en el minirreproductor, solo se amplía
        if (Rep.Video != null && Rep.Video.Clave == v.Clave)
        {
            if (_actual is VistaVideo vv && vv.Video.Clave == v.Clave) { vv.TomarReproductor(); return; }
            Navegar(new VistaVideo(Rep.Video, Rep.Info));
            return;
        }
        ColocarEnCola(v);
        Navegar(new VistaVideo(v, info), _actual is VistaVideo);
    }

    public void AbrirCanal(string id, string nombre, string? url = null)
    {
        var destino = !string.IsNullOrEmpty(id) ? id : url ?? "";
        if (destino.Length == 0) { Buscar(nombre); return; }
        Navegar(new VistaCanal(destino, nombre));
    }

    /// <summary>Texto escrito, pegado o soltado: enlace de YouTube (video, canal, lista) o de cualquier otra página.</summary>
    public void AbrirTexto(string texto)
    {
        texto = texto.Trim();
        if (File.Exists(texto)) { AbrirArchivos(new[] { texto }); return; }
        if (!Texto.EsUrl(texto)) { Buscar(texto); return; }
        var url = Texto.NormalizarUrl(texto);
        if (Texto.EsYouTube(url))
        {
            var id = Texto.IdYouTube(url);
            if (id != null) { AbrirVideo(new Video { Id = id, Url = "https://www.youtube.com/watch?v=" + id, Miniatura = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg" }); return; }
            var lista = Texto.ListaYouTube(url);
            if (lista != null) { Navegar(new VistaCanal("https://www.youtube.com/playlist?list=" + lista, "Lista", esLista: true)); return; }
            var ruta = new Uri(url).AbsolutePath;
            if (ruta.StartsWith("/@") || ruta.StartsWith("/channel/") || ruta.StartsWith("/c/") || ruta.StartsWith("/user/"))
            {
                var baseCanal = "https://www.youtube.com" + string.Join('/', ruta.Split('/').Take(3));
                Navegar(new VistaCanal(baseCanal, ""));
                return;
            }
            if (ruta is "/" or "" or "/feed/subscriptions") { IrSeccion(() => new VistaInicio()); return; }
            var q = Texto.Parametro(new Uri(url).Query, "search_query");
            if (q != null) { Buscar(q); return; }
        }
        AbrirVideo(new Video { Url = url, Titulo = url });
    }

    /// <summary>Archivos o enlaces recibidos al abrir la app (o desde otra ventana).</summary>
    public void AbrirArgumentos(string[] args, bool traerAlFrente)
    {
        if (traerAlFrente)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }
        var archivos = args.Where(File.Exists).ToArray();
        if (archivos.Length > 0) AbrirArchivos(archivos);
        else if (args.FirstOrDefault(a => !a.StartsWith("--")) is { } t) AbrirTexto(t);
    }

    static readonly string[] ExtVideo = { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".wmv", ".m4v", ".flv", ".ts", ".mts", ".m2ts", ".3gp", ".mpg", ".mpeg", ".ogv" };
    static readonly string[] ExtAudio = { ".mp3", ".m4a", ".opus", ".ogg", ".wav", ".flac", ".aac", ".wma", ".weba" };
    public static bool EsMedio(string ruta) { var x = Path.GetExtension(ruta).ToLowerInvariant(); return ExtVideo.Contains(x) || ExtAudio.Contains(x); }
    public static bool EsAudio(string ruta) => ExtAudio.Contains(Path.GetExtension(ruta).ToLowerInvariant());

    public static Video VideoLocal(string ruta)
    {
        var fi = new FileInfo(ruta);
        return new Video { Id = ruta, Titulo = Path.GetFileNameWithoutExtension(ruta), Ruta = ruta, Canal = fi.Directory?.Name ?? "", Fecha = fi.LastWriteTime, SoloAudio = EsAudio(ruta), Miniatura = "video:" + ruta };
    }

    public void AbrirArchivos(IEnumerable<string> rutas)
    {
        var lista = new List<string>();
        foreach (var r in rutas)
        {
            if (Directory.Exists(r))
                lista.AddRange(Directory.EnumerateFiles(r).Where(EsMedio).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
            else if (File.Exists(r)) lista.Add(r);
        }
        if (lista.Count == 0) { Aviso("No se encontraron videos ni audios para abrir.", ""); return; }
        var videos = lista.Select(VideoLocal).ToList();
        _cola = videos;
        _indice = 0;
        AbrirVideo(videos[0]);
        if (videos.Count > 1) Aviso($"Se agregaron {videos.Count} archivos a la cola", "");
    }

    void AbrirArchivo_Click(object s, RoutedEventArgs e) => AbrirArchivoDialogo();

    public void AbrirArchivoDialogo()
    {
        var d = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Abrir videos o audios",
            Multiselect = true,
            Filter = "Videos y audios|" + string.Join(";", ExtVideo.Concat(ExtAudio).Select(x => "*" + x)) + "|Todos los archivos|*.*",
        };
        if (d.ShowDialog(this) == true) AbrirArchivos(d.FileNames);
    }

    public void AbrirDescarga(Descarga d)
    {
        var a = d.R.Archivo;
        if (a == null) return;
        if (Directory.Exists(a)) { AbrirArchivos(new[] { a }); return; }
        if (!File.Exists(a)) { Aviso("El archivo ya no está en su carpeta. Puede que se haya movido o borrado.", ""); return; }
        var v = VideoLocal(a);
        v.Titulo = d.Titulo;
        v.Canal = d.Canal;
        AbrirVideo(v);
    }

    // ═════════════ Cola de reproducción ═════════════
    List<Video> _cola = new();
    int _indice = -1;
    public IReadOnlyList<Video> Cola => _cola;
    public int IndiceCola => _indice;
    public event Action? ColaCambio;

    void ColocarEnCola(Video v)
    {
        var i = _cola.FindIndex(x => x.Clave == v.Clave);
        if (i >= 0) _indice = i;
        else if (_cola.Count > _indice + 1 && _indice >= 0) { _cola.Insert(_indice + 1, v); _indice++; }
        else { _cola = new List<Video> { v }; _indice = 0; }
        ActualizarBotonesCola();
    }

    public void AgregarACola(Video v)
    {
        if (v.EsCanal || v.EsLista) { Abrir(v); return; }
        if (Rep.Video == null) { AbrirVideo(v); return; }
        if (_cola.Count == 0) { _cola.Add(Rep.Video); _indice = 0; }
        _cola.Add(v.Copia());
        ActualizarBotonesCola();
        Aviso($"Agregado a la cola · {_cola.Count - _indice - 1} por ver", "", ("Deshacer", () => { _cola.Remove(_cola.Last()); ActualizarBotonesCola(); }));
    }

    public void PonerCola(List<Video> videos, int inicio = 0)
    {
        if (videos.Count == 0) return;
        _cola = videos.Select(x => x.Copia()).ToList();
        _indice = Math.Clamp(inicio, 0, _cola.Count - 1);
        Navegar(new VistaVideo(_cola[_indice]), _actual is VistaVideo);
        ActualizarBotonesCola();
    }

    public void QuitarDeCola(Video v)
    {
        var i = _cola.IndexOf(v);
        if (i < 0 || i == _indice) return;
        _cola.RemoveAt(i);
        if (i < _indice) _indice--;
        ActualizarBotonesCola();
    }

    void ActualizarBotonesCola()
    {
        Rep.HaySiguiente = _indice >= 0 && _indice < _cola.Count - 1;
        Rep.HayAnterior = _indice > 0;
        ColaCambio?.Invoke();
    }

    public bool Siguiente(bool automatico = false)
    {
        if (_indice < 0 || _indice >= _cola.Count - 1) return false;
        _indice++;
        var v = _cola[_indice];
        if (_actual is VistaVideo || Mini.Visibility != Visibility.Visible) Navegar(new VistaVideo(v), _actual is VistaVideo);
        else { var vista = new VistaVideo(v); vista.CargarEnMini(); }
        ActualizarBotonesCola();
        return true;
    }

    void Anterior()
    {
        if (Rep.Motor.Posicion > 5 || _indice <= 0) { Rep.Motor.Buscar(0); return; }
        _indice--;
        Navegar(new VistaVideo(_cola[_indice]), _actual is VistaVideo);
        ActualizarBotonesCola();
    }

    DispatcherTimer? _autoSiguiente;
    void AlTerminarVideo()
    {
        if (!Almacen.Ajustes.Reproduccion_Auto || _indice >= _cola.Count - 1 || _indice < 0) return;
        var sig = _cola[_indice + 1];
        Aviso($"A continuación: {sig.Titulo}", "", ("Cancelar", () => _autoSiguiente?.Stop()));
        _autoSiguiente?.Stop();
        _autoSiguiente = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoSiguiente.Tick += (_, _) => { _autoSiguiente.Stop(); if (Rep.Motor.Estado == EstadoMotor.Terminado) Siguiente(true); };
        _autoSiguiente.Start();
    }

    // ═════════════ Acciones ═════════════
    public void Descargar(Video v)
    {
        if (v.EsLocal) return;
        if (_completa) PantallaCompleta(false);
        MostrarDialogo(new DialogoDescarga(v, Rep.Video?.Clave == v.Clave ? Rep.Info : null));
    }

    public void AlternarSuscripcion(Canal c)
    {
        if (string.IsNullOrEmpty(c.Id)) { Aviso("No se pudo identificar el canal.", ""); return; }
        var ahora = Almacen.AlternarSuscripcion(c);
        Aviso(ahora ? $"Te suscribiste a {c.Nombre}" : $"Cancelaste la suscripción a {c.Nombre}", ahora ? "" : "",
            ("Deshacer", () => { Almacen.AlternarSuscripcion(c); VistaInicio.OlvidarNovedades(); SuscripcionCambio?.Invoke(); }));
        VistaInicio.OlvidarNovedades();
        SuscripcionCambio?.Invoke();
    }
    public event Action? SuscripcionCambio;

    public void AlternarVerDespues(Video v)
    {
        var ahora = Almacen.AlternarVerDespues(v);
        Aviso(ahora ? "Guardado en Ver más tarde" : "Se quitó de Ver más tarde", "", ("Deshacer", () => Almacen.AlternarVerDespues(v)));
    }

    public void CopiarEnlace(string url)
    {
        try { Clipboard.SetText(url); _ultimoPortapapeles = url; Aviso("Enlace copiado", ""); }
        catch { Aviso("No se pudo copiar el enlace", ""); }
    }

    public void AbrirEnNavegador(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ═════════════ Portapapeles ═════════════
    string? _ultimoPortapapeles;

    void RevisarPortapapeles()
    {
        if (!Almacen.Ajustes.DetectarPortapapeles || !IsLoaded) return;
        string t;
        try { t = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : ""; } catch { return; }
        if (t.Length == 0 || t == _ultimoPortapapeles || !Texto.EsUrl(t) || t.Length > 2000) return;
        _ultimoPortapapeles = t;
        var url = Texto.NormalizarUrl(t);
        // Solo enlaces que parecen de un video (no páginas de inicio)
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.AbsolutePath.Length <= 1) return;
        if (Rep.Video?.UrlCompleta == url) return;
        var corto = u.Host.Replace("www.", "") + (u.AbsolutePath.Length > 24 ? u.AbsolutePath[..24] + "…" : u.AbsolutePath);
        Aviso($"Copiaste un enlace: {corto}", "", ("Ver", () => AbrirTexto(url)), ("Descargar", () => Descargar(new Video { Url = url, Titulo = url })));
    }

    void AbrirPortapapeles()
    {
        try
        {
            if (Clipboard.ContainsFileDropList()) { AbrirArchivos(Clipboard.GetFileDropList().Cast<string>()); return; }
            var t = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            if (Texto.EsUrl(t)) { _ultimoPortapapeles = t; AbrirTexto(t); }
            else if (t.Length > 0) { Busqueda.Text = t; EnfocarBusqueda(); }
        }
        catch { }
    }

    // ═════════════ Avisos (abajo, con acciones) ═════════════
    public void Aviso(string texto, string icono = "", params (string texto, Action accion)[] acciones)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal };
        fila.Children.Add(new TextBlock { Text = icono, FontFamily = (FontFamily)FindResource("Iconos"), FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Foreground = (Brush)FindResource("Acento") });
        fila.Children.Add(new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(200, Math.Min(520, ActualWidth - 260)), Foreground = Brushes.White, FontSize = 14 });
        var caja = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x23, 0x26, 0x2F)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 10, 8, 10),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = fila,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.4, Color = Colors.Black },
            RenderTransform = new TranslateTransform(0, 20),
            Opacity = 0,
        };
        void Cerrar()
        {
            if (!Avisos.Children.Contains(caja)) return;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            a.Completed += (_, _) => Avisos.Children.Remove(caja);
            caja.BeginAnimation(OpacityProperty, a);
        }
        foreach (var (t, accion) in acciones)
        {
            var b = new Button { Content = t, Style = (Style)FindResource("BtnEnlace"), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x9E)) };
            b.Click += (_, _) => { Cerrar(); accion(); };
            fila.Children.Add(b);
        }
        var x = new Button { Content = "", Style = (Style)FindResource("BtnIcono"), Width = 28, Height = 28, FontSize = 10, Foreground = Brushes.White, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Cerrar aviso" };
        x.Click += (_, _) => Cerrar();
        fila.Children.Add(x);
        caja.MouseEnter += (_, _) => _pausaAvisos = true;
        caja.MouseLeave += (_, _) => _pausaAvisos = false;

        while (Avisos.Children.Count >= 3) Avisos.Children.RemoveAt(0);
        Avisos.Children.Add(caja);
        var dur = TimeSpan.FromMilliseconds(260);
        caja.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur));
        ((TranslateTransform)caja.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dur) { EasingFunction = new CubicEase() });
        var t2 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(acciones.Length > 0 ? 7 : 4) };
        t2.Tick += (_, _) => { if (_pausaAvisos) return; t2.Stop(); Cerrar(); };
        t2.Start();
    }
    bool _pausaAvisos;

    // ═════════════ Diálogos ═════════════
    IInputElement? _focoAntesDialogo;
    TaskCompletionSource<bool>? _respuesta;

    public void MostrarDialogo(FrameworkElement contenido, double ancho = 560)
    {
        _focoAntesDialogo = Keyboard.FocusedElement;
        CajaDialogo.Width = Math.Min(ancho, Math.Max(300, ActualWidth - 32));
        CajaDialogo.MaxHeight = Math.Max(300, ActualHeight - 60);
        HuecoDialogo.Content = contenido;
        CapaDialogo.Visibility = Visibility.Visible;
        var dur = TimeSpan.FromMilliseconds(200);
        CapaDialogo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
        DialogoEscala.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, dur) { EasingFunction = new CubicEase() });
        DialogoEscala.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, dur) { EasingFunction = new CubicEase() });
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => contenido.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

    public void CerrarDialogo()
    {
        if (CapaDialogo.Visibility != Visibility.Visible) return;
        (HuecoDialogo.Content as IDisposable)?.Dispose();
        CapaDialogo.Visibility = Visibility.Collapsed;
        HuecoDialogo.Content = null;
        _respuesta?.TrySetResult(false);
        _respuesta = null;
        if (_focoAntesDialogo is UIElement u && u.IsVisible) u.Focus();
    }

    void VeloDialogo_Click(object s, MouseButtonEventArgs e) => CerrarDialogo();

    /// <summary>Pide confirmación antes de algo que no se puede deshacer.</summary>
    public Task<bool> Confirmar(string titulo, string texto, string accion, bool peligro = true)
    {
        var tcs = new TaskCompletionSource<bool>();
        var panel = new StackPanel { Margin = new Thickness(26, 24, 26, 22) };
        panel.Children.Add(new TextBlock { Text = titulo, Style = (Style)FindResource("Subtitulo"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = texto, Style = (Style)FindResource("TextoTenue"), Margin = new Thickness(0, 10, 0, 22), FontSize = 14 });
        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var no = new Button { Content = "Cancelar", Style = (Style)FindResource("BtnFantasma"), Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
        var si = new Button { Content = accion, Style = (Style)FindResource("BtnPrimario") };
        if (peligro) si.Background = (Brush)FindResource("Peligro");
        no.Click += (_, _) => { _respuesta = null; tcs.TrySetResult(false); CerrarDialogo(); };
        si.Click += (_, _) => { _respuesta = null; tcs.TrySetResult(true); CerrarDialogo(); };
        botones.Children.Add(no);
        botones.Children.Add(si);
        panel.Children.Add(botones);
        MostrarDialogo(panel, 440);
        _respuesta = tcs;
        no.Focus();
        return tcs.Task;
    }

    public void MostrarAtajos()
    {
        var atajos = new (string, string)[]
        {
            ("Buscar", "Ctrl+K  o  /"), ("Atrás", "Alt+←  o botón lateral del ratón"), ("Abrir archivo", "Ctrl+O"),
            ("Abrir enlace copiado", "Ctrl+V"), ("Descargar el video actual", "Ctrl+D"), ("Descargas", "Ctrl+J"),
            ("Biblioteca", "Ctrl+H"), ("Ajustes", "Ctrl+,"), ("Esta ayuda", "F1  o  ?"),
            ("Reproducir / pausar", "Espacio  o  K"), ("Retroceder / adelantar 10 s", "J  /  L"), ("Retroceder / adelantar 5 s", "←  /  →"),
            ("Volumen", "↑  /  ↓  o rueda del ratón"), ("Silenciar", "M"), ("Pantalla completa", "F  o  doble clic"),
            ("Minirreproductor", "I"), ("Subtítulos", "C"), ("Velocidad", "Mayús + <  /  >"), ("Ir a 0 %…90 %", "0 … 9"),
            ("Fotograma a fotograma (en pausa)", ",  /  ."), ("Siguiente / anterior de la cola", "Mayús+N  /  Mayús+P"),
            ("Agregar a la cola", "Clic central en una tarjeta"), ("Más opciones de una tarjeta", "Clic derecho  o  tecla Menú"),
        };
        var g = new Grid { Margin = new Thickness(26, 22, 26, 22) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        var titulo = new TextBlock { Text = "Atajos de teclado", Style = (Style)FindResource("Subtitulo"), Margin = new Thickness(0, 0, 0, 14) };
        g.Children.Add(titulo);
        var cerrar = new Button { Content = "", Style = (Style)FindResource("BtnIcono"), VerticalAlignment = VerticalAlignment.Top, ToolTip = "Cerrar (Esc)" };
        cerrar.Click += (_, _) => CerrarDialogo();
        Grid.SetColumn(cerrar, 1);
        g.Children.Add(cerrar);
        int fila = 1;
        foreach (var (que, teclas) in atajos)
        {
            g.RowDefinitions.Add(new RowDefinition());
            var a = new TextBlock { Text = que, Margin = new Thickness(0, 5, 16, 5), Foreground = (Brush)FindResource("Texto") };
            var b = new Border { CornerRadius = new CornerRadius(6), Background = (Brush)FindResource("Superficie3"), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Right, Child = new TextBlock { Text = teclas, FontSize = 12.5, Foreground = (Brush)FindResource("Texto") } };
            Grid.SetRow(a, fila); Grid.SetRow(b, fila); Grid.SetColumn(b, 1);
            g.Children.Add(a); g.Children.Add(b);
            fila++;
        }
        MostrarDialogo(new ScrollViewer { Content = g, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 600);
    }
}
