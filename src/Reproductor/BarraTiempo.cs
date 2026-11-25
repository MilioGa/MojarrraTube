using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MojarraTube;

/// <summary>Barra de tiempo del reproductor: lo visto, lo cargado, capítulos y vista previa del tiempo al pasar el ratón.</summary>
public class BarraTiempo : FrameworkElement
{
    double _pos, _dur, _cargado;
    List<Capitulo> _capitulos = new();
    double? _hoverX;
    bool _arrastrando;
    readonly Popup _globo;
    readonly TextBlock _globoTiempo = new() { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _globoCap = new() { Foreground = new SolidColorBrush(Color.FromRgb(220, 222, 230)), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis };

    static readonly Brush Fondo = Congelar(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)));
    static readonly Brush Cargado = Congelar(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)));
    static readonly Brush Hover = Congelar(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)));
    static Brush Congelar(Brush b) { b.Freeze(); return b; }

    /// <summary>El usuario pidió ir a este segundo.</summary>
    public event Action<double>? Buscar;
    /// <summary>Mientras arrastra (para mostrar el tiempo sin saltar todavía).</summary>
    public event Action<double>? Previsualizar;

    public BarraTiempo()
    {
        Height = 22;
        Cursor = Cursors.Hand;
        Focusable = true;
        FocusVisualStyle = null;
        var panel = new StackPanel();
        panel.Children.Add(_globoTiempo);
        panel.Children.Add(_globoCap);
        _globo = new Popup
        {
            Child = new Border { Background = new SolidColorBrush(Color.FromArgb(230, 20, 22, 28)), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 5, 9, 6), Child = panel },
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = this,
            IsHitTestVisible = false,
        };
        ToolTipService.SetIsEnabled(this, false);
        AutomationProperties_Nombre();
    }

    void AutomationProperties_Nombre() => System.Windows.Automation.AutomationProperties.SetName(this, "Barra de tiempo del video");

    public void Actualizar(double posicion, double duracion, double cargado)
    {
        if (_arrastrando) { _dur = duracion; return; }
        if (Math.Abs(posicion - _pos) < 0.05 && duracion == _dur && Math.Abs(cargado - _cargado) < 0.01) return;
        _pos = posicion; _dur = duracion; _cargado = cargado;
        InvalidateVisual();
    }

    public List<Capitulo> Capitulos { get => _capitulos; set { _capitulos = value ?? new(); InvalidateVisual(); } }

    double Segundo(double x) => _dur <= 0 ? 0 : Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1) * _dur;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var activo = _hoverX != null || _arrastrando;
        var alto = activo ? 6 : 4;
        var y = (h - alto) / 2;
        var acento = TryFindResource("Acento") as Brush ?? Brushes.Red;

        // Tramos (uno por capítulo, con una pequeña separación)
        var cortes = new List<double> { 0 };
        if (_dur > 0) cortes.AddRange(_capitulos.Where(c => c.Inicio > 0.5 && c.Inicio < _dur - 0.5).Select(c => c.Inicio / _dur * w));
        cortes.Add(w);
        var fraccion = _dur > 0 ? Math.Clamp(_pos / _dur, 0, 1) : 0;
        var xPos = fraccion * w;
        var xCarg = Math.Clamp(_cargado, 0, 1) * w;
        for (int i = 0; i < cortes.Count - 1; i++)
        {
            var a = cortes[i] + (i > 0 ? 1.5 : 0);
            var b = cortes[i + 1] - (i < cortes.Count - 2 ? 1.5 : 0);
            if (b <= a) continue;
            var radio = alto / 2.0;
            dc.DrawRoundedRectangle(Fondo, null, new Rect(a, y, b - a, alto), radio, radio);
            if (xCarg > a) dc.DrawRoundedRectangle(Cargado, null, new Rect(a, y, Math.Min(b, xCarg) - a, alto), radio, radio);
            if (_hoverX is { } hx && hx > a) dc.DrawRoundedRectangle(Hover, null, new Rect(a, y, Math.Min(b, hx) - a, alto), radio, radio);
            if (xPos > a) dc.DrawRoundedRectangle(acento, null, new Rect(a, y, Math.Min(b, xPos) - a, alto), radio, radio);
        }
        // Bolita
        var r = activo ? 7.0 : (IsKeyboardFocused ? 6.0 : 0);
        if (r > 0 && _dur > 0) dc.DrawEllipse(acento, null, new Point(xPos, h / 2), r, r);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var x = e.GetPosition(this).X;
        _hoverX = Math.Clamp(x, 0, ActualWidth);
        if (_arrastrando)
        {
            _pos = Segundo(x);
            Previsualizar?.Invoke(_pos);
        }
        MostrarGlobo(_hoverX.Value);
        InvalidateVisual();
    }

    void MostrarGlobo(double x)
    {
        if (_dur <= 0) { _globo.IsOpen = false; return; }
        var s = Segundo(x);
        _globoTiempo.Text = Texto.Tiempo(s);
        var cap = _capitulos.LastOrDefault(c => c.Inicio <= s);
        _globoCap.Text = cap?.Titulo ?? "";
        _globoCap.Visibility = cap == null ? Visibility.Collapsed : Visibility.Visible;
        _globo.Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var ancho = _globo.Child.DesiredSize.Width;
        _globo.HorizontalOffset = Math.Clamp(x - ancho / 2, 0, Math.Max(0, ActualWidth - ancho));
        _globo.VerticalOffset = -_globo.Child.DesiredSize.Height - 6;
        _globo.IsOpen = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (!_arrastrando) { _hoverX = null; _globo.IsOpen = false; InvalidateVisual(); }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        _arrastrando = true;
        CaptureMouse();
        _pos = Segundo(e.GetPosition(this).X);
        Previsualizar?.Invoke(_pos);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_arrastrando) return;
        _arrastrando = false;
        ReleaseMouseCapture();
        Buscar?.Invoke(Segundo(e.GetPosition(this).X));
        if (!IsMouseOver) { _hoverX = null; _globo.IsOpen = false; }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_arrastrando) { _arrastrando = false; Buscar?.Invoke(_pos); }
        base.OnLostMouseCapture(e);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { InvalidateVisual(); base.OnGotKeyboardFocus(e); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { InvalidateVisual(); base.OnLostKeyboardFocus(e); }

    public void Ocultar() { _globo.IsOpen = false; _hoverX = null; InvalidateVisual(); }
}
