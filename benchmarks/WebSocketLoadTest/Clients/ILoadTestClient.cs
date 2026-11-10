namespace WebSocketLoadTest.Clients;

/// <summary>Original streaming counter contract with explicit connection ownership.</summary>
public interface ILoadTestClient : IAsyncDisposable
{
    /// <summary>Connects and starts receiving the original continuous stream.</summary>
    Task ConnectAsync(Uri uri, CancellationToken token);
    /// <summary>Complete logical messages received since connection establishment.</summary>
    long MessagesReceived { get; }
    /// <summary>Application payload bytes received since connection establishment.</summary>
    long BytesReceived { get; }
    /// <summary>Fails on a silent receive-loop or connection error.</summary>
    void EnsureHealthy();
}
