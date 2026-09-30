namespace cAlgo.Robots;

// The broker's answer to a market order: filled, with the new position, or refused, with its error.
// A refusal is an expected outcome (it goes back to the app as the reason), not an exception.
public class BrokerOrderResultModel {
    private BrokerOrderResultModel(PositionEntryModel position, string error) {
        Position = position;
        Error = error;
    }

    public static BrokerOrderResultModel Filled(PositionEntryModel position) {
        return new BrokerOrderResultModel(position, "");
    }

    public static BrokerOrderResultModel Refused(string error) {
        return new BrokerOrderResultModel(null, error);
    }

    public bool IsFilled => Position != null;

    // Null when refused.
    public PositionEntryModel Position { get; }

    public string Error { get; }
}
