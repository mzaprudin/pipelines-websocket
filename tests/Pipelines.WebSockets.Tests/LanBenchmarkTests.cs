using System.Diagnostics;
using System.Text.Json;
using WebSocketLoadTest;

namespace GapSonar.TransportLab.Tests;

/// <summary>Runs the standalone generator and each receiver in independent processes.</summary>
public sealed class LanBenchmarkTests
{
    /// <summary>Unusable proxy that proves receivers connect directly to their local peer.</summary>
    private const string UNREACHABLE_PROXY_URL = "http://127.0.0.1:1";
    /// <summary>Primary compact profile and explicitly retained one-KiB coverage.</summary>
    private const int COMPACT_PAYLOAD_BYTES = 128, COMPACT_BATCH_FRAMES = 20, ONE_KIB_BYTES = 1_024;
    /// <summary>Bounds process startup, measurement, and cleanup.</summary>
    private static readonly TimeSpan TEST_TIMEOUT = TimeSpan.FromSeconds(15);

    /// <summary>Both clients use independent process roles for compact and one-KiB profiles.</summary>
    [Theory]
    [InlineData(false, ONE_KIB_BYTES, 1)]
    [InlineData(true, ONE_KIB_BYTES, 1)]
    [InlineData(false, COMPACT_PAYLOAD_BYTES, COMPACT_BATCH_FRAMES)]
    [InlineData(true, COMPACT_PAYLOAD_BYTES, COMPACT_BATCH_FRAMES)]
    public async Task ServerAndClient_SeparateProcesses_RecordReceiverOnlyProfile(bool standard, int payloadBytes, int batch)
    {
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        var report = Path.Combine(Path.GetTempPath(), $"ws-lan-{Guid.NewGuid():N}.json");
        using var generator = Start("--mode", "server", "--listen", "127.0.0.1", "--port", "0",
            "--payload-bytes", payloadBytes.ToString(), "--batch", batch.ToString(), "--control-stdin");
        Process? receiver = null;
        try
        {
            var listening = await generator.StandardOutput.ReadLineAsync(timeout.Token);
            const string LISTENING_PREFIX = "Listening: ";
            Assert.StartsWith(LISTENING_PREFIX, listening);
            var endpoint = listening![LISTENING_PREFIX.Length..];
            var arguments = new List<string> { "--mode", "client", "--url", endpoint, "--count", "1", "--warmup", "0", "--duration", "1",
                "--payload-bytes", payloadBytes.ToString(), "--batch", batch.ToString(), "--json", report };
            if (standard) arguments.Add("--std");
            receiver = Start(arguments.ToArray());
            var output = receiver.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = receiver.StandardError.ReadToEndAsync(timeout.Token);
            await receiver.WaitForExitAsync(timeout.Token);
            Assert.True(receiver.ExitCode == 0, await error);
            Assert.Contains($"{payloadBytes} payload bytes", await output);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(report, timeout.Token));
            var root = document.RootElement;
            Assert.Equal(payloadBytes, root.GetProperty("PayloadBytes").GetInt32());
            Assert.Equal(batch, root.GetProperty("FramesPerBatch").GetInt32());
            Assert.Equal("separate-process-loopback", root.GetProperty("Topology").GetString());
            Assert.Equal("receiver-only", root.GetProperty("CpuScope").GetString());
            Assert.True(root.GetProperty("Messages").GetInt64() > 0);
            Assert.True(root.GetProperty("MegabitsPerSecond").GetDouble() > 0);
            Assert.True(root.GetProperty("CpuMillisecondsPerMillionMessages").GetDouble() >= 0);
            Assert.Equal(receiver.Id, root.GetProperty("ReceiverProcessId").GetInt32());
            Assert.NotEqual(generator.Id, root.GetProperty("ReceiverProcessId").GetInt32());
            Assert.Equal("receiver-process-periodic-v1", root.GetProperty("MeasurementProtocol").GetString());
            await generator.StandardInput.WriteLineAsync("stop".AsMemory(), timeout.Token);
            await generator.StandardInput.FlushAsync(timeout.Token);
            await generator.WaitForExitAsync(timeout.Token);
            Assert.True(generator.ExitCode == 0, await generator.StandardError.ReadToEndAsync(timeout.Token));
        }
        finally
        {
            if (receiver is not null)
            {
                if (!receiver.HasExited) receiver.Kill(entireProcessTree: true);
                receiver.Dispose();
            }
            if (!generator.HasExited) generator.Kill(entireProcessTree: true);
            File.Delete(report);
        }
    }

    /// <summary>Uses test runtime/dependency metadata to execute the referenced benchmark entry point.</summary>
    private static Process Start(params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // Prove that LAN traffic bypasses ambient proxies in both receiver implementations.
        info.Environment["HTTP_PROXY"] = UNREACHABLE_PROXY_URL;
        info.Environment["HTTPS_PROXY"] = UNREACHABLE_PROXY_URL;
        info.Environment["ALL_PROXY"] = UNREACHABLE_PROXY_URL;
        info.Environment["NO_PROXY"] = string.Empty;
        info.ArgumentList.Add("exec");
        info.ArgumentList.Add("--runtimeconfig");
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "GapSonar.TransportLab.Tests.runtimeconfig.json"));
        info.ArgumentList.Add("--depsfile");
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "GapSonar.TransportLab.Tests.deps.json"));
        info.ArgumentList.Add(typeof(MeasurementWindow).Assembly.Location);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException("Could not start the benchmark process.");
    }
}
