using System.Text.Json.Serialization;
using GapSonar.Core.Shared.Infrastructure.Serialization;

namespace GapSonar.TransportLab.Support;

/// <summary>Identical seven-field contract for every JSON benchmark implementation.</summary>
public sealed class Trade
{
    /// <summary>Exchange trade identifier.</summary>
    public long Id { get; set; }
    /// <summary>Trade price.</summary>
    public double Price { get; set; }
    /// <summary>Trade quantity.</summary>
    public decimal Amount { get; set; }
    /// <summary>Repeated bounded symbol, optionally interned by JsonPopulator.</summary>
    [JsonIntern]
    public string Symbol { get; set; } = string.Empty;
    /// <summary>Exchange timestamp in milliseconds.</summary>
    public long Timestamp { get; set; }
    /// <summary>Trade aggressor flag.</summary>
    public bool IsBuyerMaker { get; set; }
    /// <summary>ISO timestamp accompanying the trade.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Generated framework baseline for the same contract.</summary>
[JsonSerializable(typeof(Trade))]
internal partial class TradeJsonContext : JsonSerializerContext;
