using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace MojarraTube;

/// <summary>Rejilla adaptable: tantas columnas como quepan (ancho mínimo por tarjeta), todas del mismo ancho.</summary>
public class Rejilla : Panel
{
    public static readonly DependencyProperty AnchoMinimoProperty = DependencyProperty.Register(nameof(AnchoMinimo), typeof(double), typeof(Rejilla),
        new FrameworkPropertyMetadata(260.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double AnchoMinimo { get => (double)GetValue(AnchoMinimoProperty); set => SetValue(AnchoMinimoProperty, value); }
    public static readonly DependencyProperty EspacioProperty = DependencyProperty.Register(nameof(Espacio), typeof(double), typeof(Rejilla),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double Espacio { get => (double)GetValue(EspacioProperty); set => SetValue(EspacioProperty, value); }
    public static readonly DependencyProperty EspacioVerticalProperty = DependencyProperty.Register(nameof(EspacioVertical), typeof(double), typeof(Rejilla),
        new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double EspacioVertical { get => (double)GetValue(EspacioVerticalProperty); set => SetValue(EspacioVerticalProperty, value); }
    /// <summary>Máximo de filas (0 = sin límite); las tarjetas que no caben se ocultan.</summary>
    public static readonly DependencyProperty MaxFilasProperty = DependencyProperty.Register(nameof(MaxFilas), typeof(int), typeof(Rejilla),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public int MaxFilas { get => (int)GetValue(MaxFilasProperty); set => SetValue(MaxFilasProperty, value); }

    int _cols = 1; double _anchoItem; readonly List<double> _altos = new();

    protected override Size MeasureOverride(Size disponible)
    {
        var w = double.IsInfinity(disponible.Width) ? 1200 : disponible.Width;
        _cols = Math.Max(1, (int)((w + Espacio) / (AnchoMinimo + Espacio)));
        _anchoItem = Math.Max(0, (w - Espacio * (_cols - 1)) / _cols);
        _altos.Clear();
        double alto = 0, altoFila = 0;
        var visibles = MaxFilas > 0 ? MaxFilas * _cols : int.MaxValue;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            if (i >= visibles) { c.Measure(new Size(0, 0)); continue; }
            c.Measure(new Size(_anchoItem, double.PositiveInfinity));
            altoFila = Math.Max(altoFila, c.DesiredSize.Height);
            if (i % _cols == _cols - 1 || i == InternalChildren.Count - 1 || i == visibles - 1)
            {
                _altos.Add(altoFila);
                alto += altoFila + (_altos.Count > 1 ? EspacioVertical : 0);
                altoFila = 0;
            }
        }
        return new Size(w, alto);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        var visibles = MaxFilas > 0 ? MaxFilas * _cols : int.MaxValue;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            if (i >= visibles) { c.Arrange(new Rect(0, 0, 0, 0)); continue; }
            var fila = i / _cols; var col = i % _cols;
            if (col == 0 && fila > 0) y += _altos[fila - 1] + EspacioVertical;
            var h = fila < _altos.Count ? _altos[fila] : c.DesiredSize.Height;
            c.Arrange(new Rect(col * (_anchoItem + Espacio), y, _anchoItem, h));
        }
        return final;
    }
}

/// <summary>Comportamientos pequeños para usar desde XAML.</summary>
public static class Forma
{
    /// <summary>Esquinas redondeadas que sí recortan el contenido (imágenes).</summary>
    public static readonly DependencyProperty RadioProperty = DependencyProperty.RegisterAttached("Radio", typeof(double), typeof(Forma),
        new PropertyMetadata(0.0, (d, e) =>
        {
            if (d is not FrameworkElement f) return;
            f.SizeChanged -= Recortar; f.SizeChanged += Recortar;
            Recortar(f, null);
        }));
    public static double GetRadio(DependencyObject d) => (double)d.GetValue(RadioProperty);
    public static void SetRadio(DependencyObject d, double v) => d.SetValue(RadioProperty, v);
    static void Recortar(object s, SizeChangedEventArgs? e)
    {
        var f = (FrameworkElement)s; var r = GetRadio(f);
        f.Clip = r > 0 && f.ActualWidth > 0 ? new RectangleGeometry(new Rect(0, 0, f.ActualWidth, f.ActualHeight), r, r) : null;
    }

    /// <summary>Alto = ancho / proporción (16:9 = 1.7778).</summary>
    public static readonly DependencyProperty ProporcionProperty = DependencyProperty.RegisterAttached("Proporcion", typeof(double), typeof(Forma),
        new PropertyMetadata(0.0, (d, e) =>
        {
            if (d is not FrameworkElement f) return;
            f.SizeChanged -= Ajustar; f.SizeChanged += Ajustar;
            Ajustar(f, null);
        }));
    public static double GetProporcion(DependencyObject d) => (double)d.GetValue(ProporcionProperty);
    public static void SetProporcion(DependencyObject d, double v) => d.SetValue(ProporcionProperty, v);
    static void Ajustar(object s, SizeChangedEventArgs? e)
    {
        var f = (FrameworkElement)s; var p = GetProporcion(f);
        if (p > 0 && f.ActualWidth > 0 && (e == null || e.WidthChanged))
        {
            var h = Math.Round(f.ActualWidth / p);
            if (Math.Abs(f.Height - h) > 0.5 || double.IsNaN(f.Height)) f.Height = h;
        }
    }
}

/// <summary>Clic en una tarjeta abre el video; clic derecho o tecla de menú muestra sus acciones.</summary>
public static class Acciones
{
    public static readonly DependencyProperty TarjetaProperty = DependencyProperty.RegisterAttached("Tarjeta", typeof(bool), typeof(Acciones),
        new PropertyMetadata(false, (d, e) =>
        {
            if (d is not ButtonBase b || !(bool)e.NewValue) return;
            b.Click += (_, _) => { if (b.DataContext is Video v) App.Ventana?.Abrir(v); };
            b.MouseRightButtonUp += (_, ev) => { if (b.DataContext is Video v) { Menu(v, b); ev.Handled = true; } };
            b.PreviewMouseDown += (_, ev) =>
            {
                if (ev.ChangedButton == MouseButton.Middle && b.DataContext is Video v) { App.Ventana?.AgregarACola(v); ev.Handled = true; }
            };
            b.KeyDown += (_, ev) =>
            {
                if ((ev.Key == Key.Apps || (ev.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)) && b.DataContext is Video v) { Menu(v, b); ev.Handled = true; }
            };
        }));
    public static bool GetTarjeta(DependencyObject d) => (bool)d.GetValue(TarjetaProperty);
    public static void SetTarjeta(DependencyObject d, bool v) => d.SetValue(TarjetaProperty, v);

    static MenuItem I(string texto, string icono, Action a, string atajo = "")
    {
        var m = new MenuItem { Header = texto, Tag = icono, InputGestureText = atajo };
        m.Click += (_, _) => a();
        return m;
    }

    public static void Menu(Video v, FrameworkElement junto)
    {
        var w = App.Ventana;
        if (w == null) return;
        var m = new ContextMenu { PlacementTarget = junto, Placement = Keyboard.FocusedElement == junto && !junto.IsMouseOver ? PlacementMode.Bottom : PlacementMode.MousePoint };
        if (v.EsCanal)
        {
            m.Items.Add(I("Abrir canal", "", () => w.Abrir(v)));
            var c = new Canal { Id = v.CanalId, Nombre = v.Canal, Avatar = v.Miniatura, Url = v.Url };
            if (!string.IsNullOrEmpty(v.CanalId))
                m.Items.Add(I(Almacen.Suscrito(v.CanalId) ? "Cancelar suscripción" : "Suscribirme", "", () => w.AlternarSuscripcion(c)));
        }
        else
        {
            m.Items.Add(I(v.EsLista ? "Abrir lista" : "Reproducir", "", () => w.Abrir(v)));
            if (!v.EsLista)
            {
                m.Items.Add(I("Agregar a la cola", "", () => w.AgregarACola(v), "Clic central"));
                m.Items.Add(I("Solo escuchar (audio)", "", () => { var c = v.Copia(); c.SoloAudio = true; w.Abrir(c); }));
            }
            if (!v.EsLocal)
            {
                m.Items.Add(new Separator());
                m.Items.Add(I("Descargar…", "", () => w.Descargar(v)));
                if (!v.EsLista)
                    m.Items.Add(I(Almacen.EnVerDespues(v) ? "Quitar de Ver más tarde" : "Ver más tarde", "", () => w.AlternarVerDespues(v)));
                if (!string.IsNullOrEmpty(v.CanalId) || !string.IsNullOrEmpty(v.Canal))
                    m.Items.Add(I("Ir al canal", "", () => w.AbrirCanal(v.CanalId, v.Canal)));
                m.Items.Add(new Separator());
                m.Items.Add(I("Copiar enlace", "", () => w.CopiarEnlace(v.UrlCompleta)));
                m.Items.Add(I("Abrir en el navegador", "", () => w.AbrirEnNavegador(v.UrlCompleta)));
            }
            else
            {
                m.Items.Add(new Separator());
                m.Items.Add(I("Mostrar en la carpeta", "", () => GestorDescargas.MostrarEnCarpeta(v.Ruta)));
                m.Items.Add(I("Abrir con otra app", "", () => GestorDescargas.AbrirArchivo(v.Ruta)));
            }
        }
        if (w.MenuExtra?.Invoke(v) is { Count: > 0 } extra)
        {
            m.Items.Add(new Separator());
            foreach (var (t, ico, a) in extra) m.Items.Add(I(t, ico, a));
        }
        m.IsOpen = true;
    }
}

/// <summary>Elige la plantilla de tarjeta: video, lista o canal.</summary>
public class SelectorTarjeta : DataTemplateSelector
{
    public DataTemplate? Video { get; set; }
    public DataTemplate? Canal { get; set; }
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is Video { EsCanal: true } ? Canal : Video;
}

public class BoolAVisible : IValueConverter
{
    public bool Invertir { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var b = value switch { bool x => x, string s => !string.IsNullOrEmpty(s), int i => i != 0, null => false, _ => true };
        return b ^ Invertir ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>0–1 → ancho proporcional de una barrita (para el progreso visto en las miniaturas).</summary>
public class FraccionAEscala : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is double d ? Math.Clamp(d, 0, 1) : 0.0;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
