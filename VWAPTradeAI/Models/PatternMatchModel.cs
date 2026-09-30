namespace cAlgo.Robots;

// A candle pattern that touched the key level: its name (e.g. S_Pin_1, shown on the chart and in the
// trade CSV) and the stop that pattern implies (see LevelPatternMatcher).
public class PatternMatchModel {
    public PatternMatchModel(string label, double stopLoss) {
        Label = label;
        StopLoss = stopLoss;
    }

    public string Label { get; }

    public double StopLoss { get; }
}
