using System.Text.Json.Serialization;

namespace cAlgo.Robots;

// One relay message as JSON (see rustwebsocket/README.md). TradeMessages fills in the fields each
// message type uses; unset fields are left out of the JSON.
public class TradeMessageModel {
    [JsonPropertyName("type")] public string Type { get; set; }

    [JsonPropertyName("source")] public string Source { get; set; }

    [JsonPropertyName("signal_id")] public string SignalId { get; set; }

    [JsonPropertyName("symbol")] public string Symbol { get; set; }

    [JsonPropertyName("side")] public string Side { get; set; }

    [JsonPropertyName("level")] public string Level { get; set; }

    [JsonPropertyName("pattern")] public string Pattern { get; set; }

    [JsonPropertyName("price")] public double? Price { get; set; }

    [JsonPropertyName("timestamp")] public string Timestamp { get; set; }

    [JsonPropertyName("reason")] public string Reason { get; set; }

    [JsonPropertyName("message")] public string Message { get; set; }

    [JsonPropertyName("net_profit")] public double? NetProfit { get; set; }
}
