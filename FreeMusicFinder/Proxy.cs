using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FreeMusicFinder;

/// <summary>
/// The "Proxy" setting, for places where Telegram cannot be reached directly. Either a Telegram
/// (MTProto) proxy, <see cref="MtProxyUrl"/>, or a SOCKS5 proxy, <see cref="Host"/> and the rest.
/// </summary>
internal sealed record ProxyChoice(string? MtProxyUrl, string Host, int Port, string? User, string? Password)
{
    public const string Help = "Use a Telegram proxy link (https://t.me/proxy?server=…&port=…&secret=…) or socks5://host:port.";

    /// <summary>
    /// Null for an empty setting (connect directly). Understands Telegram's own proxy links
    /// ("https://t.me/proxy?…", "tg://proxy?…", and "…/socks?…" for SOCKS5) and
    /// "socks5://[user:password@]host:port". Throws <see cref="FormatException"/> for anything else.
    /// </summary>
    public static ProxyChoice? Parse(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text; // "t.me/proxy?…"
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) throw NotUnderstood();

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is "socks5" or "socks5h" or "socks")
        {
            if (uri.Host.Length == 0 || uri.Port is <= 0 or > 65535) throw NotUnderstood();
            var login = uri.UserInfo.Split(':', 2);
            return new ProxyChoice(null, uri.Host, uri.Port,
                login[0].Length > 0 ? Uri.UnescapeDataString(login[0]) : null,
                login.Length > 1 ? Uri.UnescapeDataString(login[1]) : null);
        }

        // tg://proxy?… has "proxy" as its host; https://t.me/proxy?… has it as its path.
        var kind = scheme == "tg" ? uri.Host.ToLowerInvariant()
            : scheme is "https" or "http" && uri.Host.ToLowerInvariant() is "t.me" or "telegram.me" ? uri.AbsolutePath.Trim('/').ToLowerInvariant()
            : "";
        var values = Query(uri.Query);
        values.TryGetValue("server", out var server);
        if (string.IsNullOrEmpty(server) || !values.TryGetValue("port", out var portText) || !int.TryParse(portText, out var port) || port is <= 0 or > 65535)
            throw NotUnderstood();

        switch (kind)
        {
            case "proxy":
                if (!values.TryGetValue("secret", out var secret) || secret.Length == 0) throw NotUnderstood();
                return new ProxyChoice($"https://t.me/proxy?server={Uri.EscapeDataString(server)}&port={port}&secret={Uri.EscapeDataString(secret)}", server, port, null, null);
            case "socks":
                values.TryGetValue("user", out var user);
                values.TryGetValue("pass", out var password);
                return new ProxyChoice(null, server, port, string.IsNullOrEmpty(user) ? null : user, string.IsNullOrEmpty(password) ? null : password);
            default:
                throw NotUnderstood();
        }
    }

    private static FormatException NotUnderstood() => new("The proxy setting is not understood. " + Help);

    private static Dictionary<string, string> Query(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            values[Uri.UnescapeDataString(parts[0])] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
        }
        return values;
    }
}

/// <summary>Opens a connection through a SOCKS5 proxy (RFC 1928, with the name-and-password login of RFC 1929).</summary>
internal static class Socks5
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>A connection to <paramref name="host"/>:<paramref name="port"/> that runs through the proxy.</summary>
    public static async Task<TcpClient> ConnectAsync(ProxyChoice proxy, string host, int port)
    {
        using var timeout = new CancellationTokenSource(Patience);
        var ct = timeout.Token;
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(proxy.Host, proxy.Port, ct).ConfigureAwait(false);
            var stream = tcp.GetStream();

            var withLogin = proxy.User is not null;
            await stream.WriteAsync(withLogin ? new byte[] { 5, 2, 0, 2 } : new byte[] { 5, 1, 0 }, ct).ConfigureAwait(false);
            var method = await ReadAsync(stream, 2, ct).ConfigureAwait(false);
            if (method[0] != 5) throw new IOException("The proxy is not a SOCKS5 proxy.");
            if (method[1] == 2 && withLogin)
            {
                var user = Short(proxy.User!, "name");
                var password = Short(proxy.Password ?? "", "password", mayBeEmpty: true);
                await stream.WriteAsync(new byte[] { 1, (byte)user.Length }.Concat(user).Append((byte)password.Length).Concat(password).ToArray(), ct).ConfigureAwait(false);
                if ((await ReadAsync(stream, 2, ct).ConfigureAwait(false))[1] != 0) throw new IOException("The proxy did not accept the name and password.");
            }
            else if (method[1] != 0)
            {
                throw new IOException(withLogin ? "The proxy accepts none of the ways to log in." : "The proxy wants a name and password.");
            }

            byte[] address;
            if (IPAddress.TryParse(host, out var ip))
            {
                address = new[] { (byte)(ip.AddressFamily == AddressFamily.InterNetworkV6 ? 4 : 1) }.Concat(ip.GetAddressBytes()).ToArray();
            }
            else
            {
                var name = Short(host, "server name");
                address = new byte[] { 3, (byte)name.Length }.Concat(name).ToArray();
            }
            await stream.WriteAsync(new byte[] { 5, 1, 0 }.Concat(address).Append((byte)(port >> 8)).Append((byte)port).ToArray(), ct).ConfigureAwait(false);

            var reply = await ReadAsync(stream, 4, ct).ConfigureAwait(false);
            if (reply[0] != 5 || reply[1] != 0) throw new IOException($"The proxy could not connect to Telegram (answer {reply[1]}).");
            // The address the proxy connected from follows; it is of no use here.
            var rest = reply[3] switch
            {
                1 => 4,
                4 => 16,
                3 => (await ReadAsync(stream, 1, ct).ConfigureAwait(false))[0],
                _ => throw new IOException("The proxy answered in a way that is not SOCKS5."),
            };
            await ReadAsync(stream, rest + 2, ct).ConfigureAwait(false);
            return tcp;
        }
        catch (OperationCanceledException)
        {
            tcp.Dispose();
            throw new TimeoutException("The proxy did not answer.");
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    // SOCKS5 gives a name one byte for its length.
    private static byte[] Short(string text, string what, bool mayBeEmpty = false)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return bytes.Length <= 255 && (mayBeEmpty || bytes.Length > 0) ? bytes : throw new IOException($"The proxy {what} is empty or too long.");
    }

    private static async Task<byte[]> ReadAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
        return buffer;
    }
}
