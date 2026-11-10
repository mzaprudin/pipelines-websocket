using System.Runtime.CompilerServices;

namespace GapSonar.Core.Exchange.Infrastructure.Diagnostics;

/// <summary>Optional low-cardinality identity for a transport connection.</summary>
public readonly record struct WebSocketMetricContext(string Exchange, string Socket)
{
    /// <summary>Whether both identity labels are set.</summary>
    public bool IsEnabled => !string.IsNullOrEmpty(Exchange) && !string.IsNullOrEmpty(Socket);
}

/// <summary>Receives transport counters without coupling the library to a metrics backend.</summary>
public interface IWebSocketMetricsSink
{
    /// <summary>Records an incoming frame.</summary>
    void AddFrameReceived(string exchange, string socket, int opcode, long bytes);
    /// <summary>Records an outgoing frame.</summary>
    void AddFrameSent(string exchange, string socket, int opcode, long bytes);
    /// <summary>Records an incoming protocol ping.</summary>
    void IncrementPingReceived(string exchange, string socket);
    /// <summary>Records an incoming protocol pong.</summary>
    void IncrementPongReceived(string exchange, string socket);
    /// <summary>Records an outgoing protocol ping.</summary>
    void IncrementPingSent(string exchange, string socket);
}

/// <summary>Opt-in, allocation-free dispatch for transport counters; disabled by default.</summary>
public static class WebSocketMetrics
{
    /// <summary>Process-wide optional sink.</summary>
    private static IWebSocketMetricsSink? _sink;
    /// <summary>Installs or removes the optional sink.</summary>
    public static void SetSink(IWebSocketMetricsSink? sink) => Volatile.Write(ref _sink, sink);
    /// <summary>Records a received frame when metrics are enabled.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddFrameReceived(in WebSocketMetricContext context, int opcode, long bytes)
    {
        if (context.IsEnabled) Volatile.Read(ref _sink)?.AddFrameReceived(context.Exchange, context.Socket, opcode, bytes);
    }
    /// <summary>Records a sent frame when metrics are enabled.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddFrameSent(in WebSocketMetricContext context, int opcode, long bytes)
    {
        if (context.IsEnabled) Volatile.Read(ref _sink)?.AddFrameSent(context.Exchange, context.Socket, opcode, bytes);
    }
    /// <summary>Records an incoming ping.</summary>
    public static void IncrementPingReceived(in WebSocketMetricContext context)
    {
        if (context.IsEnabled) Volatile.Read(ref _sink)?.IncrementPingReceived(context.Exchange, context.Socket);
    }
    /// <summary>Records an incoming pong.</summary>
    public static void IncrementPongReceived(in WebSocketMetricContext context)
    {
        if (context.IsEnabled) Volatile.Read(ref _sink)?.IncrementPongReceived(context.Exchange, context.Socket);
    }
    /// <summary>Records an outgoing ping.</summary>
    public static void IncrementPingSent(in WebSocketMetricContext context)
    {
        if (context.IsEnabled) Volatile.Read(ref _sink)?.IncrementPingSent(context.Exchange, context.Socket);
    }
}
