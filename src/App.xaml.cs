using System.Windows;
using Microsoft.Win32;

namespace MojarraTube;

public partial class App : Application
{
    public static MainWindow? Ventana { get; set; }

    /// <summary>Cambia entre tema oscuro y claro sin reiniciar (todos los colores son dinámicos).</summary>
    public static void AplicarTema(string tema)
    {
        var claro = tema == "claro" || (tema == "sistema" && SistemaEnClaro());
        var dic = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Tema/{(claro ? "Claro" : "Oscuro")}.xaml") };
        var md = Current.Resources.MergedDictionaries;
        md[0] = dic;
        Ventana?.TemaCambiado(claro);
    }

    public static bool SistemaEnClaro()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }
}
