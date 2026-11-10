namespace WebSocketLoadTest;

/// <summary>Counter deltas normalized by elapsed time, independent of the warmup and sampling interval.</summary>
public readonly record struct MeasurementWindow(long Messages, long Bytes, double ElapsedSeconds)
{
    /// <summary>Actual complete-message rate over this measurement window.</summary>
    public double MessagesPerSecond => Messages / ElapsedSeconds;
    /// <summary>Application payload bandwidth; excludes WebSocket and TCP headers.</summary>
    public double MegabitsPerSecond => Bytes * BITS_PER_BYTE / BITS_PER_MEGABIT / ElapsedSeconds;
    /// <summary>Unit conversions for payload bandwidth.</summary>
    private const double BITS_PER_BYTE = 8, BITS_PER_MEGABIT = 1_000_000;

    /// <summary>Excludes counters accumulated before the measured interval.</summary>
    public static MeasurementWindow Between(long startMessages, long startBytes, long endMessages, long endBytes, double elapsedSeconds)
    {
        if (elapsedSeconds <= 0 || !double.IsFinite(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (endMessages < startMessages || endBytes < startBytes) throw new ArgumentException("Counters must be monotonic.");
        return new MeasurementWindow(endMessages - startMessages, endBytes - startBytes, elapsedSeconds);
    }
}
