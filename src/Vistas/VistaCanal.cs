using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MojarraTube;

/// <summary>Un canal de YouTube o una lista de reproducción (de YouTube o de otra página).</summary>
public class VistaCanal : UserControl, IVista
{
    public string Seccion => "";
    readonly string _destino;
    readonly bool _esLista;
    string _nombre;
    string _canalId = "";
    readonly StackPanel _raiz = new();
    readonly ScrollViewer _scroll;
    readonly StackPanel _cabecera = new();
    readonly ObservableCollection<Video> _videos = new();
    readonly ItemsControl _esqueleto;
    readonly PanelEstado _estado = new();
    readonly ProgressBar _mas;
    Pagina? _pagina;
    int _cantidad = 30;
    bool _cargando, _fin;
    CancellationTokenSource? _cts;

    public VistaCanal(string destino, string nombre, bool esLista = false)
    {
        _destino = destino;
        _nombre = nombre;
        _esLista = esLista || destino.Contains("list=");
        if (destino.StartsWith("UC") && !destino.Contains('/')) _canalId = destino;
        _scroll = UI.Desplazable(_raiz);
        Content = _scroll;
        SizeChanged += (_, _) => _raiz.Margin = UI.Margen(ActualWidth);
        _raiz.Children.Add(_cabecera);
        _esqueleto = UI.Esqueletos(12);
        _raiz.Children.Add(_esqueleto);
        _raiz.Children.Add(_estado);
        _raiz.Children.Add(UI.Rejilla(_videos));
        _mas = new ProgressBar { Style = UI.Estilo("BarraFina"), IsIndeterminate = true, Width = 160, Margin = new Thickness(0, 30, 0, 0), Visibility = Visibility.Collapsed };
        _raiz.Children.Add(_mas);
        UI.AlLlegarAlFinal(_scroll, () => { if (!_cargando && !_fin && _videos.Count >= _cantidad - 2) { _cantidad += 30; Cargar(true); } });
        PintarCabecera();
        Cargar();
    }

    /// <summary>Desde un enlace que resultó ser una lista (p. ej. una página con varios videos).</summary>
    public VistaCanal(InfoVideo info) : this(info.Video.Url, info.TituloLista, true)
    {
        _cts?.Cancel();
        _pagina = new Pagina { Titulo = info.TituloLista, Videos = info.Entradas };
        _fin = true;
        Poner(info.Entradas);
        _esqueleto.Visibility = Visibility.Collapsed;
        PintarCabecera();
    }

    public void AlMostrar()
    {
        Almacen.MarcarProgreso(_videos);
        if (App.Ventana != null) App.Ventana.SuscripcionCambio += PintarCabecera;
    }
    public void AlOcultar() { if (App.Ventana != null) App.Ventana.SuscripcionCambio -= PintarCabecera; }

