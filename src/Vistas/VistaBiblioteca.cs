using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MojarraTube;

/// <summary>Tus archivos (descargados y carpetas que agregues), historial y Ver más tarde.</summary>
public class VistaBiblioteca : UserControl, IVista
{
    public string Seccion => "biblioteca";
    static int _pestana;
    readonly StackPanel _raiz = new();
    readonly StackPanel _barra = new() { Orientation = Orientation.Horizontal };
    readonly TextBox _filtro;
    readonly TextBlock _resumen;
    readonly ObservableCollection<Video> _videos = new();
    readonly PanelEstado _estado = new();
    readonly ProgressBar _cargando;
    List<Video> _todos = new();
    CancellationTokenSource? _cts;

    public VistaBiblioteca(int pestana = -1)
    {
        if (pestana >= 0) _pestana = pestana;
        Content = UI.Desplazable(_raiz);
        SizeChanged += (_, _) => _raiz.Margin = UI.Margen(ActualWidth);
        _raiz.Children.Add(UI.Encabezado(UI.Titulo("Biblioteca"), _barra, 14));
        var chips = UI.Chips(new[] { "Mis archivos", "Historial", "Ver más tarde" }, _pestana, i => { _pestana = i; Cargar(); }, out _);
        var filtroCaja = new TextBox { Style = UI.Estilo("Caja"), Tag = "Filtrar por nombre o canal", Width = 260, Height = 38, Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(filtroCaja, "Filtrar");
        _filtro = filtroCaja;
        _filtro.TextChanged += (_, _) => Filtrar();
        var fila = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        chips.Margin = new Thickness(0, 0, 16, 0);
        fila.Children.Add(chips);
        fila.Children.Add(_filtro);
        _raiz.Children.Add(fila);
        _resumen = UI.Tenue("", 13);
        _resumen.Margin = new Thickness(2, 0, 0, 16);
        _raiz.Children.Add(_resumen);
        _cargando = new ProgressBar { Style = UI.Estilo("BarraFina"), IsIndeterminate = true, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16), Visibility = Visibility.Collapsed };
        _raiz.Children.Add(_cargando);
        _raiz.Children.Add(_estado);
        _raiz.Children.Add(UI.Rejilla(_videos, 240));
        Cargar();
    }

    public void AlMostrar()
    {
        if (App.Ventana != null) App.Ventana.MenuExtra = MenuExtra;
        Almacen.Cambio += AlCambiarDatos;
        if (_pestana != 0) Cargar();
        Almacen.MarcarProgreso(_videos);
    }
    public void AlOcultar() => Almacen.Cambio -= AlCambiarDatos;

