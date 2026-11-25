using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MojarraTube;

/// <summary>Botones de cada descarga (en la plantilla FilaDescarga).</summary>
public static class AccionDescarga
{
    public static readonly DependencyProperty AccionProperty = DependencyProperty.RegisterAttached("Accion", typeof(string), typeof(AccionDescarga),
        new PropertyMetadata(null, (d, e) =>
        {
            if (d is ButtonBase b) b.Click += (_, _) => { if (b.DataContext is Descarga x) Ejecutar(GetAccion(b), x, b); };
        }));
    public static string GetAccion(DependencyObject d) => (string)d.GetValue(AccionProperty);
    public static void SetAccion(DependencyObject d, string v) => d.SetValue(AccionProperty, v);

    static void Ejecutar(string accion, Descarga d, FrameworkElement junto)
    {
        var g = GestorDescargas.Instancia;
        var w = App.Ventana;
        switch (accion)
        {
            case "abrir": w?.AbrirDescarga(d); break;
            case "carpeta": GestorDescargas.MostrarEnCarpeta(d.R.Archivo ?? d.R.Carpeta); break;
            case "pausar": g.Pausar(d); break;
            case "reanudar": g.Reanudar(d); break;
            case "cancelar": g.Cancelar(d); break;
            case "menu": Menu(d, junto); break;
        }
    }

    static void Menu(Descarga d, FrameworkElement junto)
    {
        var w = App.Ventana;
        var g = GestorDescargas.Instancia;
        var m = new ContextMenu { PlacementTarget = junto, Placement = PlacementMode.Bottom };
        MenuItem I(string t, string ico, Action a) { var mi = new MenuItem { Header = t, Tag = ico }; mi.Click += (_, _) => a(); m.Items.Add(mi); return mi; }
        if (d.Terminada) { I("Reproducir aquí", "", () => w?.AbrirDescarga(d)); I("Abrir con otra app", "", () => GestorDescargas.AbrirArchivo(d.R.Archivo)); }
        I("Mostrar en la carpeta", "", () => GestorDescargas.MostrarEnCarpeta(d.R.Archivo ?? d.R.Carpeta));
        I("Ver el video original", "", () => w?.AbrirTexto(d.R.Url));
        I("Copiar enlace", "", () => w?.CopiarEnlace(d.R.Url));
        if (d.Terminada || d.ConError) I("Descargar de nuevo…", "", () => w?.Descargar(new Video { Url = d.R.Url, Titulo = d.Titulo, Miniatura = d.Miniatura, Canal = d.Canal, Tipo = d.R.EsLista ? TipoItem.Lista : TipoItem.Video }));
        m.Items.Add(new Separator());
        I("Quitar de la lista", "", () =>
        {
            var i = g.Lista.IndexOf(d);
            g.Quitar(d);
            w?.Aviso("Se quitó de la lista (el archivo no se borró)", "", ("Deshacer", () => g.Restaurar(d, i)));
        });
        m.IsOpen = true;
    }
}

/// <summary>Descargas: pegar un enlace, ver el progreso, pausar, reanudar y abrir lo descargado.</summary>
public class VistaDescargas : UserControl, IVista
{
    public string Seccion => "descargas";
    readonly StackPanel _raiz = new();
    readonly TextBox _enlace;
    readonly PanelEstado _estado = new();
    readonly StackPanel _barra = new() { Orientation = Orientation.Horizontal };

