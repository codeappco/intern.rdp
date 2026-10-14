using RemoteDesk.Server;

static class SecurityPolicyTests
{
    public static void Run()
    {
        static void Check(bool pass, string name)
        {
            if (!pass) throw new Exception(name);
            Console.WriteLine("PASS " + name);
        }
        var desktop = new WebSocketOriginPolicy([]);
        Check(desktop.Allows(false, []), "desktop policy allows absent Origin");
        Check(!desktop.Allows(true, ["https://trusted.example"]), "default policy denies all web origins");
        var web = new WebSocketOriginPolicy(["https://trusted.example"]);
        Check(web.Allows(true, ["https://trusted.example"]), "explicit web allowlist");
        Check(web.Allows(false, []), "web allowlist retains native compatibility");
        foreach (var origin in new[] { "null", "", "https://trusted.example.evil.example", "http://trusted.example", "https://trusted.example:444", "https://trusted.example/path", "https://trusted.example/", "https://trusted.example?x", "https://trusted.example#x", "https://user@trusted.example", "https://evil.example", "https://trusted.example, https://evil.example" })
            Check(!web.Allows(true, [origin]), "invalid/unlisted origin rejected: " + origin);
        Check(!web.Allows(true, ["https://trusted.example", "https://trusted.example"]), "multiple Origin values rejected");
        try { _ = new WebSocketOriginPolicy(["*"]); throw new Exception("Wildcard accepted"); }
        catch (ArgumentException) { Console.WriteLine("PASS wildcard configuration fails closed"); }

        var clock = new TestClock();
        var limiter = new ConnectionAttemptLimiter(clock);
        var client = new ConnectionAttemptLimiter.Window();
        for (int i = 0; i < 10; i++) Check(limiter.TryAcquire("ip1", client, out _), "client budget " + (i + 1));
        Check(!limiter.TryAcquire("ip1", client, out bool log) && log, "eleventh client attempt denied and logged");
        Check(!limiter.TryAcquire("ip1", client, out log) && !log, "repeated denial log suppressed");
        for (int i = 0; i < 20; i++) Check(limiter.TryAcquire("ip1", new(), out _), "reconnect shares IP budget " + (i + 11));
        Check(!limiter.TryAcquire("ip1", new(), out _), "thirty-first IP attempt denied across reconnects");
        Check(limiter.TryAcquire("ip2", new(), out _), "independent IP is not blocked");
        clock.Advance(TimeSpan.FromSeconds(59));
        Check(!limiter.TryAcquire("ip1", new(), out _), "budget cannot reset before window elapses");
        clock.Advance(TimeSpan.FromSeconds(1));
        Check(limiter.TryAcquire("ip1", client, out _), "expired IP and client budgets recover");

        var bounded = new ConnectionAttemptLimiter(clock, maxIpEntries: 1);
        Check(bounded.TryAcquire("one", new(), out _), "partition allocated");
        Check(!bounded.TryAcquire("two", new(), out _), "partition capacity fails closed");
        clock.Advance(TimeSpan.FromMinutes(1));
        Check(bounded.TryAcquire("two", new(), out _), "expired partitions reclaimed");
        var concurrent = new ConnectionAttemptLimiter(clock);
        int accepted = 0;
        Parallel.For(0, 100, i => { if (concurrent.TryAcquire("shared", new(), out _)) Interlocked.Increment(ref accepted); });
        Check(accepted == 30, "concurrent connections cannot exceed IP limit");
        Check(limiter.LogTag("ip1") == limiter.LogTag("ip1") && limiter.LogTag("ip1") != limiter.LogTag("ip2"), "log tags correlate without storing raw IP");
    }

    private sealed class TestClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(TimeSpan duration) => ticks += duration.Ticks;
    }
}
