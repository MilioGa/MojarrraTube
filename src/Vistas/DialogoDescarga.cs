using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MojarraTube;

/// <summary>Elegir cómo descargar: video en cierta calidad o solo audio (MP3/M4A/Opus), y dónde guardarlo.</summary>
public class DialogoDescarga : UserControl, IDisposable
{
    readonly Video _video;
    InfoVideo? _info;
    readonly CancellationTokenSource _cts = new();
    readonly StackPanel _cuerpo = new();
    readonly Grid _raiz = new();
    readonly Button _bajar;
    static string _modo = "video";
    int _calidad;
    string _formatoAudio;
    int _kbps = 320;
    bool _todaLaLista;
    string? _carpetaElegida;

    public DialogoDescarga(Video v, InfoVideo? info)
    {
        _video = v;
        _info = info;
        _calidad = Almacen.Ajustes.CalidadDescarga;
        _formatoAudio = Almacen.Ajustes.FormatoAudio;
        _todaLaLista = v.EsLista || (Texto.ListaYouTube(v.Url) != null && Texto.IdYouTube(v.Url) == null);

        _raiz.RowDefinitions.Add(new RowDefinition());
        _raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroll = new ScrollViewer { Content = _cuerpo, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        _cuerpo.Margin = new Thickness(24, 22, 24, 8);
        _raiz.Children.Add(scroll);

        var pie = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 10, 24, 20) };
        var cancelar = UI.Boton("Cancelar", null, () => App.Ventana?.CerrarDialogo(), "BtnFantasma");
        cancelar.Margin = new Thickness(0, 0, 10, 0);
        _bajar = UI.Boton("Descargar", "", Descargar, "BtnPrimario");
        _bajar.IsDefault = true;
        pie.Children.Add(cancelar);
        pie.Children.Add(_bajar);
        Grid.SetRow(pie, 1);
        _raiz.Children.Add(pie);
        Content = _raiz;

