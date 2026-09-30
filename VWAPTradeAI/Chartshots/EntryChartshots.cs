using System;
using System.IO;
using cAlgo.API;

namespace cAlgo.Robots;

// A screenshot of the chart for every order that went out, saved under the next number (see
// ChartshotFolder). It follows OrderExecutor.PositionOpened.
//
// A screenshot is a record, never a reason to stop trading: when one cannot be taken or saved,
// that is logged and the cBot carries on.
public class EntryChartshots {
    private readonly Chart _chart;
    private readonly ChartshotFolder _folder;
    private readonly Action<string> _log;

    public EntryChartshots(Chart chart, ChartshotFolder folder, Action<string> log) {
        _chart = chart;
        _folder = folder;
        _log = log;
    }

    public void Take() {
        // cTrader gives null when the chart is not visible: a non-visual backtest, optimization, or
        // a chart that is not on screen.
        byte[] png = _chart.TakeChartshot();

        if (png == null) {
            _log("Chartshot skipped | The chart is not visible");
            return;
        }

        try {
            _log($"Chartshot saved | {_folder.Save(png)}");
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            _log($"Chartshot not saved | {error.Message}");
        }
    }
}
