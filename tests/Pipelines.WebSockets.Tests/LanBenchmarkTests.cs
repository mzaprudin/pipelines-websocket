using System.Diagnostics;
using System.Text.Json;
using WebSocketLoadTest;

namespace GapSonar.TransportLab.Tests;

/// <summary>Runs the standalone generator and each receiver in independent processes.</summary>
public sealed class LanBenchmarkTests
{
    /// <summary>Unusable proxy that proves receivers connect directly to their local peer.</summary>
    private const string UNREACHABLE_PROXY_URL = "http://127.0.0.1:1";
    /// <summary>Bounds process startup, measurement, and cleanup.</summary>
    private static readonly TimeSpan TEST_TIMEOUT = TimeSpan.FromSeconds(15);

    /// <summary>CLI roles exchange the one-KiB profile and identify process-separated loopback accurately.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerAndClient_SeparateProcesses_RecordOneKiBProfile(bool standard)
    {
        const int EXPECTED_PAYLOAD_BYTES = 1_024;
        using var timeout = new CancellationTokenSource(TEST_TIMEOUT);
        var report = Path.Combine(Path.GetTempPath(), $"ws-lan-{Guid.NewGuid():N}.json");
        using var generator = Start("--mode", "server", "--listen", "127.0.0.1", "--port", "0", "--duration", "4", "--batch", "1");
        Process? receiver = null;
        try
        {
            var listening = await generator.StandardOutput.ReadLineAsync(timeout.Token);
            const string LISTENING_PREFIX = "Listening: ";
            Assert.StartsWith(LISTENING_PREFIX, listening);
            var endpoint = listening![LISTENING_PREFIX.Length..];
            var arguments = new List<string> { "--mode", "client", "--url", endpoint, "--count", "1", "--warmup", "0", "--duration", "1", "--batch", "1", "--json", report };
            if (standard) arguments.Add("--std");
            receiver = Start(arguments.ToArray());
            var output = receiver.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = receiver.StandardError.ReadToEndAsync(timeout.Token);
            await receiver.WaitForExitAsync(timeout.Token);
            Assert.True(receiver.ExitCode == 0, await error);
            Assert.Contains("1024 payload bytes", await output);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(report, timeout.Token));
            var root = document.RootElement;
            Assert.Equal(EXPECTED_PAYLOAD_BYTES, root.GetProperty("PayloadBytes").GetInt32());
            Assert.Equal(1, root.GetProperty("FramesPerBatch").GetInt32());
            Assert.Equal("separate-process-loopback", root.GetProperty("Topology").GetString());
            Assert.Equal("receiver-only", root.GetProperty("CpuScope").GetString());
            Assert.True(root.GetProperty("Messages").GetInt64() > 0);
            Assert.True(root.GetProperty("MegabitsPerSecond").GetDouble() > 0);
            Assert.True(root.GetProperty("CpuMillisecondsPerMillionMessages").GetDouble() >= 0);
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
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
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
