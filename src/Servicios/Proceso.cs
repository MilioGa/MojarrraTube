using System.Diagnostics;
using System.IO;
using System.Text;

namespace MojarraTube;

public record ResultadoProceso(int Codigo, string Salida, string Errores);

/// <summary>Ejecuta yt-dlp / ffmpeg sin ventana, con UTF-8 y la carpeta de componentes en el PATH.</summary>
public static class Proceso
{
    public static ProcessStartInfo Preparar(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Marca.DirComponentes,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        psi.Environment["PATH"] = Marca.DirComponentes + Path.PathSeparator + path;
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["NO_COLOR"] = "1";
        return psi;
    }

    public static async Task<ResultadoProceso> Ejecutar(string exe, IEnumerable<string> args, TimeSpan? limite = null,
        CancellationToken ct = default, Action<string>? alLeerLinea = null, Action<string>? alLeerError = null)
    {
        using var p = new Process { StartInfo = Preparar(exe, args), EnableRaisingEvents = true };
        var salida = new StringBuilder();
        var errores = new StringBuilder();
        var finSalida = new TaskCompletionSource();
        var finErrores = new TaskCompletionSource();
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) { finSalida.TrySetResult(); return; }
            lock (salida) salida.AppendLine(e.Data);
            alLeerLinea?.Invoke(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) { finErrores.TrySetResult(); return; }
            lock (errores) errores.AppendLine(e.Data);
            alLeerError?.Invoke(e.Data);
        };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (limite is { } l) cts.CancelAfter(l);
        try
        {
            await p.WaitForExitAsync(cts.Token);
            await Task.WhenAll(finSalida.Task, finErrores.Task).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException("El proceso tardó demasiado");
        }
        catch (TimeoutException) { }
        string s, e2;
        lock (salida) s = salida.ToString();
        lock (errores) e2 = errores.ToString();
        return new ResultadoProceso(p.ExitCode, s, e2);
    }
}