    bool _recargaPendiente;
    void AlCambiarDatos()
    {
        if (_pestana == 0 || _recargaPendiente) return;
        _recargaPendiente = true;
        Dispatcher.BeginInvoke(() => { _recargaPendiente = false; Cargar(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    List<(string, string, Action)> MenuExtra(Video v) => _pestana switch
    {
        1 => new() { ("Quitar del historial", "", () => QuitarHistorial(v)) },
        2 => new() { ("Quitar de Ver más tarde", "", () => App.Ventana?.AlternarVerDespues(v)) },
        _ => v.EsLocal ? new() { ("Borrar archivo…", "", () => BorrarArchivo(v)) } : new(),
    };

    void Barra()
    {
        _barra.Children.Clear();
        var w = App.Ventana;
        void B(Button b) { b.Margin = new Thickness(8, 0, 0, 0); _barra.Children.Add(b); }
        switch (_pestana)
        {
            case 0:
                B(UI.Boton("Abrir archivo", "", () => w?.AbrirArchivoDialogo(), "Btn", "Ctrl+O"));
                B(UI.Boton("Agregar carpeta", "", AgregarCarpeta, "BtnFantasma"));
                break;
            case 1:
                if (Almacen.Historial.Count > 0) B(UI.Boton("Borrar historial", "", BorrarHistorial, "BtnFantasma"));
                break;
            case 2:
                if (Almacen.VerDespues.Count > 0) B(UI.Boton("Reproducir todo", "", () => w?.PonerCola(Almacen.VerDespues.ToList()), "BtnPrimario"));
                break;
        }
    }

    async void Cargar()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Barra();
        _estado.Ocultar();
        switch (_pestana)
        {
            case 1:
                _todos = Almacen.Historial.Select(h => { var v = h.Video.Copia(); return v; }).ToList();
                _resumen.Text = _todos.Count == 0 ? "" : $"{_todos.Count} videos vistos · lo más reciente primero";
                break;
            case 2:
                _todos = Almacen.VerDespues.Select(v => v.Copia()).ToList();
                _resumen.Text = _todos.Count == 0 ? "" : $"{_todos.Count} videos guardados";
                break;
            default:
                _cargando.Visibility = Visibility.Visible;
                var carpetas = Carpetas();
                _todos = await Task.Run(() => Escanear(carpetas, cts.Token));
                if (cts.IsCancellationRequested) return;
                _cargando.Visibility = Visibility.Collapsed;
                _resumen.Text = $"{_todos.Count} archivos en: " + string.Join(", ", carpetas.Select(c => Path.GetFileName(c.TrimEnd('\\')) is { Length: > 0 } n ? n : c));
                break;
        }
        Filtrar();
    }

    static List<string> Carpetas()
    {
        var a = Almacen.Ajustes;
        return new[] { a.CarpetaVideos, a.CarpetaAudio }.Concat(a.CarpetasBiblioteca).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static List<Video> Escanear(List<string> carpetas, CancellationToken ct)
    {
        var lista = new List<Video>();
        var opciones = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true };
        foreach (var c in carpetas)
        {
            if (!Directory.Exists(c)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(c, "*", opciones))
                {
                    if (ct.IsCancellationRequested) return lista;
                    if (!MainWindow.EsMedio(f) || f.EndsWith(".part")) continue;
                    lista.Add(MainWindow.VideoLocal(f));
                }
            }
            catch { }
        }
        // Títulos bonitos para lo descargado: "Título [id].mp4" → "Título"
        foreach (var v in lista)
        {
            var i = v.Titulo.LastIndexOf(" [", StringComparison.Ordinal);
            if (i > 0 && v.Titulo.EndsWith(']')) v.Titulo = v.Titulo[..i];
        }
        return lista.GroupBy(v => v.Ruta, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderByDescending(v => v.Fecha).ToList();
    }

    void Filtrar()
    {
        var q = _filtro.Text.Trim();
        var lista = q.Length == 0 ? _todos : _todos.Where(v => v.Titulo.Contains(q, StringComparison.CurrentCultureIgnoreCase) || v.Canal.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        _videos.Clear();
        foreach (var v in lista.Take(600)) _videos.Add(v);
        Almacen.MarcarProgreso(_videos);
        if (_videos.Count > 0) { _estado.Ocultar(); return; }
        if (q.Length > 0) { _estado.Mostrar("", "Nada coincide con el filtro", $"No hay nada con «{q}».", ("Quitar filtro", () => _filtro.Clear())); return; }
        switch (_pestana)
        {
            case 1: _estado.Mostrar("", "Tu historial está vacío", Almacen.Ajustes.GuardarHistorial ? "Los videos que veas aparecerán aquí para seguir donde te quedaste." : "El historial está desactivado en Ajustes."); break;
            case 2: _estado.Mostrar("", "No hay nada en Ver más tarde", "En cualquier video usa «Ver más tarde» (o clic derecho en una tarjeta) para guardarlo aquí."); break;
            default:
                _estado.Mostrar("", "Aún no hay archivos", "Lo que descargues aparecerá aquí. También puedes agregar una carpeta con tus videos o música, o arrastrar archivos a la ventana.",
                    ("Agregar carpeta", AgregarCarpeta), ("Ir a Descargas", () => App.Ventana?.Navegar(new VistaDescargas())));
                break;
        }
    }

    void AgregarCarpeta()
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Elegir una carpeta con videos o música" };
        if (d.ShowDialog() != true) return;
        if (!Almacen.Ajustes.CarpetasBiblioteca.Contains(d.FolderName, StringComparer.OrdinalIgnoreCase))
        {
            Almacen.Ajustes.CarpetasBiblioteca.Add(d.FolderName);
            Almacen.Guardar("ajustes");
        }
        _pestana = 0;
        Cargar();
        App.Ventana?.Aviso("Carpeta agregada a tu biblioteca", "", ("Quitar", () => { Almacen.Ajustes.CarpetasBiblioteca.Remove(d.FolderName); Almacen.Guardar("ajustes"); Cargar(); }));
    }

    void QuitarHistorial(Video v)
    {
        var e = Almacen.Historial.FirstOrDefault(h => h.Video.Clave == v.Clave);
        if (e == null) return;
        var i = Almacen.Historial.IndexOf(e);
        Almacen.Historial.Remove(e);
        Almacen.Guardar("historial");
        App.Ventana?.Aviso("Se quitó del historial", "", ("Deshacer", () => { Almacen.Historial.Insert(Math.Min(i, Almacen.Historial.Count), e); Almacen.Guardar("historial"); }));
    }

    async void BorrarHistorial()
    {
        if (App.Ventana == null) return;
        if (!await App.Ventana.Confirmar("¿Borrar todo el historial?", "Se olvidarán los videos que viste y dónde te quedaste en cada uno. Tus descargas no se tocan.", "Borrar historial")) return;
        Almacen.Historial.Clear();
        Almacen.Guardar("historial");
        App.Ventana.Aviso("Historial borrado", "");
    }

    async void BorrarArchivo(Video v)
    {
        if (App.Ventana == null || v.Ruta == null) return;
        if (!await App.Ventana.Confirmar("¿Mover a la papelera?", $"«{v.Titulo}» se enviará a la papelera de reciclaje de Windows. Podrás recuperarlo desde ahí.", "Mover a la papelera")) return;
        if (App.Ventana.Rep.Video?.Ruta == v.Ruta) App.Ventana.CerrarReproductor();
        try
        {
            Papelera.Enviar(v.Ruta);
            App.Ventana.Aviso("Archivo enviado a la papelera", "");
            Cargar();
        }
        catch (Exception e) { App.Ventana.Aviso("No se pudo borrar: " + e.Message, ""); }
    }
}
