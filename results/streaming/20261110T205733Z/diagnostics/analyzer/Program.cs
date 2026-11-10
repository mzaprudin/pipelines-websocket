using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

// Diagnostic post-processing only. AllocationTick samples are not exact per-type byte totals.
var converted = Path.ChangeExtension(args[0], ".etlx");
TraceLog.CreateFromEventPipeDataFile(args[0], converted);
using var trace = new TraceLog(converted);
var events = new List<object>();
var groups = new Dictionary<string, (int Count, long Bytes)>();
foreach (var data in trace.Events)
{
    if (data is not GCAllocationTickTraceData allocation) continue;
    var stack = new List<string>();
    for (var frame = data.CallStack(); frame != null && stack.Count < 64; frame = frame.Caller)
        stack.Add(frame.CodeAddress.FullMethodName);
    var amount = allocation.AllocationAmount64 > 0 ? allocation.AllocationAmount64 : allocation.AllocationAmount;
    var type = allocation.TypeName ?? "<unknown>";
    var previous = groups.GetValueOrDefault(type);
    groups[type] = (previous.Count + 1, previous.Bytes + amount);
    events.Add(new { data.TimeStampRelativeMSec, data.ProcessID, data.ThreadID, Type = type,
        SampledAllocationBytes = amount, Stack = stack });
}
Console.WriteLine(JsonSerializer.Serialize(new {
    TraceEventVersion = typeof(TraceLog).Assembly.GetName().Version?.ToString(),
    trace.EventsLost,
    Interpretation = "GCAllocationTick samples; sampled weights do not give exact allocation totals by type.",
    Types = groups.OrderByDescending(item => item.Value.Bytes).Select(item => new {
        Type = item.Key, Samples = item.Value.Count, SampledAllocationBytes = item.Value.Bytes }),
    Events = events
}, new JsonSerializerOptions { WriteIndented = true }));
