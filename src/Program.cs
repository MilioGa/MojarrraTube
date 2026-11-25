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
        Marca.Iniciar();

        // Una sola ventana: si ya está abierta, le pasa los archivos/enlaces y termina
        using var mutex = new Mutex(true, $"{Marca.Nombre}-{Environment.UserName}", out var primera);
        if (!primera) { EnviarAInstancia(args); return 0; }

        Almacen.Cargar();
        var app = new App();
        app.InitializeComponent();
        app.DispatcherUnhandledException += (_, e) => { Marca.Registrar(e.Exception); e.Handled = true; App.Ventana?.Aviso("Ocurrió un error inesperado. Se guardó en errores.log.", ""); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Marca.Registrar(e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Marca.Registrar(e.Exception); e.SetObserved(); };
        App.AplicarTema(Almacen.Ajustes.Tema);
        var ventana = new MainWindow(args);
        App.Ventana = ventana;
        Escuchar(ventana);
        var r = app.Run(ventana);
        Almacen.Escribir();
        return r;
    }

    static void EnviarAInstancia(string[] args)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", Tubo, PipeDirection.Out);
            c.Connect(3000);
            using var w = new StreamWriter(c, new UTF8Encoding(false));
            w.Write(string.Join("\n", args.Length == 0 ? new[] { "--mostrar" } : args));
        }
        catch { }
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
