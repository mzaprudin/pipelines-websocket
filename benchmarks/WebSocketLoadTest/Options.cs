using System.Globalization;
using System.Net;
using WebSocketLoadTest.Server;

namespace WebSocketLoadTest;

/// <summary>Execution roles for a colocated benchmark or independent LAN processes.</summary>
internal enum WorkloadMode { Loopback, Server, Client }

/// <summary>Validated connection, payload and measurement configuration.</summary>
internal sealed record Options(int ClientCount, TimeSpan Duration, TimeSpan Warmup, bool Standard, int Port,
    string? JsonPath, WorkloadMode Mode, Uri? RemoteUri, IPAddress ListenAddress, int PayloadBytes, int FramesPerBatch)
{
    /// <summary>Portable benchmark defaults and network bounds.</summary>
    private const int DEFAULT_CLIENTS = 10, DEFAULT_DURATION_SECONDS = 30, DEFAULT_WARMUP_SECONDS = 5,
        DEFAULT_SERVER_PORT = 5_050, MAX_PORT = 65_535;
    /// <summary>Wire bytes per prepared server write.</summary>
    public int BatchBytes => (PayloadBytes + StreamingPayload.HeaderBytes(PayloadBytes)) * FramesPerBatch;

    /// <summary>Validates the full configuration before opening listeners or remote connections.</summary>
    public static Options Parse(string[] args)
    {
        int clients = DEFAULT_CLIENTS, duration = DEFAULT_DURATION_SECONDS, warmup = DEFAULT_WARMUP_SECONDS, port = 0;
        int payload = StreamingPayload.DEFAULT_BYTES, batch = LoadServer.FRAMES_PER_BATCH;
        bool standard = false, explicitPort = false, explicitDuration = false;
        string? json = null;
        var mode = WorkloadMode.Loopback;
        Uri? remote = null;
        IPAddress? listen = null;
        for (int i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            if (argument == "--std") { standard = true; continue; }
            if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {argument}.");
            var value = args[++i];
            switch (argument)
            {
                case "--json": json = value; continue;
                case "--mode": mode = value switch { "loopback" => WorkloadMode.Loopback, "server" => WorkloadMode.Server, "client" => WorkloadMode.Client, _ => throw new ArgumentException("Use mode loopback, server or client.") }; continue;
                case "--url": remote = new Uri(value, UriKind.Absolute); continue;
                case "--listen": listen = IPAddress.Parse(value); continue;
            }
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                throw new ArgumentException($"Invalid integer for {argument}.");
            switch (argument)
            {
                case "--count": clients = number; break;
                case "--duration": duration = number; explicitDuration = true; break;
                case "--warmup": warmup = number; break;
                case "--port": port = number; explicitPort = true; break;
                case "--payload-bytes": payload = number; break;
                case "--batch": batch = number; break;
                default: throw new ArgumentException($"Unknown option {argument}.");
            }
        }
        if (clients <= 0 || duration <= 0 || warmup < 0 || port < 0 || port > MAX_PORT)
            throw new ArgumentOutOfRangeException(nameof(args), "Require positive count/duration, nonnegative warmup, and a valid port.");
        StreamingPayload.ValidateLength(payload);
        if (batch <= 0 || batch > StreamingPayload.MAX_BATCH_BYTES / (payload + StreamingPayload.HeaderBytes(payload)))
            throw new ArgumentOutOfRangeException(nameof(args), "Prepared batches must fit the bounded buffer.");
        if (mode == WorkloadMode.Client && (remote is null || remote.Scheme != "ws" || remote.UserInfo.Length != 0))
            throw new ArgumentException("Client mode requires --url ws://HOST:PORT without credentials.");
        if (mode != WorkloadMode.Client && remote is not null) throw new ArgumentException("--url requires --mode client.");
        if (mode == WorkloadMode.Server && !explicitPort) port = DEFAULT_SERVER_PORT;
        return new Options(clients, mode == WorkloadMode.Server && !explicitDuration ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(duration),
            TimeSpan.FromSeconds(warmup), standard, port, json, mode, remote,
            listen ?? (mode == WorkloadMode.Server ? IPAddress.Any : IPAddress.Loopback), payload, batch);
    }
}