    public VistaDescargas()
    {
        Content = UI.Desplazable(_raiz);
        SizeChanged += (_, _) => _raiz.Margin = UI.Margen(ActualWidth);
        _raiz.Children.Add(UI.Encabezado(UI.Titulo("Descargas"), _barra, 16));

        // Descargar desde cualquier enlace
        var caja = new Border { Style = UI.Estilo("Tarjeta"), Margin = new Thickness(0, 0, 0, 22), Padding = new Thickness(16) };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Descargar desde un enlace", FontWeight = FontWeights.SemiBold, FontSize = 15 });
        sp.Children.Add(UI.Tenue("Pega el enlace de un video de YouTube o de otra página (Facebook, Instagram, TikTok, X, Vimeo, Twitch y más de mil sitios).", 13));
        var fila = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        fila.ColumnDefinitions.Add(new ColumnDefinition());
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _enlace = new TextBox { Style = UI.Estilo("Caja"), Tag = "https://…" };
        System.Windows.Automation.AutomationProperties.SetName(_enlace, "Enlace para descargar");
        _enlace.KeyDown += (_, e) => { if (e.Key == Key.Enter) Descargar(); };
        fila.Children.Add(_enlace);
        var pegar = UI.Boton("Pegar", "", () => { try { if (Clipboard.ContainsText()) { _enlace.Text = Clipboard.GetText().Trim(); _enlace.Focus(); _enlace.CaretIndex = _enlace.Text.Length; } } catch { } }, "BtnFantasma");
        pegar.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(pegar, 1);
        fila.Children.Add(pegar);
        var bajar = UI.Boton("Descargar", "", Descargar, "BtnPrimario");
        bajar.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(bajar, 2);
        fila.Children.Add(bajar);
        sp.Children.Add(fila);
        caja.Child = sp;
        _raiz.Children.Add(caja);

        _raiz.Children.Add(_estado);
        _raiz.Children.Add(new ItemsControl { ItemsSource = GestorDescargas.Instancia.Lista, ItemTemplate = (DataTemplate)FindResource("FilaDescarga"), Focusable = false });
        Pintar();
    }

    public void AlMostrar()
    {
        Pintar();
        GestorDescargas.Instancia.PropertyChanged += AlCambiarGestor;
        GestorDescargas.Instancia.Lista.CollectionChanged += AlCambiarLista;
    }
    public void AlOcultar()
    {
        GestorDescargas.Instancia.PropertyChanged -= AlCambiarGestor;
        GestorDescargas.Instancia.Lista.CollectionChanged -= AlCambiarLista;
    }
    void AlCambiarGestor(object? s, System.ComponentModel.PropertyChangedEventArgs e) => Pintar();
    void AlCambiarLista(object? s, NotifyCollectionChangedEventArgs e) => Pintar();

    void Pintar()
    {
        var g = GestorDescargas.Instancia;
        _barra.Children.Clear();
        void B(Button b) { b.Margin = new Thickness(8, 0, 0, 0); _barra.Children.Add(b); }
        if (g.Lista.Any(x => x.PuedePausar)) B(UI.Boton("Pausar todo", "", g.PausarTodas, "BtnFantasma"));
        if (g.Lista.Any(x => x.Estado == EstadoDescarga.Pausada)) B(UI.Boton("Reanudar todo", "", g.ReanudarTodas, "BtnFantasma"));
        if (g.Lista.Any(x => x.Estado is EstadoDescarga.Terminada or EstadoDescarga.Cancelada)) B(UI.Boton("Limpiar terminadas", "", g.LimpiarTerminadas, "BtnFantasma", "Quita de la lista lo terminado (los archivos se quedan)"));
        B(UI.Boton("Abrir carpeta", "", () => GestorDescargas.MostrarEnCarpeta(Almacen.Ajustes.CarpetaVideos), "Btn"));
        if (g.Lista.Count == 0)
            _estado.Mostrar("", "No hay descargas", "Usa el botón «Descargar» en cualquier video, o pega un enlace arriba. Elige la calidad o descarga solo el audio en MP3.");
        else _estado.Ocultar();
    }

    void Descargar()
    {
        var t = _enlace.Text.Trim();
        if (!Texto.EsUrl(t))
        {
            App.Ventana?.Aviso(t.Length == 0 ? "Primero pega un enlace." : "Eso no parece un enlace. Debe empezar con https://", "");
            _enlace.Focus();
            return;
        }
        App.Ventana?.Descargar(new Video { Url = Texto.NormalizarUrl(t), Titulo = t });
        _enlace.Clear();
    }
}
