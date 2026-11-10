using BenchmarkDotNet.Attributes;
using GapSonar.TransportLab.Support;

namespace GapSonar.TransportLab.Benchmarks;

/// <summary>Measures complete local echo roundtrips, including validation, peer work, and scheduling.</summary>
[MemoryDiagnoser]
public class WebSocketRoundtripBenchmarks
{
    /// <summary>Trade-sized and extended-length messages.</summary>
    private const int SMALL_PAYLOAD_BYTES = 128, LARGE_PAYLOAD_BYTES = 4_096;
    /// <summary>Common deterministic binary payload.</summary>
    private byte[] _payload = null!;
    /// <summary>One loopback connection per benchmark process.</summary>
    private RoundtripSession _session = null!;
    /// <summary>Payload size shared by both implementations.</summary>
    [Params(SMALL_PAYLOAD_BYTES, LARGE_PAYLOAD_BYTES)]
    public int PayloadBytes { get; set; }
    /// <summary>False measures the custom client; true measures the framework wrapper.</summary>
    [Params(false, true)]
    public bool StandardClient { get; set; }
    /// <summary>Creates and warms the connection outside measurements.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _payload = new byte[PayloadBytes];
        for (int i = 0; i < _payload.Length; i++) _payload[i] = unchecked((byte)i);
        _session = new RoundtripSession();
        await _session.ConnectAsync(StandardClient).ConfigureAwait(false);
        await _session.RoundtripAsync(_payload).ConfigureAwait(false);
    }
    /// <summary>Sends one message and waits until the identical reply has been verified.</summary>
    [Benchmark]
    public Task EchoRoundtrip() => _session.RoundtripAsync(_payload);
    /// <summary>Observes shutdown of both network peers.</summary>
    [GlobalCleanup]
    public async Task Cleanup() => await _session.DisposeAsync().ConfigureAwait(false);
}
