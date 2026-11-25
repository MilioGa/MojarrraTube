using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MojarraTube;

/// <summary>Página de un video: reproductor, título, canal, acciones, descripción, capítulos y qué ver después.</summary>
public partial class VistaVideo : UserControl, IVista
{
    public string Seccion => "";
    public Video Video { get; private set; }
    InfoVideo? _info;

    readonly Grid _layout = new();
    readonly ScrollViewer _scroll;
    readonly StackPanel _izq = new();
    readonly StackPanel _der = new();
    readonly Grid _hueco = new() { Background = Brushes.Black };
    readonly Border _marcoReproductor;
    readonly TextBlock _titulo;
    readonly StackPanel _filaCanal = new() { Orientation = Orientation.Horizontal };
    readonly WrapPanel _acciones = new();
    readonly Border _descripcionCaja;
    readonly TextBlock _meta = new() { FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _descripcion = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
    readonly Button _verMas;
    readonly StackPanel _capitulos = new();
    readonly ObservableCollection<Video> _siguientes = new();
    readonly ObservableCollection<Video> _delCanal = new();
    readonly StackPanel _bloqueCola = new();
    readonly StackPanel _bloqueCanal = new();
    readonly ProgressBar _cargandoInfo;
    CancellationTokenSource? _cts;
    bool _descripcionAbierta;
    bool _dosColumnas;

    public VistaVideo(Video v, InfoVideo? info = null)
    {
        Video = v;
        _info = info;
        _scroll = UI.Desplazable(_layout);
        Content = _scroll;

        _layout.ColumnDefinitions.Add(new ColumnDefinition());
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        _layout.Children.Add(_izq);
        _layout.Children.Add(_der);

        _marcoReproductor = new Border { CornerRadius = new CornerRadius(14), Background = Brushes.Black, Child = _hueco, ClipToBounds = true };
        Forma.SetRadio(_hueco, 14);
        _izq.Children.Add(_marcoReproductor);
        _cargandoInfo = new ProgressBar { Style = UI.Estilo("BarraFina"), IsIndeterminate = true, Height = 3, Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
        _izq.Children.Add(_cargandoInfo);

        _titulo = new TextBlock { Style = UI.Estilo("Titulo"), FontSize = 21, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 12) };
        _izq.Children.Add(_titulo);

        var filaAcciones = new WrapPanel();
        filaAcciones.Children.Add(_filaCanal);
        _acciones.Margin = new Thickness(0, 0, 0, 0);
        filaAcciones.Children.Add(_acciones);
        _izq.Children.Add(filaAcciones);

        var desc = new StackPanel();
        desc.Children.Add(_meta);
        desc.Children.Add(_descripcion);
        _verMas = UI.Boton("Mostrar más", null, AlternarDescripcion, "BtnEnlace");
        _verMas.Margin = new Thickness(0, 8, 0, 0);
        _verMas.HorizontalAlignment = HorizontalAlignment.Left;
        desc.Children.Add(_verMas);
        _descripcionCaja = new Border { Style = UI.Estilo("Tarjeta"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 14, 0, 0), Child = desc, Visibility = Visibility.Collapsed };
        _descripcionCaja.SetResourceReference(Border.BackgroundProperty, "Superficie2");
        _descripcionCaja.BorderThickness = new Thickness(0);
        _izq.Children.Add(_descripcionCaja);
        _izq.Children.Add(_capitulos);

        _bloqueCola.Children.Add(UI.Encabezado(UI.Subtitulo("A continuación"), null, 10));
        _bloqueCola.Children.Add(UI.Filas(_siguientes));
        _bloqueCola.Visibility = Visibility.Collapsed;
        _bloqueCanal.Children.Add(UI.Encabezado(UI.Subtitulo("Más de este canal"), null, 10));
        _bloqueCanal.Children.Add(UI.Filas(_delCanal));
        _bloqueCanal.Visibility = Visibility.Collapsed;
        _der.Children.Add(_bloqueCola);
        _der.Children.Add(_bloqueCanal);

        SizeChanged += (_, _) => Acomodar();
        PintarDatos();
    }

    // ───────────── Diseño adaptable ─────────────
    void Acomodar()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0) return;
        _dosColumnas = w >= 1080;
        var m = w < 700 ? 12.0 : 24.0;
        _layout.Margin = new Thickness(m, w < 700 ? 4 : 10, m, 48);
        _layout.ColumnDefinitions[1].Width = _dosColumnas ? new GridLength(Math.Min(420, Math.Max(340, w * 0.28))) : new GridLength(0);
        if (_dosColumnas)
        {
            if (_der.Parent != _layout) { _izq.Children.Remove(_der); _layout.Children.Add(_der); }
            Grid.SetColumn(_der, 1);
            _der.Margin = new Thickness(24, 0, 0, 0);
        }
        else
        {
            if (_der.Parent == _layout) { _layout.Children.Remove(_der); _izq.Children.Add(_der); }
            _der.Margin = new Thickness(0, 26, 0, 0);
        }
        // El video ocupa el ancho disponible en 16:9, sin pasar del alto de la ventana
        var anchoCol = w - m * 2 - (_dosColumnas ? _layout.ColumnDefinitions[1].Width.Value + 24 : 0);
        var alto = Math.Min(anchoCol * 9 / 16, Math.Max(220, h - 150));
        _marcoReproductor.Height = Math.Max(180, alto);
        _marcoReproductor.CornerRadius = new CornerRadius(w < 700 ? 8 : 14);
        Forma.SetRadio(_hueco, w < 700 ? 8 : 14);
    }

    // ───────────── Ciclo de vida ─────────────
    public void AlMostrar()
    {
        var w = App.Ventana!;
        w.ColaCambio += PintarCola;
        w.SuscripcionCambio += PintarCanal;
        w.MenuExtra = v => _siguientes.Contains(v) ? new() { ("Quitar de la cola", "", () => w.QuitarDeCola(v)) } : new();
        TomarReproductor();
        if (w.Rep.Video?.Clave == Video.Clave)
        {
            _info ??= w.Rep.Info;
            if (_info != null) { Video = _info.Video.EsLocal ? Video : _info.Video; PintarDatos(); }
        }
        else Iniciar();
        PintarCola();
        Acomodar();
    }

    public void AlOcultar()
    {
        var w = App.Ventana!;
        w.ColaCambio -= PintarCola;
        w.SuscripcionCambio -= PintarCanal;
        if (w.EnPantallaCompleta) return;
        // Al salir, el video sigue en el minirreproductor
        if (w.Rep.Video != null && w.Rep.Parent == _hueco)
        {
            if (w.Rep.Motor.Estado is EstadoMotor.Reproduciendo or EstadoMotor.Cargando or EstadoMotor.Pausado) w.IrAMini();
            else w.CerrarReproductor();
        }
    }

    public void TomarReproductor() => App.Ventana?.ColocarReproductor(_hueco);

    /// <summary>Carga el video y lo reproduce (también se usa desde el minirreproductor para el siguiente de la cola).</summary>
    async void Iniciar(bool enMini = false)
    {
        var w = App.Ventana!;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        w.Rep.Preparar(Video, () => Iniciar(enMini));
        if (Video.EsLocal)
        {
            w.Rep.Cargar(Video, null);
            Almacen.AgregarHistorial(Video);
            PintarDatos();
            return;
        }
        _cargandoInfo.Visibility = Visibility.Visible;
        try
        {
            var info = await YtDlp.Info(Video.UrlCompleta, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (info.EsLista)
            {
                // El enlace era una lista o una página con varios videos
                if (!enMini) w.Navegar(new VistaCanal(info), true);
                return;
            }
            _info = info;
            var inicio = Texto.InicioEnUrl(Video.Url) ?? 0;
            var soloAudio = Video.SoloAudio;
            Video = info.Video;
            Video.SoloAudio = soloAudio;
            if (w.Rep.Video == null || w.Rep.Video.Clave != Video.Clave || w.Rep.Info == null) w.Rep.Cargar(Video, info, inicio);
            Almacen.AgregarHistorial(Video);
            PintarDatos();
            w.Rep.ActualizarSubtitulosVisibles();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (cts.IsCancellationRequested) return;
            w.Rep.MostrarFallo(e is ErrorYtDlp ? e.Message : "No se pudo abrir el video: " + e.Message, () => Iniciar(enMini));
            PintarDatos();
        }
        finally { if (_cts == cts) _cargandoInfo.Visibility = Visibility.Collapsed; }
    }

    public void CargarEnMini()
    {
        var w = App.Ventana!;
        w.IrAMini();
        Iniciar(true);
    }

    // ───────────── Datos ─────────────
    void PintarDatos()
    {
        _titulo.Text = string.IsNullOrEmpty(Video.Titulo) || Video.Titulo == Video.Url ? (_cargandoInfo.Visibility == Visibility.Visible ? "Cargando…" : Video.Url) : Video.Titulo;
        PintarCanal();
        PintarAcciones();
        // Descripción
        var meta = new List<string>();
        if (Video.Vistas is { } vi) meta.Add(Texto.Vistas(vi));
        if (Video.Fecha is { } f) meta.Add(f.ToString("d 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-MX")));
        if (_info?.MeGusta is { } mg) meta.Add(Texto.Numero(mg) + " me gusta");
        if (Video.EsLocal && Video.Ruta != null) meta.Add(Video.Ruta);
        _meta.Text = string.Join("   ·   ", meta);
        var texto = _info?.Descripcion ?? "";
        PintarDescripcion(texto);
        _descripcionCaja.Visibility = meta.Count > 0 || texto.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Capítulos
        _capitulos.Children.Clear();
        if (_info?.Capitulos.Count > 1)
        {
            _capitulos.Margin = new Thickness(0, 18, 0, 0);
            _capitulos.Children.Add(UI.Encabezado(UI.Subtitulo($"Capítulos ({_info.Capitulos.Count})"), null, 8));
            foreach (var c in _info.Capitulos)
            {
                var cap = c;
                var b = new Button { Style = UI.Estilo("BtnTarjeta"), Margin = new Thickness(0, 0, 0, 2) };
                Acciones.SetTarjeta(b, false);
                var g = new Grid { Margin = new Thickness(6, 6, 6, 6) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var t = new TextBlock { Text = cap.InicioTexto, FontWeight = FontWeights.SemiBold };
                t.SetResourceReference(TextBlock.ForegroundProperty, "Acento");
                var n = new TextBlock { Text = cap.Titulo, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(n, 1);
                g.Children.Add(t); g.Children.Add(n);
                b.Content = g;
                b.Click += (_, _) => { App.Ventana?.Rep.Motor.Buscar(cap.Inicio); App.Ventana?.Rep.Motor.Reproducir(); };
                _capitulos.Children.Add(b);
            }
        }
        _ = CargarDelCanal();
    }

    void PintarCanal()
    {
        _filaCanal.Children.Clear();
        _filaCanal.Margin = new Thickness(0, 0, 24, 12);
        if (Video.EsLocal || string.IsNullOrEmpty(Video.Canal)) { _filaCanal.Visibility = Visibility.Collapsed; return; }
        _filaCanal.Visibility = Visibility.Visible;
        var avatar = new Grid { Width = 42, Height = 42, Margin = new Thickness(0, 0, 12, 0), Cursor = Cursors.Hand };
        var circ = new System.Windows.Shapes.Ellipse(); circ.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Superficie3");
        avatar.Children.Add(circ);
        var ini = new TextBlock { Text = Video.Inicial, FontWeight = FontWeights.Bold, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        ini.SetResourceReference(TextBlock.ForegroundProperty, "Tenue");
        avatar.Children.Add(ini);
        avatar.MouseLeftButtonUp += (_, _) => App.Ventana?.AbrirCanal(Video.CanalId, Video.Canal);
        _filaCanal.Children.Add(avatar);
        var nombres = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        var nombre = UI.Boton(Video.Canal, null, () => App.Ventana?.AbrirCanal(Video.CanalId, Video.Canal), "BtnEnlace");
        nombre.Foreground = UI.Pincel("Texto");
        nombre.SetResourceReference(Control.ForegroundProperty, "Texto");
        nombre.FontSize = 15;
        nombres.Children.Add(nombre);
        if (_info?.Seguidores is { } s) nombres.Children.Add(UI.Tenue(Texto.Suscriptores(s), 12));
        _filaCanal.Children.Add(nombres);
        if (!string.IsNullOrEmpty(Video.CanalId) && Video.EsYouTube)
        {
            var sus = Almacen.Suscrito(Video.CanalId);
            var b = UI.Boton(sus ? "Suscrito" : "Suscribirme", sus ? "" : null,
                () => App.Ventana?.AlternarSuscripcion(new Canal { Id = Video.CanalId, Nombre = Video.Canal }), sus ? "Btn" : "BtnPrimario");
            if (!sus) { b.SetResourceReference(Control.BackgroundProperty, "Texto"); b.SetResourceReference(Control.ForegroundProperty, "Fondo"); }
            b.VerticalAlignment = VerticalAlignment.Center;
            _filaCanal.Children.Add(b);
        }
    }

    void PintarAcciones()
    {
        _acciones.Children.Clear();
        var w = App.Ventana;
        if (w == null) return;
        void Agregar(Button b) { b.Margin = new Thickness(0, 0, 8, 12); _acciones.Children.Add(b); }
        if (!Video.EsLocal)
        {
            Agregar(UI.Boton("Descargar", "", () => w.Descargar(Video), "Btn", "Descargar en video o solo audio (Ctrl+D)"));
            var guardado = Almacen.EnVerDespues(Video);
            Agregar(UI.Boton(guardado ? "Guardado" : "Ver más tarde", guardado ? "" : "", () => { w.AlternarVerDespues(Video); PintarAcciones(); }, "Btn"));
            Agregar(UI.Boton("Copiar enlace", "", () => w.CopiarEnlace(Video.UrlCompleta), "Btn"));
            Agregar(UI.Boton("", "", () => { w.Rep.Motor.Pausar(); w.AbrirEnNavegador(Video.UrlCompleta); }, "Btn", "Abrir en el navegador"));
        }
        else
        {
            Agregar(UI.Boton("Mostrar en la carpeta", "", () => GestorDescargas.MostrarEnCarpeta(Video.Ruta), "Btn"));
            Agregar(UI.Boton("Abrir con otra app", "", () => { w.Rep.Motor.Pausar(); GestorDescargas.AbrirArchivo(Video.Ruta); }, "Btn"));
        }
    }

    // Descripción con enlaces y marcas de tiempo que se pueden tocar
    static readonly Regex RegexEnlaces = new(@"(https?://[^\s]+)|(?<![\d:])(\d{1,2}:\d{2}(?::\d{2})?)(?![\d:])", RegexOptions.Compiled);

    void PintarDescripcion(string texto)
    {
        _descripcion.Inlines.Clear();
        var lineas = texto.Split('\n');
        var corta = !_descripcionAbierta && (lineas.Length > 3 || texto.Length > 260);
        if (corta)
        {
            texto = string.Join("\n", lineas.Take(3));
            if (texto.Length > 260) texto = texto[..260];
            texto = texto.TrimEnd() + "…";
        }
        _verMas.Visibility = lineas.Length > 3 || (_info?.Descripcion.Length ?? 0) > 260 ? Visibility.Visible : Visibility.Collapsed;
        ((TextBlock)((StackPanel)_verMas.Content).Children[0]).Text = _descripcionAbierta ? "Mostrar menos" : "Mostrar más";
        _descripcion.Margin = new Thickness(0, texto.Length > 0 ? 8 : 0, 0, 0);
        int pos = 0;
        foreach (Match m in RegexEnlaces.Matches(texto))
        {
            if (m.Index > pos) _descripcion.Inlines.Add(new Run(texto[pos..m.Index]));
            var valor = m.Value;
            var h = new Hyperlink(new Run(valor)) { TextDecorations = null, Cursor = Cursors.Hand };
            h.SetResourceReference(TextElement.ForegroundProperty, "Acento");
            if (m.Groups[1].Success) h.Click += (_, _) => App.Ventana?.AbrirTexto(valor.TrimEnd('.', ',', ')'));
            else h.Click += (_, _) =>
            {
                var partes = valor.Split(':').Select(double.Parse).Reverse().ToArray();
                var s = partes[0] + partes[1] * 60 + (partes.Length > 2 ? partes[2] * 3600 : 0);
                App.Ventana?.Rep.Motor.Buscar(s);
                _scroll.ScrollToTop();
            };
            _descripcion.Inlines.Add(h);
            pos = m.Index + m.Length;
        }
        if (pos < texto.Length) _descripcion.Inlines.Add(new Run(texto[pos..]));
    }

    void AlternarDescripcion()
    {
        _descripcionAbierta = !_descripcionAbierta;
        PintarDescripcion(_info?.Descripcion ?? "");
    }

    void PintarCola()
    {
        var w = App.Ventana;
        if (w == null) return;
        _siguientes.Clear();
        for (int i = w.IndiceCola + 1; i < w.Cola.Count; i++) _siguientes.Add(w.Cola[i]);
        _bloqueCola.Visibility = _siguientes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_bloqueCola.Children[0] is Grid g && g.Children[0] is TextBlock t) t.Text = $"A continuación ({_siguientes.Count})";
    }

    string? _canalCargado;
    async Task CargarDelCanal()
    {
        if (Video.EsLocal || string.IsNullOrEmpty(Video.CanalId) || !Video.CanalId.StartsWith("UC") || _canalCargado == Video.CanalId) return;
        _canalCargado = Video.CanalId;
        try
        {
            var lista = await YtDlp.Feed(Video.CanalId, CancellationToken.None);
            _delCanal.Clear();
            foreach (var v in lista.Where(x => x.Clave != Video.Clave).Take(12)) _delCanal.Add(v);
            Almacen.MarcarProgreso(_delCanal);
            _bloqueCanal.Visibility = _delCanal.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_bloqueCanal.Children[0] is Grid g && g.Children[0] is TextBlock t) t.Text = "Más de " + Video.Canal;
        }
        catch { _canalCargado = null; }
    }
}
