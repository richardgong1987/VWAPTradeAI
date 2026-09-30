using System;
using System.Diagnostics;
using System.Threading;
using cAlgo.API;

namespace cAlgo.Robots;

[Robot(TimeZone = TimeZones.TokyoStandardTime, AccessRights = AccessRights.FullAccess, AddIndicators = false)]
public class VWAPTradeAI : Robot
{
    [Parameter("订单标签", DefaultValue = "VWAPTradeAI-label")]
    public string OrderLabel { get; set; }

    [Parameter("风险%", DefaultValue = 1, MinValue = 0)]
    public double RiskPct { get; set; }

    [Parameter("止盈目标", DefaultValue = 2.0, MinValue = 0.5, MaxValue = 20.0, Step = 0.1)]
    public double TakeProfitR { get; set; }

    [Parameter("止损偏移点数", DefaultValue = 50, MinValue = 0, MaxValue = 2000, Group = "风控配置")]
    public int StopOffsetTicks { get; set; }

    [Parameter("启动时清空交易记录CSV和截图", DefaultValue = true, Group = "开发调试")]
    public bool ResetTradeLogOnStart { get; set; }

    [Parameter("debug调试", DefaultValue = false, Group = "开发调试")]
    public bool IsDebug { get; set; }

    [Parameter("输出文件名", DefaultValue = "VWAPTradeAIs.csv", Group = "开发调试")]
    public string FileName { get; set; }

    private VwapSeries _vwapSeries;
    private VwapLines _vwapLines;
    private SignalDetector _signalDetector;
    private SignalMarkers _signalMarkers;
    private OrderExecutor _orderExecutor;

    protected override void OnStart() {
        LaunchDebug();
        string error = StartupCheck.FindError(Bars.TimeFrame.Equals(TimeFrame.Minute5), Bars.TimeFrame.ToString(), OrderLabel);

        if (error != null) {
            Print("*****参数有误，已停止：{0}", error);
            Stop();
            return;
        }

        var settings = new TradeSettingsModel(RiskPct, TakeProfitR, StopOffsetTicks);
        PrintSettings(settings);

        BuildSignalPipeline();
        BuildChartDrawing();
        TradeCsvLogger tradeLog = BuildTradeLog();
        EntryChartshots entryChartshots = BuildEntryChartshots();
        _orderExecutor = BuildOrderExecutor(settings);

        // Pattern: Observer. Everything that follows a trade opening or closing, in one place.
        _orderExecutor.PositionOpened += tradeLog.RecordEntry;
        _orderExecutor.PositionOpened += (_, _) => entryChartshots.Take();
        _orderExecutor.PositionClosed += tradeLog.RecordClose;

        Print("*****VWAP break and reverse started.");
    }

    protected override void OnBar() {
        // The newly closed bar's VWAP first: drawing and signal detection both read it.
        _vwapSeries.Update();
        _vwapLines.Draw();

        SignalModel signal = _signalDetector.DetectOnClosedBar();

        if (signal == null)
            return;

        // The chart shows every signal, whether or not its order goes out.
        _signalMarkers.Draw(signal);
        // The executor logs the gate that stopped an order, so the result needs nothing more here.
        _orderExecutor.TryEnter(signal, out _);
    }

    protected override void OnStop() {
        Print("*****cBot stopped.*******************");
    }

    // VWAP series → signal detection.
    private void BuildSignalPipeline() {
        _vwapSeries = new VwapSeries(Bars);
        _vwapSeries.Update();
        _signalDetector = new SignalDetector(Bars, _vwapSeries);
    }

    // The three VWAP lines and the signal markers; needs the VWAP series.
    private void BuildChartDrawing() {
        _signalMarkers = new SignalMarkers(Chart, Symbol.TickSize);
        _vwapLines = new VwapLines(Chart, _vwapSeries);
        _vwapLines.Draw();
        // No lines? ClosedBars 0 means no history yet; ChartObjects 0 means this timeframe is not drawn (daily and up).
        Print("*****VWAP lines | ClosedBars: {0}, ChartObjects: {1}, TimeFrame: {2}", _vwapSeries.Count, _vwapLines.DrawnObjectCount,
            Bars.TimeFrame);
    }

    private TradeCsvLogger BuildTradeLog() {
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        bool isLiveAccount = !IsBacktesting && Account.IsLive;
        var csvFile = new TradeCsvFile(ResetTradeLogOnStart, TradeCsvFile.ReportsDirectory(documentsPath, IsBacktesting, isLiveAccount),
            FileName);
        Print("****CSV logger path: {0}", csvFile.FilePath);

        if (csvFile.ArchivedFilePath != null)
            Print("****Trade CSV had different columns; the old file was moved to {0}", csvFile.ArchivedFilePath);

        return new TradeCsvLogger(csvFile, SymbolName, Bars.TimeFrame.ToString(), Log);
    }

    // A numbered screenshot of the chart for every order that goes out.
    private EntryChartshots BuildEntryChartshots() {
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = new ChartshotFolder(ResetTradeLogOnStart, ChartshotFolder.DirectoryIn(documentsPath));
        Print("****Chartshot folder: {0}", folder.DirectoryPath);
        return new EntryChartshots(Chart, folder, Log);
    }

    // Order gates → sizing → the broker, reached only through its adapter.
    private OrderExecutor BuildOrderExecutor(TradeSettingsModel settings) {
        var planner = new OrderPlanner(new CAlgoSymbolModel(Symbol), settings);
        return new OrderExecutor(new CAlgoBroker(this), planner, OrderLabel.Trim(), Log);
    }

    private void Log(string message) {
        Print("*****{0}", message);
    }

    // 风险% 留 0 就等于这个 cBot 不会下任何单，启动时说清楚，免得以为是信号没出。
    private void PrintSettings(TradeSettingsModel settings) {
        Print("*****Trade settings | RiskPct: {0}, TakeProfitR: {1}, StopOffsetTicks: {2}", settings.RiskPct, settings.TakeProfitR,
            settings.StopOffsetTicks);

        if (settings.RiskPct <= 0.0)
            Print("*****Risk % is 0, so this cBot will never trade. Set it above 0 to enable orders.");
    }

    private void LaunchDebug() {
        if (!IsDebug)
            return;

        // Debugger.Launch() is the Windows route; on macOS, wait for Rider to attach instead.
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!Debugger.IsAttached && DateTime.UtcNow < deadline)
            Thread.Sleep(200);
    }
}
