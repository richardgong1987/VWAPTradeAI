namespace cAlgo.Robots;

// The key level a pattern must touch: the daily VWAP on the signal bar (see SignalDetector), so
// its price changes bar by bar. Side is the trade side the Strong stack allows at that bar.
// Name goes into the order label and the trade CSV's 关键位 column.
public class TradeLevelModel {
    public TradeLevelModel(string name, SignalSideModel side, double price) {
        Name = name;
        Side = side;
        Price = price;
    }

    public string Name { get; }

    public SignalSideModel Side { get; }

    public double Price { get; }
}
