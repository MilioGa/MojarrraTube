using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MojarraTube;

/// <summary>Piezas de interfaz reutilizables para armar las vistas desde código.</summary>
public static class UI
{
    static object R(string k) => Application.Current.FindResource(k);
    public static Style Estilo(string k) => (Style)R(k);
    public static Brush Pincel(string k) => (Brush)R(k);

    public static ScrollViewer Desplazable(UIElement contenido)
    {
        var s = new ScrollViewer
        {
            Content = contenido,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            PanningMode = PanningMode.VerticalOnly,
        };
        return s;
    }

    public static TextBlock Titulo(string t, double tam = 24) => new() { Text = t, Style = Estilo("Titulo"), FontSize = tam, TextWrapping = TextWrapping.Wrap };
    public static TextBlock Subtitulo(string t) => new() { Text = t, Style = Estilo("Subtitulo"), TextTrimming = TextTrimming.CharacterEllipsis };
    public static TextBlock Tenue(string t, double tam = 13) => new() { Text = t, Style = Estilo("TextoTenue"), FontSize = tam };
    public static TextBlock Icono(string g, double tam = 16, string color = "Texto") => new() { Text = g, Style = Estilo("Icono"), FontSize = tam, Foreground = Pincel(color) };

    public static Button Boton(string texto, string? icono, Action accion, string estilo = "Btn", string? tip = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (icono != null) sp.Children.Add(new TextBlock { Text = icono, FontFamily = (FontFamily)R("Iconos"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, texto.Length > 0 ? 8 : 0, 0) });
        if (texto.Length > 0) sp.Children.Add(new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = sp, Style = Estilo(estilo) };
        if (tip != null) b.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(b, texto.Length > 0 ? texto : tip ?? "");
        b.Click += (_, _) => accion();
        return b;
    }

    public static Button BotonIcono(string icono, string tip, Action accion)
    {
        var b = new Button { Content = icono, Style = Estilo("BtnIcono"), ToolTip = tip };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => accion();
        return b;
    }

    public static ItemsControl Rejilla(IEnumerable? items = null, double anchoMin = 260, int maxFilas = 0)
    {
        var ic = new ItemsControl { Style = Estilo("ListaRejilla"), ItemsSource = items };
        var panel = new FrameworkElementFactory(typeof(Rejilla));
        panel.SetValue(MojarraTube.Rejilla.AnchoMinimoProperty, anchoMin);
        panel.SetValue(MojarraTube.Rejilla.MaxFilasProperty, maxFilas);
        ic.ItemsPanel = new ItemsPanelTemplate(panel);
        return ic;
    }

    public static ItemsControl Filas(IEnumerable? items = null) => new()
    {
        ItemsSource = items,
        ItemTemplate = (DataTemplate)R("FilaVideo"),
        Focusable = false,
    };

    public static ItemsControl Esqueletos(int n = 12, double anchoMin = 260, int maxFilas = 0)
    {
        var ic = new ItemsControl { Style = Estilo("ListaEsqueleto"), ItemsSource = Enumerable.Range(0, n).ToList() };
        var panel = new FrameworkElementFactory(typeof(Rejilla));
        panel.SetValue(MojarraTube.Rejilla.AnchoMinimoProperty, anchoMin);
        panel.SetValue(MojarraTube.Rejilla.MaxFilasProperty, maxFilas);
        ic.ItemsPanel = new ItemsPanelTemplate(panel);
        return ic;
    }

