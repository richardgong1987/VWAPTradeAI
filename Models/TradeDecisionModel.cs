namespace cAlgo.Robots;

// The user's answer to a trade opportunity, as it arrived from the app: Place Trade
// (execute_trade) or Dismiss (dismiss_trade).
public class TradeDecisionModel {
    public TradeDecisionModel(string signalId, bool isApproved) {
        SignalId = signalId;
        IsApproved = isApproved;
    }

    public string SignalId { get; }

    public bool IsApproved { get; }
}
