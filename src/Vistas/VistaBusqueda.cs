using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace MojarraTube;

/// <summary>Resultados de búsqueda en YouTube, con orden y carga continua al bajar.</summary>
public class VistaBusqueda : UserControl, IVista
{
    public string Seccion => "";
    public string Consulta { get; }

    readonly StackPanel _raiz = new();
    readonly ScrollViewer _scroll;
    readonly ObservableCollection<Video> _videos = new();
    readonly ItemsControl _esqueleto;
    readonly PanelEstado _estado = new();
    readonly ProgressBar _mas;
    YtDlp.Orden _orden = YtDlp.Orden.Relevancia;
    int _cantidad = 30;
    bool _cargando, _fin;
    CancellationTokenSource? _cts;

    public VistaBusqueda(string q)
    {
        Consulta = q;
        _scroll = UI.Desplazable(_raiz);
        Content = _scroll;
        SizeChanged += (_, _) => _raiz.Margin = UI.Margen(ActualWidth);

        var cab = new StackPanel();
        cab.Children.Add(UI.Titulo($"Resultados para «{q}»", 22));
        var chips = UI.Chips(new[] { "Más relevantes", "Más recientes", "Más vistos" }, 0, i =>
        {
            _orden = (YtDlp.Orden)i; _cantidad = 30; _fin = false; Cargar();
        }, out _);
        chips.Margin = new Thickness(0, 14, 0, 0);
        cab.Children.Add(chips);
        cab.Margin = new Thickness(0, 6, 0, 16);
        _raiz.Children.Add(cab);
        _esqueleto = UI.Esqueletos(12);
        _raiz.Children.Add(_esqueleto);
        _raiz.Children.Add(_estado);
        _raiz.Children.Add(UI.Rejilla(_videos));
        _mas = new ProgressBar { Style = UI.Estilo("BarraFina"), IsIndeterminate = true, Width = 160, Margin = new Thickness(0, 30, 0, 0), Visibility = Visibility.Collapsed };
        _raiz.Children.Add(_mas);
        UI.AlLlegarAlFinal(_scroll, () => { if (!_cargando && !_fin && _videos.Count > 0 && _cantidad < 150) { _cantidad += 30; Cargar(true); } });
        Cargar();
    }

    public void Recargar() { _cantidad = 30; _fin = false; Cargar(); }

    public void AlMostrar() => Almacen.MarcarProgreso(_videos);
    public void AlOcultar() { }

    async void Cargar(bool mas = false)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _cargando = true;
        _estado.Ocultar();
        if (!mas) { _videos.Clear(); _esqueleto.Visibility = Visibility.Visible; _scroll.ScrollToTop(); }
        else _mas.Visibility = Visibility.Visible;
        try
        {
            var lista = await YtDlp.Buscar(Consulta, _cantidad, _orden, cts.Token);
            if (cts.IsCancellationRequested) return;
            var claves = new HashSet<string>(_videos.Select(v => v.Clave));
            var nuevos = lista.Where(v => claves.Add(v.Clave)).ToList();
            foreach (var v in nuevos) _videos.Add(v);
            if (nuevos.Count == 0 || lista.Count < _cantidad - 5) _fin = true;
            Almacen.MarcarProgreso(_videos);
            if (_videos.Count == 0)
                _estado.Mostrar("", "Sin resultados", $"No se encontró nada para «{Consulta}». Revisa la ortografía o usa otras palabras.");
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (cts.IsCancellationRequested) return;
            if (_videos.Count == 0) _estado.Error(e, () => Cargar());
            else { _fin = true; App.Ventana?.Aviso(e.Message, ""); }
        }
        finally
        {
            if (_cts == cts)
            {
                _cargando = false;
                _esqueleto.Visibility = Visibility.Collapsed;
                _mas.Visibility = Visibility.Collapsed;
            }
        }
    }
}
