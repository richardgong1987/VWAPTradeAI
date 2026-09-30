namespace cAlgo.Robots;

// The 7 HanJin pattern verdicts for one candle window. Each field is the side that pattern
// points to, or None when it did not fire.
public class HanJinSignalScanModel {
    public SignalSideModel Pinbar { get; set; }

    public SignalSideModel Engulf { get; set; }

    public SignalSideModel FractalTop { get; set; }

    public SignalSideModel FractalBottom { get; set; }

    public SignalSideModel HaramiSingle { get; set; }
}
