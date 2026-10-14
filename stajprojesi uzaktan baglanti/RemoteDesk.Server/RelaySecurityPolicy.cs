using System.Security.Cryptography;
using System.Text;

namespace RemoteDesk.Server;

public sealed class WebSocketOriginPolicy
{
    private readonly HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase);

    public WebSocketOriginPolicy(IEnumerable<string> origins)
    {
        foreach (var origin in origins)
            allowed.Add(Normalize(origin) ?? throw new ArgumentException("AllowedOrigins must contain HTTP(S) origins without paths, credentials or wildcards."));
    }

    public bool Allows(bool present, IReadOnlyList<string> values) =>
        !present || (values.Count == 1 && Normalize(values[0]) is { } origin && allowed.Contains(origin));

    private static string? Normalize(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace) ||
            value.IndexOfAny(['*', ',', '\\', '?', '#']) >= 0 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") || uri.UserInfo.Length != 0 ||
            value.IndexOf('/', value.IndexOf("://", StringComparison.Ordinal) + 3) >= 0)
            return null;
        return uri.GetLeftPart(UriPartial.Authority);
    }
}

// A rolling one-minute budget, instantiated separately for generation and guesses.
// No client-supplied ID or forwarding header is used for partitioning.
public sealed class ConnectionAttemptLimiter(TimeProvider? clock = null, int maxIpEntries = 10000)
{
    public const int ClientLimit = 10;
    public const int IpLimit = 30;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<string, Window> addresses = new();
    private readonly byte[] logKey = RandomNumberGenerator.GetBytes(32);

    public sealed class Window
    {
        internal readonly Queue<long> Attempts = new();
        internal bool LimitLogged;
    }

    public string LogTag(string address) => Convert.ToHexString(
        HMACSHA256.HashData(logKey, Encoding.UTF8.GetBytes(address)))[..16];

    public bool TryAcquire(string address, Window client, out bool logLimit)
    {
        lock (gate)
        {
            long now = time.GetTimestamp();
            // Expired partitions are removed even if that IP never connects again.
            foreach (var entry in addresses.ToArray())
            {
                Prune(entry.Value, now);
                if (entry.Value.Attempts.Count == 0) addresses.Remove(entry.Key);
            }
            logLimit = false;
            if (!addresses.TryGetValue(address, out var ip))
            {
                // Bound memory and fail closed under partition exhaustion.
                if (addresses.Count >= maxIpEntries) return false;
                addresses[address] = ip = new Window();
            }
            Prune(client, now);
            if (ip.Attempts.Count >= IpLimit || client.Attempts.Count >= ClientLimit)
            {
                var blocked = ip.Attempts.Count >= IpLimit ? ip : client;
                logLimit = !blocked.LimitLogged;
                blocked.LimitLogged = true;
                return false;
            }
            ip.Attempts.Enqueue(now);
            client.Attempts.Enqueue(now);
            return true;
        }
    }

    private void Prune(Window window, long now)
    {
        while (window.Attempts.TryPeek(out long first) && time.GetElapsedTime(first, now) >= TimeSpan.FromMinutes(1))
            window.Attempts.Dequeue();
        if (window.Attempts.Count == 0) window.LimitLogged = false;
    }
}
