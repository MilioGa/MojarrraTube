using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace MojarraTube;

/// <summary>Ajustes agrupados por tema, con efecto inmediato (no hay botón "Guardar").</summary>
public class VistaAjustes : UserControl, IVista
{
    public string Seccion => "ajustes";
    readonly StackPanel _raiz = new() { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
    TextBlock? _versiones, _cache, _estadoComp;
    Ajustes A => Almacen.Ajustes;

    public VistaAjustes()
    {
        var s = UI.Desplazable(_raiz);
        Content = s;
        SizeChanged += (_, _) => _raiz.Margin = UI.Margen(ActualWidth);
        Construir();
    }

    public void AlMostrar() { _ = ActualizarVersiones(); Herramientas.Instancia.PropertyChanged += Comp; }
    public void AlOcultar() => Herramientas.Instancia.PropertyChanged -= Comp;
    void Comp(object? s, System.ComponentModel.PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_estadoComp != null) _estadoComp.Text = Herramientas.Instancia.Ocupado ? Herramientas.Instancia.Mensaje : "";
        if (e.PropertyName == nameof(Herramientas.Version)) _ = ActualizarVersiones();
    });

    static void G() => Almacen.Guardar("ajustes");

    void Construir()
    {
        _raiz.Children.Add(UI.Titulo("Ajustes"));
        _raiz.Children.Add(UI.Tenue("Los cambios se guardan solos.", 13));

        // Apariencia
        var ap = Seccion_("Apariencia", "");
        var temas = new[] { "oscuro", "claro", "sistema" };
        ap.Children.Add(Opcion("Tema", "El tema «Sistema» sigue el modo claro u oscuro de Windows.",
            UI.Chips(new[] { "Oscuro", "Claro", "Sistema" }, Math.Max(0, Array.IndexOf(temas, A.Tema)), i => { A.Tema = temas[i]; G(); App.AplicarTema(A.Tema); }, out _, true)));
        ap.Children.Add(Interruptor("Menú lateral compacto", "Muestra solo iconos en el menú de la izquierda.", A.MenuCompacto, v => { A.MenuCompacto = v; G(); App.Ventana?.Responsivo(); }));

        // Reproducción
        var re = Seccion_("Reproducción", "");
        var calidades = new[] { 0, 2160, 1440, 1080, 720, 480, 360 };
        re.Children.Add(Opcion("Calidad preferida", "Si el video no la tiene, se usa la más cercana por debajo. «La mejor» usa más datos.",
            UI.Chips(new[] { "La mejor", "4K", "1440p", "1080p", "720p", "480p", "360p" }, Math.Max(0, Array.IndexOf(calidades, A.CalidadReproduccion)), i => { A.CalidadReproduccion = calidades[i]; G(); }, out _, true)));
        re.Children.Add(Interruptor("Seguir donde me quedé", "Al volver a abrir un video empieza donde lo dejaste.", A.RecordarPosicion, v => { A.RecordarPosicion = v; G(); }));
        re.Children.Add(Interruptor("Reproducir el siguiente de la cola", "Al terminar un video empieza el siguiente (con 3 segundos para cancelarlo).", A.Reproduccion_Auto, v => { A.Reproduccion_Auto = v; G(); }));

        // Descargas
        var de = Seccion_("Descargas", "");
        de.Children.Add(Carpeta("Carpeta de videos", () => A.CarpetaVideos, v => { A.CarpetaVideos = v; G(); }));
        de.Children.Add(Carpeta("Carpeta de audio (MP3)", () => A.CarpetaAudio, v => { A.CarpetaAudio = v; G(); }));
        var cd = new[] { 0, 2160, 1440, 1080, 720, 480, 360 };
        de.Children.Add(Opcion("Calidad de video predeterminada", "Puedes cambiarla en cada descarga.",
            UI.Chips(new[] { "La mejor", "4K", "1440p", "1080p", "720p", "480p", "360p" }, Math.Max(0, Array.IndexOf(cd, A.CalidadDescarga)), i => { A.CalidadDescarga = cd[i]; G(); }, out _, true)));
        var fa = new[] { "mp3", "m4a", "opus" };
        de.Children.Add(Opcion("Formato de audio predeterminado", "MP3 funciona en cualquier lugar. M4A y Opus conservan mejor la calidad original.",
            UI.Chips(new[] { "MP3", "M4A", "Opus" }, Math.Max(0, Array.IndexOf(fa, A.FormatoAudio)), i => { A.FormatoAudio = fa[i]; G(); }, out _, true)));
        de.Children.Add(Opcion("Descargas al mismo tiempo", "Más descargas a la vez pueden hacer que cada una vaya más lenta.",
            UI.Chips(new[] { "1", "2", "3", "4" }, Math.Clamp(A.DescargasSimultaneas - 1, 0, 3), i => { A.DescargasSimultaneas = i + 1; G(); }, out _, true)));
        de.Children.Add(Interruptor("Detectar enlaces copiados", "Al volver a MojarraTube, si copiaste un enlace de video te ofrece verlo o descargarlo.", A.DetectarPortapapeles, v => { A.DetectarPortapapeles = v; G(); }));

        // Sesión
        var se = Seccion_("Sesión del navegador", "");
        var navs = new[] { "", "firefox", "edge", "chrome", "brave" };
        se.Children.Add(Opcion("Usar las cookies de tu navegador",
            "Solo hace falta para videos con restricción de edad, solo para miembros o si YouTube pide confirmar que no eres un robot. Firefox es el que mejor funciona; Chrome y Edge deben estar cerrados.",
            UI.Chips(new[] { "No usar", "Firefox", "Edge", "Chrome", "Brave" }, Math.Max(0, Array.IndexOf(navs, A.CookiesNavegador)), i => { A.CookiesNavegador = navs[i]; G(); }, out _, true)));

        // Privacidad
        var pr = Seccion_("Privacidad y datos", "");
        pr.Children.Add(Interruptor("Guardar historial", "Guarda en tu equipo lo que ves y buscas (para «Seguir viendo» y «Para ti»).", A.GuardarHistorial, v => { A.GuardarHistorial = v; G(); }));
        var filaPriv = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        filaPriv.Children.Add(Boton("Borrar historial", "", async () =>
        {
            if (App.Ventana != null && await App.Ventana.Confirmar("¿Borrar todo el historial?", "Se olvidarán los videos vistos y dónde te quedaste.", "Borrar"))
            { Almacen.Historial.Clear(); Almacen.Guardar("historial"); App.Ventana.Aviso("Historial borrado", ""); }
        }));
        filaPriv.Children.Add(Boton("Borrar búsquedas recientes", "", () =>
        {
            var copia = Almacen.Busquedas.ToList();
            Almacen.Busquedas.Clear(); Almacen.Guardar("busquedas");
            App.Ventana?.Aviso("Búsquedas recientes borradas", "", ("Deshacer", () => { Almacen.Busquedas.AddRange(copia); Almacen.Guardar("busquedas"); }));
            return Task.CompletedTask;
        }));
        filaPriv.Children.Add(Boton("Vaciar caché de imágenes", "", () => { Img.BorrarCache(); _ = ActualizarVersiones(); App.Ventana?.Aviso("Caché vaciada", ""); return Task.CompletedTask; }));
        pr.Children.Add(filaPriv);
        _cache = UI.Tenue("", 12);
        _cache.Margin = new Thickness(0, 6, 0, 0);
        pr.Children.Add(_cache);

        // Componentes
        var co = Seccion_("Componentes", "");
        co.Children.Add(UI.Tenue("MojarraTube usa yt-dlp para leer videos de YouTube y de otras páginas, FFmpeg para unir video y audio o convertir a MP3, y Deno para descifrar los videos de YouTube. Se descargan solos y yt-dlp se actualiza cada semana.", 13));
        _versiones = new TextBlock { FontSize = 13, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
        co.Children.Add(_versiones);
        var filaComp = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        filaComp.Children.Add(Boton("Buscar actualizaciones", "", async () =>
        {
            var r = await Herramientas.Instancia.ActualizarYtDlp();
            G();
            App.Ventana?.Aviso(r, "");
            await ActualizarVersiones();
        }));
        filaComp.Children.Add(Boton("Reinstalar componentes", "", async () =>
        {
            if (App.Ventana != null && await App.Ventana.Confirmar("¿Reinstalar los componentes?", "Se descargarán de nuevo yt-dlp, Deno y FFmpeg (unos 150 MB). Útil si algo dejó de funcionar.", "Reinstalar", false))
                _ = Herramientas.Instancia.Instalar(true);
        }));
        filaComp.Children.Add(Boton("Abrir carpeta de datos", "", () => { Process.Start("explorer.exe", $"\"{Marca.DirDatos}\""); return Task.CompletedTask; }));
        co.Children.Add(filaComp);
        _estadoComp = UI.Tenue("", 12);
        _estadoComp.Margin = new Thickness(0, 6, 0, 0);
        co.Children.Add(_estadoComp);

        // Acerca de
        var ac = Seccion_("Acerca de", "");
        ac.Children.Add(new TextBlock { Text = $"{Marca.Nombre} {Marca.Version}", FontWeight = FontWeights.SemiBold });
        ac.Children.Add(UI.Tenue("Parte de Mojarrium. Videos de YouTube y de la web, descargas y reproductor propio.", 13));
        var filaAc = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        filaAc.Children.Add(Boton("Atajos de teclado", "", () => { App.Ventana?.MostrarAtajos(); return Task.CompletedTask; }));
        ac.Children.Add(filaAc);
    }

    StackPanel Seccion_(string titulo, string icono)
    {
        var tarjeta = new Border { Style = UI.Estilo("Tarjeta"), Margin = new Thickness(0, 22, 0, 0), Padding = new Thickness(22, 18, 22, 18) };
        var sp = new StackPanel();
        var cab = UI.Horizontal(UI.Icono(icono, 17, "Acento"), new TextBlock { Text = titulo, Style = UI.Estilo("Subtitulo"), Margin = new Thickness(12, 0, 0, 0) });
        cab.Margin = new Thickness(0, 0, 0, 8);
        sp.Children.Add(cab);
        tarjeta.Child = sp;
        _raiz.Children.Add(tarjeta);
        return sp;
    }

    static FrameworkElement Opcion(string titulo, string ayuda, FrameworkElement control)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        sp.Children.Add(new TextBlock { Text = titulo, FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (ayuda.Length > 0) sp.Children.Add(new TextBlock { Text = ayuda, Style = UI.Estilo("TextoTenue"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 8) });
        sp.Children.Add(control);
        return sp;
    }

    static FrameworkElement Interruptor(string titulo, string ayuda, bool valor, Action<bool> alCambiar)
    {
        var contenido = new StackPanel();
        contenido.Children.Add(new TextBlock { Text = titulo, FontSize = 14, FontWeight = FontWeights.SemiBold });
        contenido.Children.Add(new TextBlock { Text = ayuda, Style = UI.Estilo("TextoTenue"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) });
        var cb = new CheckBox { Style = UI.Estilo("Interruptor"), IsChecked = valor, Content = contenido, Margin = new Thickness(0, 14, 0, 2) };
        System.Windows.Automation.AutomationProperties.SetName(cb, titulo);
        cb.Checked += (_, _) => alCambiar(true);
        cb.Unchecked += (_, _) => alCambiar(false);
        return cb;
    }

    FrameworkElement Carpeta(string titulo, Func<string> leer, Action<string> poner)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        sp.Children.Add(new TextBlock { Text = titulo, FontSize = 14, FontWeight = FontWeights.SemiBold });
        var fila = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        fila.ColumnDefinitions.Add(new ColumnDefinition());
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ruta = new Border { Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(10) };
        ruta.SetResourceReference(Border.BackgroundProperty, "Superficie2");
        var texto = new TextBlock { Text = leer(), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
        ruta.Child = texto;
        texto.ToolTip = leer();
        fila.Children.Add(ruta);
        var cambiar = UI.Boton("Cambiar…", null, () =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = titulo, InitialDirectory = System.IO.Directory.Exists(leer()) ? leer() : "" };
            if (d.ShowDialog() == true) { poner(d.FolderName); texto.Text = d.FolderName; texto.ToolTip = d.FolderName; }
        }, "Btn");
        cambiar.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(cambiar, 1);
        fila.Children.Add(cambiar);
        var abrir = UI.BotonIcono("", "Abrir la carpeta", () => { System.IO.Directory.CreateDirectory(leer()); GestorDescargas.MostrarEnCarpeta(leer()); });
        abrir.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(abrir, 2);
        fila.Children.Add(abrir);
        sp.Children.Add(fila);
        return sp;
    }

    static Button Boton(string texto, string icono, Func<Task> accion)
    {
        Button? b = null;
        b = UI.Boton(texto, icono, async () =>
        {
            b!.IsEnabled = false;
            try { await accion(); } finally { b.IsEnabled = true; }
        }, "BtnFantasma");
        b.Margin = new Thickness(0, 0, 8, 8);
        return b;
    }

    async Task ActualizarVersiones()
    {
        if (_versiones == null) return;
        _versiones.Text = "Comprobando versiones…";
        var v = await Task.Run(() => Herramientas.Instancia.Version);
        var c = await Task.Run(Img.TamanoCache);
        _versiones.Text = v;
        if (_cache != null) _cache.Text = c > 0 ? "Caché de imágenes: " + Texto.Bytes(c) : "";
    }
}
