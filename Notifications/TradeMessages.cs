using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace cAlgo.Robots;

// The relay messages this cBot sends, and the one it acts on, as JSON text. Every message type
// and field name of the protocol that the cBot uses lives here.
//
// Pattern: Factory Method (static). One method per outgoing message type builds it complete, so no
// caller ever assembles protocol JSON by hand; TryReadDecision is the one way in.
//
// Pure, no cAlgo dependency, unit tested.
public static class TradeMessages {
    public const string Source = "ctrader";

    // The trade_rejected reason: an order gate refused the approved signal. The message says which.
    public const string OrderRejected = "order_rejected";

    // Commission and swap make a close at exactly 0 rare; within half a cent of it is neither a
    // win nor a loss worth a sound.
    private const double BreakevenTolerance = 0.005;

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string Opportunity(PendingTradeSignalModel pending, string symbol) {
        SignalModel signal = pending.Signal;
        return Write(new TradeMessageModel {
            Type = "trade_opportunity",
            SignalId = pending.SignalId,
            Symbol = symbol,
            Side = signal.Level.Side == SignalSideModel.Sell ? "sell" : "buy",
            Level = signal.Level.Name,
            Pattern = signal.Label,
            Price = signal.Close,
            Timestamp = pending.DetectedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            Message = "Trading opportunity detected"
        });
    }

    public static string Opened(PendingTradeSignalModel pending, string symbol) {
        return Write(new TradeMessageModel {
            Type = "trade_opened", SignalId = pending.SignalId, Symbol = symbol, Message = "Trade opened successfully"
        });
    }

    // `detail` is the actual reason from the order pipeline, e.g. "Level VWAP already has an open
    // position on XAUUSD".
    public static string Rejected(PendingTradeSignalModel pending, string symbol, string reason, string detail) {
        return Write(new TradeMessageModel {
            Type = "trade_rejected",
            SignalId = pending.SignalId,
            Symbol = symbol,
            Reason = reason,
            Message = $"Trade could not be opened: {detail}"
        });
    }

    public static string Closed(string symbol, double netProfit) {
        return Write(new TradeMessageModel { Type = ClosedTradeType(netProfit), Symbol = symbol, NetProfit = netProfit });
    }

    private static string ClosedTradeType(double netProfit) {
        if (Math.Abs(netProfit) < BreakevenTolerance)
            return "trade_breakeven";

        return netProfit > 0.0 ? "trade_profit" : "trade_loss";
    }

    // True for a well-formed decision from the app: execute_trade (Place Trade) or dismiss_trade
    // (Dismiss). Any other text, including other message types and broken JSON, returns false.
    public static bool TryReadDecision(string text, out TradeDecisionModel decision) {
        decision = null;
        TradeMessageModel message;

        try {
            message = JsonSerializer.Deserialize<TradeMessageModel>(text, Json);
        } catch (JsonException) {
            return false;
        }

        bool? isApproved = message?.Type switch {
            "execute_trade" => true,
            "dismiss_trade" => false,
            _ => null
        };

        if (isApproved == null || string.IsNullOrWhiteSpace(message.SignalId))
            return false;

        decision = new TradeDecisionModel(message.SignalId.Trim(), isApproved.Value);
        return true;
    }

    private static string Write(TradeMessageModel message) {
        message.Source = Source;
        return JsonSerializer.Serialize(message, Json);
    }
}