        if (_info != null && !_todaLaLista) Pintar();
        else Cargar();
    }

    public void Dispose() => _cts.Cancel();

    async void Cargar()
    {
        _bajar.IsEnabled = false;
        _cuerpo.Children.Clear();
        _cuerpo.Children.Add(Cabecera(_video.Titulo == _video.Url ? "Leyendo el enlace…" : _video.Titulo, _video.Canal, _video.Miniatura, null));
        var carga = new StackPanel { Margin = new Thickness(0, 26, 0, 20) };
        carga.Children.Add(new ProgressBar { Style = UI.Estilo("BarraFina"), IsIndeterminate = true, Width = 220 });
        carga.Children.Add(new TextBlock { Text = "Buscando los formatos disponibles…", Style = UI.Estilo("TextoTenue"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });
        _cuerpo.Children.Add(carga);
        try
        {
            _info = await YtDlp.Info(_video.UrlCompleta, _cts.Token, _todaLaLista);
            if (_info.EsLista) _todaLaLista = true;
            Pintar();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            _cuerpo.Children.Clear();
            var p = new PanelEstado { Padding = new Thickness(10, 20, 10, 10) };
            p.Error(e, Cargar);
            _cuerpo.Children.Add(p);
        }
    }

    FrameworkElement Cabecera(string titulo, string canal, string miniatura, double? duracion)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mini = new Grid { Width = 150, Height = 84, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
        mini.SetResourceReference(Panel.BackgroundProperty, "Esqueleto");
        Forma.SetRadio(mini, 10);
        var img = new Image { Stretch = Stretch.UniformToFill };
        Img.SetFuente(img, miniatura); Img.SetAncho(img, 320);
        mini.Children.Add(img);
        if (duracion is > 0)
            mini.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0xD9, 0, 0, 0)), CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Child = new TextBlock { Text = Texto.Tiempo(duracion.Value), Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold } });
        g.Children.Add(mini);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = "Descargar", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = UI.Pincel("Acento") });
        t.Children.Add(new TextBlock { Text = titulo, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 66, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 2) });
        if (!string.IsNullOrEmpty(canal)) t.Children.Add(UI.Tenue(canal, 13));
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var x = UI.BotonIcono("", "Cerrar (Esc)", () => App.Ventana?.CerrarDialogo());
        x.VerticalAlignment = VerticalAlignment.Top;
        x.Margin = new Thickness(8, -8, -10, 0);
        Grid.SetColumn(x, 2);
        g.Children.Add(x);
        return g;
    }

    void Pintar()
    {
        if (_info == null) return;
        _cuerpo.Children.Clear();
        var v = _info.Video;
        var esLista = _todaLaLista || _info.EsLista;
        _cuerpo.Children.Add(Cabecera(esLista && _info.EsLista ? _info.TituloLista : v.Titulo, v.Canal, v.Miniatura.Length > 0 ? v.Miniatura : _video.Miniatura, esLista ? null : v.Duracion));

        // ¿Lista completa o solo este video?
        var puedeLista = Texto.ListaYouTube(_video.Url) != null && Texto.IdYouTube(_video.Url) != null;
        if (_info.EsLista)
            _cuerpo.Children.Add(Nota("", $"Es una lista con {_info.Entradas.Count} videos. Se descargarán todos en una carpeta con el nombre de la lista."));
        else if (puedeLista)
        {
            _cuerpo.Children.Add(Etiqueta("¿Qué descargar?"));
            _cuerpo.Children.Add(UI.Chips(new[] { "Solo este video", "Toda la lista" }, _todaLaLista ? 1 : 0, i => { _todaLaLista = i == 1; }, out _, true));
        }

        _cuerpo.Children.Add(Etiqueta("Formato"));
        var opciones = new StackPanel();
        _cuerpo.Children.Add(UI.Chips(new[] { "Video (MP4)", "Solo audio" }, _modo == "audio" ? 1 : 0, i => { _modo = i == 1 ? "audio" : "video"; PintarOpciones(opciones); }, out _, true));
        _cuerpo.Children.Add(opciones);
        PintarOpciones(opciones);

        // Carpeta
        _cuerpo.Children.Add(Etiqueta("Se guardará en"));
        var fila = new Grid();
        fila.ColumnDefinitions.Add(new ColumnDefinition());
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ruta = new TextBlock { Text = Carpeta, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        ruta.SetResourceReference(TextBlock.ForegroundProperty, "Tenue");
        fila.Children.Add(ruta);
        var cambiar = UI.Boton("Cambiar…", null, () =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "¿Dónde guardar?", InitialDirectory = Directory.Exists(Carpeta) ? Carpeta : "" };
            if (d.ShowDialog() == true) { _carpetaElegida = d.FolderName; ruta.Text = Carpeta; }
        }, "BtnEnlace");
        Grid.SetColumn(cambiar, 1);
        fila.Children.Add(cambiar);
        _cuerpo.Children.Add(fila);
        _cuerpo.Tag = ruta;
        _bajar.IsEnabled = true;
        Dispatcher.BeginInvoke(() => _bajar.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    string Carpeta => _carpetaElegida ?? (_modo == "audio" ? Almacen.Ajustes.CarpetaAudio : Almacen.Ajustes.CarpetaVideos);

    void PintarOpciones(StackPanel panel)
    {
        panel.Children.Clear();
        panel.Margin = new Thickness(0, 4, 0, 0);
        if (_cuerpo.Tag is TextBlock ruta) ruta.Text = Carpeta;
        var dur = _info?.Video.Duracion;
        var formatos = _info?.Formatos ?? new();
        var grupo = "o" + Guid.NewGuid().ToString("N")[..5];
        if (_modo == "video")
        {
            var alturas = formatos.Count > 0 && !_todaLaLista ? YtDlpDatos.Calidades(formatos).Where(a => a >= 144).ToList() : new List<int> { 2160, 1440, 1080, 720, 480, 360 };
            if (alturas.Count == 0) alturas.Add(0);
            if (!alturas.Contains(_calidad)) _calidad = alturas.Where(a => _calidad == 0 || a <= _calidad).DefaultIfEmpty(alturas[0]).Max();
            if (_calidad == 0) _calidad = alturas[0];
            foreach (var a in alturas)
            {
                var alto = a;
                var tam = formatos.Count > 0 && !_todaLaLista ? YtDlpDatos.TamanoEstimado(formatos, alto, dur) : 0;
                var etiqueta = alto >= 2160 ? "4K" : alto >= 1440 ? "2K" : alto >= 1080 ? "Full HD" : alto >= 720 ? "HD" : "";
                panel.Children.Add(Fila(grupo, alto == 0 ? "La mejor disponible" : YtDlpDatos.NombreCalidad(alto), etiqueta, tam > 0 ? "≈ " + Texto.Bytes(tam) : "", _calidad == alto, () => _calidad = alto));
            }
            panel.Children.Add(UI.Tenue("Se guarda como MP4 compatible con todo. En 1440p y 4K el video usa VP9/AV1.", 12));
        }
        else
        {
            var opciones = new (string fmt, int kbps, string nombre, string etiqueta, double kbpsEst)[]
            {
                ("mp3", 320, "MP3 · 320 kbps", "Máxima", 320),
                ("mp3", 192, "MP3 · 192 kbps", "Ligero", 192),
                ("m4a", 0, "M4A (AAC) · original", "Sin pérdida extra", 130),
                ("opus", 0, "Opus · original", "Más pequeño", 130),
            };
            foreach (var o in opciones)
            {
                var op = o;
                var tam = dur is > 0 && !_todaLaLista ? (long)(op.kbpsEst * 1000 / 8 * dur.Value) : 0;
                var marcado = _formatoAudio == op.fmt && (op.fmt != "mp3" || _kbps == op.kbps);
                panel.Children.Add(Fila(grupo, op.nombre, op.etiqueta, tam > 0 ? "≈ " + Texto.Bytes(tam) : "", marcado, () => { _formatoAudio = op.fmt; _kbps = op.kbps; }));
            }
            panel.Children.Add(UI.Tenue("Incluye la portada y los datos (título, artista) en el archivo.", 12));
        }
        foreach (var c in panel.Children.OfType<TextBlock>()) c.Margin = new Thickness(2, 2, 0, 0);
    }

    static RadioButton Fila(string grupo, string titulo, string etiqueta, string tamano, bool marcado, Action alElegir)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = titulo, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        if (etiqueta.Length > 0)
        {
            var e = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = etiqueta, FontSize = 11, FontWeight = FontWeights.SemiBold } };
            e.SetResourceReference(Border.BackgroundProperty, "Superficie3");
            Grid.SetColumn(e, 1);
            g.Children.Add(e);
        }
        var t = new TextBlock { Text = tamano, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Tenue");
        Grid.SetColumn(t, 2);
        g.Children.Add(t);
        var rb = new RadioButton { Style = UI.Estilo("OpcionFila"), GroupName = grupo, IsChecked = marcado, Content = g };
        System.Windows.Automation.AutomationProperties.SetName(rb, titulo + " " + tamano);
        rb.Checked += (_, _) => alElegir();
        return rb;
    }

    static TextBlock Etiqueta(string t) => new() { Text = t, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) };

    static Border Nota(string icono, string texto)
    {
        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 14, 0, 0) };
        b.SetResourceReference(Border.BackgroundProperty, "Superficie2");
        var sp = UI.Horizontal(UI.Icono(icono, 15, "Acento"), new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 0, 0, 0), FontSize = 13, MaxWidth = 440 });
        b.Child = sp;
        return b;
    }

    void Descargar()
    {
        if (_info == null) return;
        var v = _info.Video;
        var esLista = _todaLaLista || _info.EsLista;
        var url = esLista ? _video.Url : (v.Url.Length > 0 ? v.UrlCompleta : _video.Url);
        var carpeta = Carpeta;
        try { Directory.CreateDirectory(carpeta); }
        catch { App.Ventana?.Aviso("No se puede usar esa carpeta. Elige otra.", ""); return; }
        var r = new RegistroDescarga
        {
            Url = url,
            Titulo = esLista && _info.EsLista ? _info.TituloLista : v.Titulo,
            Miniatura = v.Miniatura.Length > 0 ? v.Miniatura : _video.Miniatura,
            Canal = v.Canal,
            Duracion = v.Duracion,
            Modo = _modo,
            Calidad = _modo == "audio" ? (_formatoAudio == "mp3" ? _kbps : 0) : _calidad,
            FormatoAudio = _formatoAudio,
            Carpeta = carpeta,
            EsLista = esLista,
        };
        if (r.Miniatura.Contains("i.ytimg.com") && r.Miniatura.Contains("maxres")) r.Miniatura = r.Miniatura.Replace("maxresdefault", "mqdefault");
        GestorDescargas.Instancia.Agregar(r);
        App.Ventana?.CerrarDialogo();
        App.Ventana?.Aviso(_modo == "audio" ? $"Descargando audio: {r.Titulo}" : $"Descargando: {r.Titulo}", "", ("Ver descargas", () => App.Ventana?.Navegar(new VistaDescargas())));
    }
}
