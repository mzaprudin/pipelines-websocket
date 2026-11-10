using System.Diagnostics;
using System.Text.Json;
using WebSocketLoadTest.Clients;
using WebSocketLoadTest.Server;

namespace WebSocketLoadTest;

/// <summary>Runs the same independent receiver against a local or remote streaming generator.</summary>
internal static class Program
{
    /// <summary>Sampling and connection/shutdown deadlines.</summary>
    private static readonly TimeSpan SAMPLE_INTERVAL = TimeSpan.FromSeconds(1), LIFECYCLE_ALLOWANCE = TimeSpan.FromSeconds(30);
    /// <summary>Human-readable result serialization outside the measured window.</summary>
    private static readonly JsonSerializerOptions JSON_OPTIONS = new() { WriteIndented = true };
    /// <summary>Graceful shutdown command used by the owning benchmark runner.</summary>
    private const string SERVER_STOP_COMMAND = "stop";

    /// <summary>Runs one standalone streaming measurement and returns nonzero on failure.</summary>
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--help", StringComparer.Ordinal))
            {
                Console.WriteLine("Generator: --mode server --listen 0.0.0.0 --port 5050 --payload-bytes 128 --batch 20");
                Console.WriteLine("Receiver: --mode client --url ws://HOST:5050 --payload-bytes 128 --batch 20 --count 10 --duration 30 --warmup 5 [--std] --json result.json");
                Console.WriteLine("Local or LAN comparison: python3 scripts/streaming_benchmarks.py [--url ws://HOST:5050]");
                return 0;
            }
            var options = Options.Parse(args);
            if (options.Mode == WorkloadMode.Server) return await RunServerAsync(options).ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(options.Warmup + options.Duration + LIFECYCLE_ALLOWANCE);
            var endpoint = LoadServer.ProfileEndpoint(options.RemoteUri!, options.PayloadBytes, options.FramesPerBatch);
            var clients = new List<ILoadTestClient>(options.ClientCount);
            try
            {
                for (int i = 0; i < options.ClientCount; i++)
                    clients.Add(options.Standard ? new StandardClient() : new PipelineClient());
                var connections = new Task[clients.Count];
                for (int i = 0; i < clients.Count; i++) connections[i] = clients[i].ConnectAsync(endpoint, deadline.Token);
                await Task.WhenAll(connections).ConfigureAwait(false);
                Console.WriteLine($"Connected {clients.Count} {(options.Standard ? "ClientWebSocket" : "Pipelines")} clients to {endpoint}; {options.PayloadBytes} payload bytes, {options.FramesPerBatch} frames/batch; warmup {options.Warmup.TotalSeconds}s.");
                await Task.Delay(options.Warmup, deadline.Token).ConfigureAwait(false);
                var summary = await MeasureAsync(clients, endpoint, options, args, deadline.Token).ConfigureAwait(false);
                foreach (var client in clients) client.EnsureHealthy();
                if (summary.Messages <= 0 || summary.Bytes <= 0) throw new InvalidDataException("No streaming traffic was measured.");
                Console.WriteLine(FormattableString.Invariant($"Avg Msg/s: {summary.MessagesPerSecond:N0}; payload Mbps: {summary.MegabitsPerSecond:F2}; GC {summary.Gen0Collections}/{summary.Gen1Collections}/{summary.Gen2Collections}"));
                if (options.JsonPath is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.JsonPath))!);
                    await File.WriteAllTextAsync(options.JsonPath, JsonSerializer.Serialize(summary, JSON_OPTIONS) + Environment.NewLine, deadline.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                // Stop/observe clients outside timing; the generator belongs to another process.
                var shutdownTasks = new Task[clients.Count];
                for (int i = 0; i < clients.Count; i++) shutdownTasks[i] = clients[i].DisposeAsync().AsTask();
                await Task.WhenAll(shutdownTasks).ConfigureAwait(false);
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Streaming benchmark failed: {error}");
            return 1;
        }
    }

    /// <summary>Runs an independently hosted generator until Ctrl+C or an explicit duration expires.</summary>
    private static async Task<int> RunServerAsync(Options options)
    {
        using var stop = new CancellationTokenSource();
        if (options.Duration != Timeout.InfiniteTimeSpan) stop.CancelAfter(options.Duration);
        ConsoleCancelEventHandler onCancel = (_, signal) => { signal.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            await using var server = new LoadServer(options.Port, options.PayloadBytes, options.FramesPerBatch, options.ListenAddress);
            var listenUri = new UriBuilder(server.Uri) { Host = options.ListenAddress.ToString() }.Uri;
            Console.WriteLine($"Listening: {listenUri}");
            Console.WriteLine($"Payload {options.PayloadBytes} bytes; batch {options.FramesPerBatch} frames / {server.BatchBytes} bytes. Stop with Ctrl+C.");
            try
            {
                if (options.ControlStdin)
                {
                    var command = await Console.In.ReadLineAsync(stop.Token).ConfigureAwait(false);
                    if (command is not null && command != SERVER_STOP_COMMAND)
                        throw new ArgumentException("Expected the generator stop command or end of input.");
                    await stop.CancelAsync().ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            server.EnsureHealthy();
            return 0;
        }
        finally { Console.CancelKeyPress -= onCancel; }
    }

    /// <summary>Records actual counter deltas over elapsed time after all clients have warmed up.</summary>
    private static async Task<StreamingSummary> MeasureAsync(List<ILoadTestClient> clients, Uri endpoint, Options options, string[] args, CancellationToken token)
    {
        var samples = new List<StreamingSample>();
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        int gen0Before = GC.CollectionCount(0), gen1Before = GC.CollectionCount(1), gen2Before = GC.CollectionCount(2);
        var initial = CounterSnapshot.Read(clients);
        var previous = initial;
        var startedAt = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        double previousSeconds = 0;
        while (timer.Elapsed < options.Duration)
        {
            var remaining = options.Duration - timer.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            var delay = TimeSpan.FromMilliseconds(Math.Ceiling(Math.Min(remaining.TotalMilliseconds, SAMPLE_INTERVAL.TotalMilliseconds)));
            await Task.Delay(delay, token).ConfigureAwait(false);
            var current = CounterSnapshot.Read(clients);
            double seconds = timer.Elapsed.TotalSeconds;
            var window = MeasurementWindow.Between(previous.Messages, previous.Bytes, current.Messages, current.Bytes, seconds - previousSeconds);
            samples.Add(new StreamingSample(seconds, window.Messages, window.Bytes, window.MessagesPerSecond));
            foreach (var client in clients) client.EnsureHealthy();
            Console.WriteLine(FormattableString.Invariant($"{seconds:F2}s: {window.MessagesPerSecond:N0} msg/s"));
            previous = current;
            previousSeconds = seconds;
        }
        var total = MeasurementWindow.Between(initial.Messages, initial.Bytes, previous.Messages, previous.Bytes, previousSeconds);
        return new StreamingSummary(startedAt, string.Join(' ', args), options.Standard ? "standard" : "pipeline", clients.Count,
            options.Warmup.TotalSeconds, total.ElapsedSeconds, options.PayloadBytes, options.FramesPerBatch, options.BatchBytes,
            total.Messages, total.Bytes, total.MessagesPerSecond, total.MegabitsPerSecond,
            (process.TotalProcessorTime - cpuBefore).TotalMilliseconds, GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
            GC.CollectionCount(0) - gen0Before, GC.CollectionCount(1) - gen1Before, GC.CollectionCount(2) - gen2Before, samples,
            endpoint.IsLoopback ? "separate-process-loopback" : "remote-client", endpoint.ToString());
    }

}

/// <summary>One actual elapsed-time-normalized sample.</summary>
internal sealed record StreamingSample(double ElapsedSeconds, long Messages, long Bytes, double MessagesPerSecond);

/// <summary>Complete raw streaming evidence, including samples and process-level counters.</summary>
internal sealed record StreamingSummary(DateTimeOffset StartedAtUtc, string CommandLine, string ClientType, int ClientCount,
    double WarmupSeconds, double DurationSeconds, int PayloadBytes, int FramesPerBatch, int BatchBytes,
    long Messages, long Bytes, double MessagesPerSecond, double MegabitsPerSecond, double CpuMilliseconds,
    long AllocatedBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections, List<StreamingSample> Samples,
    string Topology, string Endpoint)
{
    /// <summary>Benchmark-process CPU cost per million completed messages; CpuScope identifies the included roles.</summary>
    public double CpuMillisecondsPerMillionMessages => Messages > 0 ? CpuMilliseconds * MESSAGES_PER_MILLION / Messages : 0;
    /// <summary>Process allocation volume per completed message, including measurement sampling.</summary>
    public double AllocatedBytesPerMessage => Messages > 0 ? (double)AllocatedBytes / Messages : 0;
    /// <summary>Mean logical-core consumption; 100 percent means one fully occupied logical core.</summary>
    public double CpuCorePercent => CpuMilliseconds / (DurationSeconds * MILLISECONDS_PER_SECOND) * PERCENT_SCALE;
    /// <summary>Roles included in CPU, allocation and GC counters for this process.</summary>
    public string CpuScope => "receiver-only";
    /// <summary>Identity of the measured process, independent of the generator and runner.</summary>
    public int ReceiverProcessId => Environment.ProcessId;
    /// <summary>Shared local/LAN sampling contract; periodic instrumentation is included.</summary>
    public string MeasurementProtocol => "receiver-process-periodic-v1";
    /// <summary>Presentation unit conversions.</summary>
    private const double MESSAGES_PER_MILLION = 1_000_000, MILLISECONDS_PER_SECOND = 1_000, PERCENT_SCALE = 100;
}