    void PintarCabecera()
    {
        _cabecera.Children.Clear();
        _cabecera.Margin = new Thickness(0, 6, 0, 22);
        var titulo = !string.IsNullOrEmpty(_pagina?.Titulo) ? _pagina!.Titulo : _nombre;
        if (!string.IsNullOrEmpty(_pagina?.Banner))
        {
            var img = new Image { Stretch = Stretch.UniformToFill, Height = 150 };
            Img.SetFuente(img, _pagina!.Banner);
            Img.SetAncho(img, 1600);
            var marco = new Border { Child = img, Margin = new Thickness(0, 0, 0, 20), Height = 150 };
            Forma.SetRadio(marco, 14);
            marco.SetResourceReference(Border.BackgroundProperty, "Esqueleto");
            _cabecera.Children.Add(marco);
        }
        var fila = new Grid();
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fila.ColumnDefinitions.Add(new ColumnDefinition());
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // Avatar
        var av = new Grid { Width = 84, Height = 84, Margin = new Thickness(0, 0, 18, 0) };
        var c = new System.Windows.Shapes.Ellipse(); c.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Superficie3");
        av.Children.Add(c);
        if (_esLista) av.Children.Add(UI.Icono("", 30, "Tenue"));
        else
        {
            var ini = new TextBlock { Text = string.IsNullOrWhiteSpace(titulo) ? "?" : titulo.Trim()[0].ToString().ToUpper(), FontSize = 34, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ini.SetResourceReference(TextBlock.ForegroundProperty, "Tenue");
            av.Children.Add(ini);
            if (!string.IsNullOrEmpty(_pagina?.Avatar))
            {
                var img = new Image { Stretch = Stretch.UniformToFill, Clip = new EllipseGeometry(new Point(42, 42), 42, 42) };
                Img.SetFuente(img, _pagina!.Avatar); Img.SetAncho(img, 168);
                av.Children.Add(img);
            }
        }
        fila.Children.Add(av);
        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textos.Children.Add(UI.Titulo(string.IsNullOrWhiteSpace(titulo) ? (_esLista ? "Lista" : "Canal") : titulo, 26));
        var detalles = new List<string>();
        if (_pagina?.Seguidores is { } seg) detalles.Add(Texto.Suscriptores(seg));
        if (_esLista && _videos.Count > 0) detalles.Add(_videos.Count + (_fin ? "" : "+") + " videos");
        if (detalles.Count > 0) textos.Children.Add(UI.Tenue(string.Join("  ·  ", detalles), 14));
        if (!string.IsNullOrWhiteSpace(_pagina?.Descripcion))
        {
            var d = UI.Tenue(_pagina!.Descripcion.Split('\n')[0], 13);
            d.TextTrimming = TextTrimming.CharacterEllipsis; d.TextWrapping = TextWrapping.NoWrap; d.Margin = new Thickness(0, 4, 0, 0);
            d.ToolTip = _pagina.Descripcion.Length > 600 ? _pagina.Descripcion[..600] + "…" : _pagina.Descripcion;
            textos.Children.Add(d);
        }
        Grid.SetColumn(textos, 1);
        fila.Children.Add(textos);

        var botones = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var w = App.Ventana;
        if (_videos.Count > 0)
        {
            var b = UI.Boton("Reproducir todo", "", () => w?.PonerCola(_videos.ToList()), "BtnPrimario");
            b.Margin = new Thickness(8, 4, 0, 4);
            botones.Children.Add(b);
        }
        if (_esLista)
        {
            var b = UI.Boton("Descargar lista", "", () => w?.Descargar(new Video { Url = _destino, Titulo = titulo, Tipo = TipoItem.Lista, Miniatura = _videos.FirstOrDefault()?.Miniatura ?? "" }), "Btn");
            b.Margin = new Thickness(8, 4, 0, 4);
            botones.Children.Add(b);
        }
        else if (!string.IsNullOrEmpty(_canalId))
        {
            var sus = Almacen.Suscrito(_canalId);
            var b = UI.Boton(sus ? "Suscrito" : "Suscribirme", sus ? "" : null, () =>
                w?.AlternarSuscripcion(new Canal { Id = _canalId, Nombre = titulo, Avatar = _pagina?.Avatar ?? "" }), sus ? "Btn" : "BtnPrimario");
            if (!sus) { b.SetResourceReference(Control.BackgroundProperty, "Texto"); b.SetResourceReference(Control.ForegroundProperty, "Fondo"); }
            b.Margin = new Thickness(8, 4, 0, 4);
            botones.Children.Add(b);
        }
        var web = UI.BotonIcono("", "Abrir en el navegador", () => w?.AbrirEnNavegador(_destino.StartsWith("http") ? _destino : "https://www.youtube.com/channel/" + _destino));
        web.Margin = new Thickness(6, 4, 0, 4);
        botones.Children.Add(web);
        Grid.SetColumn(botones, 2);
        // En ventanas angostas los botones bajan a otra fila
        if (ActualWidth > 0 && ActualWidth < 760) { Grid.SetColumn(botones, 1); Grid.SetRow(botones, 1); fila.RowDefinitions.Add(new RowDefinition()); fila.RowDefinitions.Add(new RowDefinition()); botones.HorizontalAlignment = HorizontalAlignment.Left; botones.Margin = new Thickness(-8, 8, 0, 0); }
        fila.Children.Add(botones);
        _cabecera.Children.Add(fila);
    }

    async void Cargar(bool mas = false)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _cargando = true;
        _estado.Ocultar();
        if (!mas) { _videos.Clear(); _esqueleto.Visibility = Visibility.Visible; }
        else _mas.Visibility = Visibility.Visible;
        try
        {
            var p = await YtDlp.Canal(_destino, _cantidad, cts.Token);
            if (cts.IsCancellationRequested) return;
            _pagina = p;
            if (!string.IsNullOrEmpty(p.CanalId) && p.CanalId.StartsWith("UC") && !_esLista) _canalId = p.CanalId;
            if (string.IsNullOrEmpty(_nombre)) _nombre = p.Titulo;
            var antes = _videos.Count;
            Poner(p.Videos);
            if (p.Videos.Count < _cantidad || _videos.Count == antes) _fin = true;
            PintarCabecera();
            if (_videos.Count == 0) _estado.Mostrar("", _esLista ? "Esta lista está vacía" : "Este canal no tiene videos", "");
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (cts.IsCancellationRequested) return;
            if (_videos.Count == 0) _estado.Error(e, () => Cargar());
            else _fin = true;
        }
        finally
        {
            if (_cts == cts) { _cargando = false; _esqueleto.Visibility = Visibility.Collapsed; _mas.Visibility = Visibility.Collapsed; }
        }
    }

    void Poner(IEnumerable<Video> lista)
    {
        var claves = new HashSet<string>(_videos.Select(v => v.Clave));
        foreach (var v in lista) if (claves.Add(v.Clave)) _videos.Add(v);
        Almacen.MarcarProgreso(_videos);
    }
}
