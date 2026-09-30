namespace cAlgo.Robots;

// The side a candle pattern points to. Replaces the Pine library's "BUY"/"SELL"/na strings
// so callers switch on a value instead of comparing magic text.
public enum SignalSideModel {
    None,
    Buy,
    Sell
}
