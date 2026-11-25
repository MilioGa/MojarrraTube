using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace MojarraTube;

/// <summary>Inicio: seguir viendo, novedades de tus canales y videos por tema.</summary>
public class VistaInicio : UserControl, IVista
{
    public string Seccion => "inicio";

    static readonly (string nombre, string consulta, YtDlp.Orden orden)[] Temas =
    {
        ("Para ti", "", YtDlp.Orden.Relevancia),
        ("Música", "música en tendencia", YtDlp.Orden.Relevancia),
        ("Videojuegos", "videojuegos novedades", YtDlp.Orden.Relevancia),
        ("Noticias", "noticias de hoy", YtDlp.Orden.Recientes),
        ("Tecnología", "tecnología novedades", YtDlp.Orden.Relevancia),
        ("Deportes", "deportes resumen", YtDlp.Orden.Recientes),
        ("Ciencia", "ciencia divulgación", YtDlp.Orden.Relevancia),
        ("Cocina", "recetas de cocina fáciles", YtDlp.Orden.Relevancia),
        ("Comedia", "comedia", YtDlp.Orden.Relevancia),
        ("Podcasts", "podcast en español", YtDlp.Orden.Relevancia),
        ("Tráilers", "tráiler oficial", YtDlp.Orden.Recientes),
        ("Aprender", "aprende tutorial", YtDlp.Orden.Relevancia),
    };

    static int _tema;
    static readonly Dictionary<int, List<Video>> Cache = new();
    static List<Video>? _cacheSuscripciones;

    readonly StackPanel _raiz = new();
    readonly ScrollViewer _scroll;
    readonly ObservableCollection<Video> _videos = new();
    readonly ItemsControl _rejilla;
    readonly ItemsControl _esqueleto;
    readonly PanelEstado _estado = new();
    readonly StackPanel _seguir = new();
    readonly StackPanel _novedades = new();
    readonly TextBlock _tituloTema;
    readonly Button _masBtn;
    CancellationTokenSource? _cts;
    int _cantidad = 36;
    bool _cargando;

    public VistaInicio()
    {
        _scroll = UI.Desplazable(_raiz);
        Content = _scroll;
        SizeChanged += (_, _) => _raiz.Margin = UI.Margen(ActualWidth);

        _raiz.Children.Add(UI.Chips(Temas.Select(t => t.nombre).ToList(), _tema, i => { _tema = i; _cantidad = 36; Cargar(); }, out _));
        ((FrameworkElement)_raiz.Children[0]).Margin = new Thickness(0, 4, 0, 18);
        _raiz.Children.Add(_seguir);
        _raiz.Children.Add(_novedades);
        _tituloTema = UI.Subtitulo("");
        _raiz.Children.Add(UI.Encabezado(_tituloTema));
        _esqueleto = UI.Esqueletos(12);
        _raiz.Children.Add(_esqueleto);
        _raiz.Children.Add(_estado);
        _rejilla = UI.Rejilla(_videos);
        _raiz.Children.Add(_rejilla);
        _masBtn = UI.Boton("Mostrar más", "", () => { _cantidad += 24; Cargar(true); }, "BtnFantasma");
        _masBtn.HorizontalAlignment = HorizontalAlignment.Center;
        _masBtn.Margin = new Thickness(0, 28, 0, 0);
        _masBtn.Visibility = Visibility.Collapsed;
        _raiz.Children.Add(_masBtn);
        UI.AlLlegarAlFinal(_scroll, () => { if (!_cargando && _videos.Count > 0 && _cantidad < 120 && _videos.Count >= _cantidad - 12) { _cantidad += 24; Cargar(true); } });
        Cargar();
    }

    public void AlMostrar()
    {
        PintarSeguirViendo();
        _ = PintarNovedades();
        Almacen.MarcarProgreso(_videos);
    }
    public void AlOcultar() { }

    void PintarSeguirViendo()
    {
        _seguir.Children.Clear();
        var lista = Almacen.Historial.Where(h => h.Posicion > 0 && h.Video.Tipo == TipoItem.Video && (!h.Video.EsLocal || System.IO.File.Exists(h.Video.Ruta))).Take(8)
            .Select(h => { var v = h.Video.Copia(); return v; }).ToList();
        if (lista.Count == 0) return;
        Almacen.MarcarProgreso(lista);
        _seguir.Children.Add(UI.Encabezado(UI.Subtitulo("Seguir viendo"), UI.Boton("Historial", null, () => App.Ventana?.Navegar(new VistaBiblioteca(1)), "BtnEnlace")));
        _seguir.Children.Add(UI.Rejilla(lista, 220, 1));
        _seguir.Children.Add(UI.Separador(26, 26));
    }

