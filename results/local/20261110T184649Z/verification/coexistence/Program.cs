using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using GapSonar.Core.Shared.Infrastructure.Serialization;
using GapSonar.TransportLab.Support;

const int MESSAGE_COUNT = 100, OUTPUT_CAPACITY_BYTES = 1_024;
var fixture = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "trade.json"));
var expected = JsonSerializer.Deserialize(fixture, TradeJsonContext.Default.Trade)!;
var trade = new Trade();
JsonPopulator.Populate(fixture, trade);
if (JsonSerializer.Serialize(trade) != JsonSerializer.Serialize(expected)) throw new InvalidDataException("Parsed trade differs from the framework baseline.");
var output = new ArrayBufferWriter<byte>(OUTPUT_CAPACITY_BYTES);
using var writer = new Utf8JsonWriter(output);
foreach (bool standard in new[] { false })
{
    await using var session = new RoundtripSession();
    await session.ConnectAsync(standard);
    var timer = Stopwatch.StartNew();
    for (int messageIndex = 0; messageIndex < MESSAGE_COUNT; messageIndex++)
    {
        trade.Id = expected.Id + messageIndex;
        output.Clear();
        writer.Reset(output);
        JsonPushulator.Push(trade, writer);
        writer.Flush();
        await session.RoundtripAsync(output.WrittenMemory, WebSocketMessageType.Text);
    }
    if (session.VerifiedMessages != MESSAGE_COUNT) throw new InvalidDataException("Missing responses.");
    Console.WriteLine($"{(standard ? "ClientWebSocket" : "Pipelines")}: {session.VerifiedMessages} ordered JSON roundtrips verified in {timer.Elapsed.TotalMilliseconds:F1} ms");
}
Console.WriteLine("PASS: fixture parsing, JSON serialization, payload integrity, message ordering, and the public WebSocket client.");

var assemblies = new[] { typeof(JsonPopulator).Assembly, typeof(GapSonar.Core.Exchange.Networking.Core.FastWebSocketClient).Assembly };
var publicTypes = assemblies.SelectMany(assembly => assembly.GetExportedTypes()).ToArray();
if (publicTypes.GroupBy(type => type.FullName).Any(group => group.Count() != 1))
    throw new InvalidOperationException("Public type names collide between the libraries.");
if (publicTypes.Any(type => type.Name.StartsWith("PoolRental", StringComparison.Ordinal)))
    throw new InvalidOperationException("PoolRental must be internal.");
var upstream = System.Reflection.Assembly.LoadFrom(Path.GetFullPath("../source/src/GapSonar.TransportLab/bin/Release/net10.0/GapSonar.TransportLab.dll"));
const System.Reflection.BindingFlags PUBLIC_MEMBERS = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
foreach (var oldType in upstream.GetExportedTypes())
{
    if (oldType.Name.StartsWith("PoolRental", StringComparison.Ordinal)) continue;
    var newType = publicTypes.Single(type => type.FullName == oldType.FullName);
    var before = oldType.GetMembers(PUBLIC_MEMBERS).Select(member => member.ToString()).Order().ToArray();
    var after = newType.GetMembers(PUBLIC_MEMBERS).Select(member => member.ToString()).Order().ToArray();
    if (!before.SequenceEqual(after)) throw new InvalidOperationException($"Public API drift: {oldType.FullName}");
}
var symbol = new GapSonar.Core.Shared.SymbolId(GapSonar.Core.Shared.ExchangeId.MexcFutures, new GapSonar.Core.Shared.SymbolKey("BTC", "USDT"), GapSonar.Core.Shared.SymbolType.LinearPerpetual, "BTC_USDT");
RouterBenchmark.SymbolLookup lookup = (ReadOnlySpan<byte> bytes, out GapSonar.Core.Shared.SymbolId id) => { id = symbol; return bytes.SequenceEqual("BTC_USDT"u8); };
var router = new RouterBenchmark.CustomMessageRouter(lookup);
if (!router.Route(new System.Buffers.ReadOnlySequence<byte>("{\"symbol\":\"BTC_USDT\"}"u8.ToArray()), out var actual, out _) || actual != symbol)
    throw new InvalidDataException("Exported router failed.");
Console.WriteLine($"PASS: {publicTypes.Length} distinct exported types; original API retained except internal PoolRental; public router verified.");
