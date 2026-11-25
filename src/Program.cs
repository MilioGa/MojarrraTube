using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;

namespace MojarraTube;

public static class Program
{
    static string Tubo => $"{Marca.Nombre}-abrir-{Environment.UserName}";

    [STAThread]
    public static int Main(string[] args)
    {
        try { return Arrancar(args); }
        catch (Exception e)
        {
            Marca.Registrar(e);
            Fatal(e);
            return 1;
        }
    }

    static int Arrancar(string[] args)
    {
        Marca.Iniciar();

        // Una sola ventana: si ya está abierta, le pasa los archivos/enlaces y termina
        using var mutex = new Mutex(true, $"{Marca.Nombre}-{Environment.UserName}", out var primera);
        if (!primera)
        {
            if (EnviarAInstancia(args)) return 0;
            // La otra copia no contesta (se quedó trabada): ofrecer cerrarla y seguir con esta
            var r = MessageBox.Show("MojarraTube ya está abierto pero no responde.\n\n¿Cerrarlo y abrirlo de nuevo?",
                Marca.Nombre, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return 0;
            CerrarOtrasCopias();
            try { if (!mutex.WaitOne(5000)) return 0; } catch (AbandonedMutexException) { }
        }

        Almacen.Cargar();
        var app = new App();
        app.InitializeComponent();
        MainWindow? ventana = null;
        app.DispatcherUnhandledException += (_, e) =>
        {
            Marca.Registrar(e.Exception);
            e.Handled = true;
            // Si falla antes de mostrar la ventana, avisar y salir en vez de quedarse abierto sin ventana
            if (ventana == null || !ventana.IsLoaded) { Fatal(e.Exception); app.Shutdown(1); return; }
            App.Ventana?.Aviso("Ocurrió un error inesperado. Se guardó en errores.log.", "\uE783");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Marca.Registrar(e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Marca.Registrar(e.Exception); e.SetObserved(); };
        App.AplicarTema(Almacen.Ajustes.Tema);
        ventana = new MainWindow(args);
        App.Ventana = ventana;
        Escuchar(ventana);
        var res = app.Run(ventana);
        Almacen.Escribir();
        return res;
    }

    /// <summary>Error al arrancar: lo muestra en pantalla (una app sin ventana no avisa de nada).</summary>
    static void Fatal(Exception e)
    {
        var raiz = e;
        while (raiz.InnerException != null) raiz = raiz.InnerException;
        try
        {
            MessageBox.Show($"MojarraTube no pudo iniciar.\n\n{raiz.GetType().Name}: {raiz.Message}\n\nDetalle guardado en:\n{Marca.ArchivoErrores}",
                Marca.Nombre, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
    }

    static void CerrarOtrasCopias()
    {
        var yo = Environment.ProcessId;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName))
        {
            try { if (p.Id != yo) { p.Kill(true); p.WaitForExit(5000); } } catch { }
            finally { p.Dispose(); }
        }
    }

    static bool EnviarAInstancia(string[] args)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", Tubo, PipeDirection.Out);
            c.Connect(3000);
            using var w = new StreamWriter(c, new UTF8Encoding(false));
            w.Write(string.Join("\n", args.Length == 0 ? new[] { "--mostrar" } : args));
            return true;
        }
        catch { return false; }
    }

    static void Escuchar(MainWindow v)
    {
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var s = new NamedPipeServerStream(Tubo, PipeDirection.In, 1);
                    s.WaitForConnection();
                    var texto = new StreamReader(s, Encoding.UTF8).ReadToEnd();
                    var lista = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    v.Dispatcher.BeginInvoke(() => v.AbrirArgumentos(lista, true));
                }
                catch { Thread.Sleep(500); }
            }
        }) { IsBackground = true, Name = "Instancias" }.Start();
    }
}
