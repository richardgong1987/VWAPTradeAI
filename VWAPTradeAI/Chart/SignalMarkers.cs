using cAlgo.API;

namespace cAlgo.Robots;

// The marker for a detected signal, drawn when it is announced, whether or not it is ever traded:
// a triangle plus the signal name, below a long's low and above a short's high.
public class SignalMarkers {
    private const string Prefix = "VWAP_SIGNAL_";

    private const int IconOffsetTicks = 120;
    private const int TextOffsetTicks = 320;
    private const int TextFontSize = 8;

    private readonly Chart _chart;
    private readonly double _iconOffset;
    private readonly double _textOffset;

    public SignalMarkers(Chart chart, double tickSize) {
        _chart = chart;
        _iconOffset = tickSize * IconOffsetTicks;
        _textOffset = tickSize * TextOffsetTicks;
    }

    public void Draw(SignalModel signalModel) {
        bool isLong = signalModel.Level.Side == SignalSideModel.Buy;
        string side = isLong ? "LONG" : "SHORT";
        string key = $"{signalModel.BarTime:yyyyMMdd_HHmmss}_{signalModel.Level.Name}";
        Color color = isLong ? Color.Lime : Color.Red;

        // Markers step away from the bar: down from a long's low, up from a short's high.
        double anchor = isLong ? signalModel.Low : signalModel.High;
        double away = isLong ? -1.0 : 1.0;

        _chart.DrawIcon($"{Prefix}{side}_ICON_{key}", isLong ? ChartIconType.UpTriangle : ChartIconType.DownTriangle, signalModel.BarIndex,
            anchor + away * _iconOffset, color);

        ChartText text = _chart.DrawText($"{Prefix}{side}_TEXT_{key}", signalModel.Label, signalModel.BarIndex, anchor + away * _textOffset,
            color);
        text.FontSize = TextFontSize;
        text.IsBold = false;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
    }
}