    async Task PintarNovedades()
    {
        if (Almacen.Suscripciones.Count == 0) { _novedades.Children.Clear(); return; }
        try
        {
            _cacheSuscripciones ??= await VistaSuscripciones.CargarFeed(CancellationToken.None);
            var lista = _cacheSuscripciones.Take(8).ToList();
            _novedades.Children.Clear();
            if (lista.Count == 0) return;
            Almacen.MarcarProgreso(lista);
            _novedades.Children.Add(UI.Encabezado(UI.Subtitulo("Novedades de tus canales"), UI.Boton("Ver todo", null, () => App.Ventana?.Navegar(new VistaSuscripciones()), "BtnEnlace")));
            _novedades.Children.Add(UI.Rejilla(lista, 220, 1));
            _novedades.Children.Add(UI.Separador(26, 26));
        }
        catch { }
    }

    public static void OlvidarNovedades() => _cacheSuscripciones = null;

    async void Cargar(bool mas = false)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var tema = _tema;
        _tituloTema.Text = tema == 0 ? "Para ti" : Temas[tema].nombre;
        _estado.Ocultar();
        if (!mas && Cache.TryGetValue(tema, out var guardado) && guardado.Count > 0)
        {
            Poner(guardado);
            return;
        }
        _cargando = true;
        if (!mas) { _videos.Clear(); _esqueleto.Visibility = Visibility.Visible; }
        _masBtn.IsEnabled = false;
        try
        {
            List<Video> lista;
            if (tema == 0) lista = await ParaTi(_cantidad, cts.Token);
            else lista = await YtDlp.Buscar(Temas[tema].consulta, _cantidad, Temas[tema].orden, cts.Token);
            if (cts.IsCancellationRequested) return;
            lista = lista.Where(v => v.Tipo == TipoItem.Video).GroupBy(v => v.Clave).Select(g => g.First()).ToList();
            Cache[tema] = lista;
            Poner(lista);
            if (lista.Count == 0) _estado.Mostrar("", "No hay videos para mostrar", "Prueba con otro tema o busca algo arriba.");
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (cts.IsCancellationRequested) return;
            _esqueleto.Visibility = Visibility.Collapsed;
            if (_videos.Count == 0) _estado.Error(e, () => Cargar());
            else App.Ventana?.Aviso(e.Message, "");
        }
        finally { if (_cts == cts) { _cargando = false; _masBtn.IsEnabled = true; } }
    }

    void Poner(List<Video> lista)
    {
        _esqueleto.Visibility = Visibility.Collapsed;
        var existentes = new HashSet<string>(_videos.Select(v => v.Clave));
        if (_videos.Count > 0 && lista.Take(_videos.Count).Select(v => v.Clave).SequenceEqual(_videos.Select(v => v.Clave)))
        {
            foreach (var v in lista.Skip(_videos.Count)) _videos.Add(v);
        }
        else
        {
            _videos.Clear();
            foreach (var v in lista) _videos.Add(v);
        }
        Almacen.MarcarProgreso(_videos);
        _masBtn.Visibility = _videos.Count >= 12 && _cantidad < 120 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"Para ti": mezcla videos de los canales que más has visto (o populares si aún no hay historial).</summary>
    static async Task<List<Video>> ParaTi(int cantidad, CancellationToken ct)
    {
        var canales = Almacen.Historial.Where(h => !h.Video.EsLocal && !string.IsNullOrEmpty(h.Video.Canal))
            .Take(60).GroupBy(h => h.Video.Canal).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(4).ToList();
        var consultas = canales.Count > 0 ? canales : new List<string> { "videos populares", "música en tendencia", "lo más visto hoy" };
        var porConsulta = Math.Max(8, cantidad / consultas.Count + 2);
        var tareas = consultas.Select(q => YtDlp.Buscar(q, porConsulta, YtDlp.Orden.Relevancia, ct)).ToList();
        var resultados = new List<List<Video>>();
        foreach (var t in tareas)
        {
            try { resultados.Add(await t); } catch (OperationCanceledException) { throw; } catch (Exception) { }
        }
        if (resultados.Count == 0) await tareas[0]; // propaga el error para mostrarlo
        var vistos = new HashSet<string>(Almacen.Historial.Select(h => h.Video.Clave));
        var mezcla = new List<Video>();
        for (int i = 0; mezcla.Count < cantidad && resultados.Any(r => r.Count > i); i++)
            foreach (var r in resultados)
                if (r.Count > i && r[i].Tipo == TipoItem.Video && !vistos.Contains(r[i].Clave)) mezcla.Add(r[i]);
        return mezcla;
    }
}
