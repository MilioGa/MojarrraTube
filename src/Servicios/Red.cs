using System.IO;
using System.Net;
using System.Net.Http;

namespace MojarraTube;

public static class Red
{
    public static readonly HttpClient Cliente = Crear();

    static HttpClient Crear()
    {
        var h = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 12,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var c = new HttpClient(h) { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"Mozilla/5.0 (Windows NT 10.0; Win64; x64) {Marca.Nombre}/{Marca.Version}");
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es-MX,es;q=0.9,en;q=0.6");
        return c;
    }

    public static async Task<string> Texto(string url, CancellationToken ct = default, int segundos = 20)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(segundos));
        return await Cliente.GetStringAsync(url, cts.Token);
    }

    /// <summary>Descarga un archivo grande con progreso (bytes, total).</summary>
    public static async Task Archivo(string url, string destino, Action<long, long>? progreso, CancellationToken ct = default)
    {
        using var r = await Cliente.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        r.EnsureSuccessStatusCode();
        var total = r.Content.Headers.ContentLength ?? -1;
        var tmp = destino + ".part";
        await using (var origen = await r.Content.ReadAsStreamAsync(ct))
        await using (var f = File.Create(tmp))
        {
            var buf = new byte[1 << 16];
            long leidos = 0; int n; var ultimo = DateTime.MinValue;
            while ((n = await origen.ReadAsync(buf, ct)) > 0)
            {
                await f.WriteAsync(buf.AsMemory(0, n), ct);
                leidos += n;
                if ((DateTime.Now - ultimo).TotalMilliseconds > 120) { progreso?.Invoke(leidos, total); ultimo = DateTime.Now; }
            }
            progreso?.Invoke(leidos, total);
        }
        File.Move(tmp, destino, true);
    }
}
