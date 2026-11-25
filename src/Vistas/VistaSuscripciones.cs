using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MojarraTube;

/// <summary>Lo último de los canales a los que te suscribiste (guardados en tu equipo, sin cuenta de Google).</summary>
public class VistaSuscripciones : UserControl, IVista
{
    public string Seccion => "suscripciones";
    readonly StackPanel _raiz = new();
    readonly WrapPanel _canales = new() { Margin = new Thickness(0, 0, 0, 22) };
    readonly ObservableCollection<Video> _videos = new();
    readonly ItemsControl _esqueleto;
    readonly PanelEstado _estado = new();
    CancellationTokenSource? _cts;

    public VistaSuscripciones()
    {
        Content = UI.Desplazable(_raiz);
        SizeChanged += (_, _) => _raiz.Margin = UI.Margen(ActualWidth);
        var recargar = UI.Boton("Actualizar", "", () => { VistaInicio.OlvidarNovedades(); Cargar(); }, "BtnFantasma", "Buscar videos nuevos");
        _raiz.Children.Add(UI.Encabezado(UI.Titulo("Suscripciones"), recargar, 16));
        _raiz.Children.Add(_canales);
        _esqueleto = UI.Esqueletos(8);
        _raiz.Children.Add(_esqueleto);
        _raiz.Children.Add(_estado);
        _raiz.Children.Add(UI.Rejilla(_videos));
        Cargar();
    }

    public void AlMostrar()
    {
        Almacen.MarcarProgreso(_videos);
        PintarCanales();
        if (App.Ventana != null) App.Ventana.SuscripcionCambio += AlCambiar;
    }
    public void AlOcultar() { if (App.Ventana != null) App.Ventana.SuscripcionCambio -= AlCambiar; }
    void AlCambiar() { PintarCanales(); VistaInicio.OlvidarNovedades(); Cargar(); }

    void PintarCanales()
    {
        _canales.Children.Clear();
        foreach (var c in Almacen.Suscripciones.OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase))
        {
            var canal = c;
            var b = new Button { Style = UI.Estilo("Btn"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(6, 5, 14, 5), ToolTip = "Abrir canal (clic derecho: más opciones)" };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var av = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 8, 0) };
            var el = new System.Windows.Shapes.Ellipse(); el.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Fondo");
            av.Children.Add(el);
            av.Children.Add(new TextBlock { Text = canal.Inicial, FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            if (!string.IsNullOrEmpty(canal.Avatar)) { var img = new Image { Clip = new EllipseGeometry(new Point(13, 13), 13, 13), Stretch = Stretch.UniformToFill }; Img.SetFuente(img, canal.Avatar); Img.SetAncho(img, 64); av.Children.Add(img); }
            sp.Children.Add(av);
            sp.Children.Add(new TextBlock { Text = canal.Nombre, VerticalAlignment = VerticalAlignment.Center });
            b.Content = sp;
            b.Click += (_, _) => App.Ventana?.AbrirCanal(canal.Id, canal.Nombre);
            b.MouseRightButtonUp += (_, e) =>
            {
                var m = new ContextMenu();
                var abrir = new MenuItem { Header = "Abrir canal", Tag = "" }; abrir.Click += (_, _) => App.Ventana?.AbrirCanal(canal.Id, canal.Nombre);
                var quitar = new MenuItem { Header = "Cancelar suscripción", Tag = "" }; quitar.Click += (_, _) => App.Ventana?.AlternarSuscripcion(canal);
                m.Items.Add(abrir); m.Items.Add(quitar);
                m.IsOpen = true; e.Handled = true;
            };
            _canales.Children.Add(b);
        }
        _canales.Visibility = _canales.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    async void Cargar()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _estado.Ocultar();
        if (Almacen.Suscripciones.Count == 0)
        {
            _videos.Clear();
            _esqueleto.Visibility = Visibility.Collapsed;
            _estado.Mostrar("", "Aún no sigues ningún canal",
                "Abre un canal o un video y toca «Suscribirme». Tus suscripciones se guardan en tu equipo; no hace falta una cuenta.",
                ("Explorar videos", () => App.Ventana?.Navegar(new VistaInicio())));
            return;
        }
        if (_videos.Count == 0) _esqueleto.Visibility = Visibility.Visible;
        try
        {
            var lista = await CargarFeed(cts.Token);
            if (cts.IsCancellationRequested) return;
            _videos.Clear();
            foreach (var v in lista) _videos.Add(v);
            Almacen.MarcarProgreso(_videos);
            if (_videos.Count == 0) _estado.Mostrar("", "No hay videos nuevos", "Tus canales no han publicado nada recientemente.");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!cts.IsCancellationRequested) _estado.Error(e, Cargar); }
        finally { if (_cts == cts) _esqueleto.Visibility = Visibility.Collapsed; }
    }

    /// <summary>Junta los feeds de todos los canales (6 a la vez) y los ordena del más nuevo al más viejo.</summary>
    public static async Task<List<Video>> CargarFeed(CancellationToken ct)
    {
        var turnos = new SemaphoreSlim(6);
        var errores = 0;
        var tareas = Almacen.Suscripciones.ToList().Select(async c =>
        {
            await turnos.WaitAsync(ct);
            try { return await YtDlp.Feed(c.Id, ct); }
            catch (OperationCanceledException) { throw; }
            catch { Interlocked.Increment(ref errores); return new List<Video>(); }
            finally { turnos.Release(); }
        }).ToList();
        var todos = (await Task.WhenAll(tareas)).SelectMany(x => x).ToList();
        if (todos.Count == 0 && errores > 0) throw new System.Net.Http.HttpRequestException("Sin conexión");
        return todos.OrderByDescending(v => v.Fecha ?? DateTime.MinValue).Take(120).ToList();
    }
}
