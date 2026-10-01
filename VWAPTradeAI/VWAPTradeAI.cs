using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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

    // 开启后，信号要先由本机的 AI 服务（TrendAssessmentModel）看图确认趋势，通过了才会下单。只支持实盘和
    // 模拟盘；关闭时（默认）与 AI 毫无关系，信号照旧立即下单。
    [Parameter("启用AI趋势过滤", DefaultValue = false, Group = "AI趋势判断")]
    public bool IsAiTrendFilterEnabled { get; set; }

    [Parameter("AI服务地址", DefaultValue = "http://127.0.0.1:8787", Group = "AI趋势判断")]
    public string AiServiceUrl { get; set; }

    [Parameter("AI超时秒数", DefaultValue = 30, MinValue = 1, MaxValue = StartupCheck.MaxAiTimeoutSeconds, Group = "AI趋势判断")]
    public int AiTimeoutSeconds { get; set; }

    // 把发给 AI 的截图和它的判断存到 Documents/TrendAssessment，用于事后评估模型。
    [Parameter("保存AI评估截图", DefaultValue = false, Group = "AI趋势判断")]
    public bool IsAiAssessmentRecorded { get; set; }

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
    private TrendAssessmentClient _trendAssessmentClient; // null with the AI trend filter off
    private AiTrendFilter _aiTrendFilter; // null with the AI trend filter off
    private AiChartshots _aiChartshots; // null with the AI trend filter off

    protected override void OnStart() {
        LaunchDebug();
        bool hasChart = RunningMode == RunningMode.RealTime || RunningMode == RunningMode.VisualBacktesting;
        string error = StartupCheck.FindError(Bars.TimeFrame.Equals(TimeFrame.Minute5), Bars.TimeFrame.ToString(), OrderLabel) ??
                       StartupCheck.FindAiFilterError(IsAiTrendFilterEnabled, hasChart, AiServiceUrl, AiTimeoutSeconds);

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

        if (IsAiTrendFilterEnabled) {
            _aiTrendFilter = BuildAiTrendFilter();
            _aiChartshots = BuildAiChartshots();
        }

        Print("*****VWAP break and reverse started.");
    }

    protected override void OnBar() {
        // The newly closed bar's VWAP first: drawing and signal detection both read it.
        _vwapSeries.Update();
        _vwapLines.Draw();
        // A signal from the last bar whose picture never came is settled before a new one.
        _aiChartshots?.OnNewBar();

        SignalModel signal = _signalDetector.DetectOnClosedBar();

        if (signal == null)
            return;

        if (_aiChartshots != null) {
            // The marker is drawn once the AI's picture is taken (AssessPicturedSignal), so the
            // model never sees the strategy's own BUY/SELL mark.
            _aiChartshots.OnSignal(signal);
            return;
        }

        // The chart shows every signal, whether or not its order goes out.
        _signalMarkers.Draw(signal);
        // The executor logs the gate that stopped an order, so the result needs nothing more here.
        _orderExecutor.TryEnter(signal, out _);
    }

    // Only the AI filter uses ticks: a signal whose bar the chart has not drawn yet waits here.
    protected override void OnTick() {
        _aiChartshots?.OnTick();
    }

    // Called once per signal by AiChartshots, with the picture, or null when none could be taken.
    private void AssessPicturedSignal(SignalModel signal, byte[] unmarkedChartPng) {
        // The chart shows every signal, whether or not its order goes out.
        _signalMarkers.Draw(signal);

        if (IsBacktesting)
            // Pauses the backtest until the model has answered; the signal is decided on this bar.
            _aiTrendFilter.AssessAndWait(signal, unmarkedChartPng);
        else
            // Returns at once. A signal the AI passes reaches OrderExecutor later, on this thread.
            _aiTrendFilter.Submit(signal, unmarkedChartPng);
    }

    protected override void OnStop() {
        _trendAssessmentClient?.Dispose();
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

    // Signal → local AI service → TrendDirectionGate → OrderExecutor. Only built with the filter on.
    private AiTrendFilter BuildAiTrendFilter() {
        _trendAssessmentClient = new TrendAssessmentClient(new Uri(AiServiceUrl.Trim()), TimeSpan.FromSeconds(AiTimeoutSeconds));
        Print("*****AI trend filter on | Service: {0}, TimeoutSeconds: {1}, Recording: {2}", AiServiceUrl.Trim(), AiTimeoutSeconds,
            IsAiAssessmentRecorded);

        // Said out loud, so a backtest that stands still at a signal is not mistaken for a hang.
        if (IsBacktesting)
            Print("*****Visual backtest: the backtest pauses at every signal until the AI has answered, several seconds each.");

        // Not awaited: loading the model takes seconds, and the outcome only goes to the log.
        _ = WarmUpAiServiceAsync(_trendAssessmentClient);

        // OnBar fires as a new bar opens, so the last closed bar is Count - 2 (as in SignalDetector).
        return new AiTrendFilter(_trendAssessmentClient.AssessAsync, BeginInvokeOnMainThread, () => Bars.Count - 2, _orderExecutor.TryEnter,
            BuildTrendAssessmentRecorder(), Log);
    }

    // The AI's picture waits, across the ticks of the bar, until the chart shows the signal's bar.
    // Only a backtest also pauses on each of those ticks, to give its lagging chart real time.
    private AiChartshots BuildAiChartshots() {
        return new AiChartshots(new CAlgoChartCamera(Chart), IsBacktesting, Thread.Sleep, () => Server.Time, AssessPicturedSignal, Log);
    }

    // Null unless the AI's pictures and answers are being kept for evaluation.
    private TrendAssessmentRecorder BuildTrendAssessmentRecorder() {
        if (!IsAiAssessmentRecorded)
            return null;

        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var recorder = new TrendAssessmentRecorder(TrendAssessmentRecorder.DirectoryIn(documentsPath), SymbolName);
        Print("****AI assessment folder: {0}", recorder.DirectoryPath);
        return recorder;
    }

    // Asks the service to load the model now, so the first signal does not pay for it. The wait is
    // off the cBot thread; only the log line comes back to it.
    private async Task WarmUpAiServiceAsync(TrendAssessmentClient client) {
        string failure = await client.WarmUpAsync().ConfigureAwait(false);
        BeginInvokeOnMainThread(() => Log(failure == null
            ? "AI service ready | The model is loaded"
            : $"AI service warm-up failed | Reason: {failure} | Signals are rejected until the service answers"));
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
