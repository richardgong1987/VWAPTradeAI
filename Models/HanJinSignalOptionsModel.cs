namespace cAlgo.Robots;

// Tunables for the fraction-based HanJin patterns. Defaults match the Pine library so a
// caller that passes nothing reproduces the original behaviour exactly.
public class HanJinSignalOptionsModel {
    // Pinbar: minimum dominant-wick fraction, and (when Strict) the maximum opposite wick.
    public double PinbarLongFraction { get; set; } = 0.667;

    public double PinbarShortFraction { get; set; } = 0.2;

    public bool PinbarStrict { get; set; } = true;
}
