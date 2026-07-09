using System.Net;
using System.Net.Sockets;
using System.Text;
using ClaudeUsageTray.Localization;

namespace ClaudeUsageTray.Auth;

public sealed record AuthCodeResult(string? Code, string? State, string? Error);

/// <summary>
/// Minimal raw-socket HTTP listener for the OAuth loopback redirect. Deliberately
/// uses TcpListener rather than HttpListener: HttpListener's http.sys URL prefixes
/// can require a URL ACL reservation (admin rights) on some Windows configurations,
/// while binding a plain socket to 127.0.0.1 never does.
/// </summary>
public sealed class LoopbackListener : IDisposable
{
    private readonly TcpListener _listener;
    public int Port { get; }

    private LoopbackListener(TcpListener listener, int port)
    {
        _listener = listener;
        Port = port;
    }

    public static LoopbackListener BindFirstAvailable(IEnumerable<int> candidatePorts)
    {
        foreach (var port in candidatePorts)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return new LoopbackListener(listener, port);
            }
            catch (SocketException)
            {
                // Port already in use - try the next candidate.
            }
        }

        throw new InvalidOperationException(Strings.LoopbackNoPort);
    }

    public async Task<AuthCodeResult> WaitForRedirectAsync(CancellationToken cancellationToken)
    {
        using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
        using var stream = client.GetStream();

        var requestLine = await ReadLineAsync(stream, cancellationToken);
        // Expect: "GET /callback?code=...&state=... HTTP/1.1"
        var parts = (requestLine ?? "").Split(' ');
        var target = parts.Length >= 2 ? parts[1] : "";

        // Drain remaining headers so the client doesn't see a connection reset.
        string? line;
        while (!string.IsNullOrEmpty(line = await ReadLineAsync(stream, cancellationToken))) { }

        var query = target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "";
        var parsed = ParseQueryString(query);

        var result = new AuthCodeResult(
            parsed.GetValueOrDefault("code"),
            parsed.GetValueOrDefault("state"),
            parsed.GetValueOrDefault("error"));

        var body = result.Error is null
            ? $"<html><body style='font-family:sans-serif'><h2>{WebUtility.HtmlEncode(Strings.LoopbackSuccessTitle)}</h2><p>{WebUtility.HtmlEncode(Strings.LoopbackSuccessBody)}</p></body></html>"
            : $"<html><body style='font-family:sans-serif'><h2>{WebUtility.HtmlEncode(Strings.LoopbackFailureTitle)}</h2><p>{WebUtility.HtmlEncode(result.Error)}</p></body></html>";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(response, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);

        return result;
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            var key = idx >= 0 ? pair[..idx] : pair;
            var value = idx >= 0 ? pair[(idx + 1)..] : "";
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        return result;
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>();
        var single = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(single.AsMemory(0, 1), cancellationToken);
            if (read == 0)
                return buffer.Count == 0 ? null : Encoding.ASCII.GetString(buffer.ToArray());
            if (single[0] == '\n')
            {
                if (buffer.Count > 0 && buffer[^1] == '\r')
                    buffer.RemoveAt(buffer.Count - 1);
                return Encoding.ASCII.GetString(buffer.ToArray());
            }
            buffer.Add(single[0]);
        }
    }

    public void Dispose() => _listener.Stop();
}
