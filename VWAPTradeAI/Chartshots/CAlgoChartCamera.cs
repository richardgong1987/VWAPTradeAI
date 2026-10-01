using cAlgo.API;

namespace cAlgo.Robots;

// Pattern: Adapter. Passes IChartCamera straight through to cTrader's Chart; it decides nothing.
public class CAlgoChartCamera : IChartCamera {
    private readonly Chart _chart;

    public CAlgoChartCamera(Chart chart) {
        _chart = chart;
    }

    public bool IsVisible => _chart.IsVisible;

    public int FirstVisibleBarIndex => _chart.FirstVisibleBarIndex;

    public int LastVisibleBarIndex => _chart.LastVisibleBarIndex;

    public void ScrollXTo(int barIndex) {
        _chart.ScrollXTo(barIndex);
    }

    public byte[] TakeChartshot() {
        return _chart.TakeChartshot();
    }
}