    /// <summary>Fila de chips (opciones excluyentes) con desplazamiento horizontal si no caben.</summary>
    public static FrameworkElement Chips(IList<string> opciones, int elegido, Action<int> alElegir, out List<RadioButton> botones, bool envolver = false)
    {
        var grupo = "g" + Guid.NewGuid().ToString("N")[..6];
        Panel sp = envolver ? new WrapPanel() : new StackPanel { Orientation = Orientation.Horizontal };
        botones = new List<RadioButton>();
        for (int i = 0; i < opciones.Count; i++)
        {
            var n = i;
            var rb = new RadioButton { Content = opciones[i], Style = Estilo("Chip"), GroupName = grupo, IsChecked = i == elegido };
            rb.Checked += (_, _) => alElegir(n);
            sp.Children.Add(rb);
            botones.Add(rb);
        }
        if (envolver) return sp;
        var sv = new ScrollViewer { Content = sp, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        sv.PreviewMouseWheel += (_, e) =>
        {
            if (sv.ScrollableWidth <= 0) return;
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        return sv;
    }

    public static Grid Encabezado(UIElement izquierda, UIElement? derecha = null, double margenAbajo = 14)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, margenAbajo) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(izquierda);
        if (derecha != null) { Grid.SetColumn(derecha, 1); g.Children.Add(derecha); ((FrameworkElement)derecha).VerticalAlignment = VerticalAlignment.Center; }
        return g;
    }

    public static Border Separador(double arriba = 24, double abajo = 24) =>
        new() { Height = 1, Background = Pincel("Linea"), Margin = new Thickness(0, arriba, 0, abajo) };

    public static StackPanel Horizontal(params UIElement[] hijos)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var h in hijos) sp.Children.Add(h);
        return sp;
    }

    /// <summary>Al acercarse al final del desplazamiento, pide más contenido.</summary>
    public static void AlLlegarAlFinal(ScrollViewer s, Action cargarMas)
    {
        s.ScrollChanged += (_, e) =>
        {
            if (s.ScrollableHeight > 0 && s.VerticalOffset >= s.ScrollableHeight - 700 && e.VerticalChange > 0) cargarMas();
        };
    }

    /// <summary>Margen lateral según el ancho (más aire en pantallas grandes).</summary>
    public static Thickness Margen(double ancho) => ancho < 700 ? new Thickness(14, 8, 14, 40) : ancho < 1100 ? new Thickness(24, 8, 24, 48) : new Thickness(32, 10, 32, 56);
}

/// <summary>Estado vacío o de error: icono, título, explicación y una acción para salir del paso.</summary>
public class PanelEstado : Border
{
    readonly TextBlock _icono = new() { FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _titulo = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 6) };
    readonly TextBlock _texto = new() { FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
    readonly StackPanel _botones = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };

    public PanelEstado()
    {
        Padding = new Thickness(20, 56, 20, 56);
        Visibility = Visibility.Collapsed;
        _icono.FontFamily = (FontFamily)Application.Current.FindResource("Iconos");
        _icono.SetResourceReference(TextBlock.ForegroundProperty, "MuyTenue");
        _titulo.SetResourceReference(TextBlock.ForegroundProperty, "Texto");
        _texto.SetResourceReference(TextBlock.ForegroundProperty, "Tenue");
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(_icono); sp.Children.Add(_titulo); sp.Children.Add(_texto); sp.Children.Add(_botones);
        Child = sp;
    }

    public void Mostrar(string icono, string titulo, string texto, params (string texto, Action accion)[] acciones)
    {
        _icono.Text = icono; _titulo.Text = titulo; _texto.Text = texto;
        _texto.Visibility = texto.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _botones.Children.Clear();
        var primero = true;
        foreach (var (t, a) in acciones)
        {
            var b = UI.Boton(t, null, a, primero ? "BtnPrimario" : "BtnFantasma");
            b.Margin = new Thickness(5, 0, 5, 0);
            _botones.Children.Add(b);
            primero = false;
        }
        _botones.Visibility = acciones.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Visibility = Visibility.Visible;
    }

    public void Error(Exception e, Action reintentar)
    {
        var msg = e is ErrorYtDlp ey ? ey.Message : e is System.Net.Http.HttpRequestException ? "No hay conexión a internet." : "Algo salió mal: " + e.Message;
        var acciones = new List<(string, Action)> { ("Reintentar", reintentar) };
        if (msg.Contains("Actualiza los componentes") || msg.Contains("componentes"))
            acciones.Add(("Actualizar componentes", () => _ = Herramientas.Instancia.ActualizarYtDlp().ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(reintentar))));
        Mostrar("", "No se pudo cargar", msg, acciones.ToArray());
    }

    public void Ocultar() => Visibility = Visibility.Collapsed;
}
