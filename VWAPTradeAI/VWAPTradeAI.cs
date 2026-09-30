using System;
using System.Diagnostics;
using System.Threading;
using cAlgo.API;

namespace cAlgo.Robots;

// Pattern: Composition Root. The only place objects are created and wired together: OnStart reads
// the parameters, validates them and builds the pipelines; the other overrides forward cTrader's
// events to them. No trading rule lives here.
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

    // 信号只推送到 App，由人在 App 里点「Place Trade」后才下单。默认 Local（本机中继），测试时不会推送到
    // 老板的电脑；老板用 Production。Off = 不连接，信号永远无法被确认。地址见 RelayEnvironments。
    [Parameter("中继环境", DefaultValue = RelayEnvironmentModel.Local, Group = "人工确认")]
    public RelayEnvironmentModel RelayEnvironment { get; set; }

    // One person's key from the relay's RELAY_ACCESS_KEYS (rustwebsocket/relay.env.example). The key
    // picks the room: only an app using the same key sees this bot's signals and can approve them.
    [Parameter("访问密钥", DefaultValue = "myaccesscode", Group = "人工确认")]
    public string RelayAccessKey { get; set; }

    [Parameter("启动时清空交易记录CSV", DefaultValue = true, Group = "开发调试")]
    public bool ResetTradeLogOnStart { get; set; }

    [Parameter("debug调试", DefaultValue = false, Group = "开发调试")]
    public bool IsDebug { get; set; }

    [Parameter("输出文件名", DefaultValue = "VWAPTradeAIs.csv", Group = "开发调试")]
    public string FileName { get; set; }

    private VwapSeries _vwapSeries;
    private VwapLines _vwapLines;
    private SignalDetector _signalDetector;
    private SignalMarkers _signalMarkers;
    private ApprovalDesk _approvalDesk;
    private TradeNotificationClient _relay; // null in optimization and with the relay Off

    protected override void OnStart() {
        LaunchDebug();
        string error = StartupCheck.FindError(Bars.TimeFrame.Equals(TimeFrame.Minute5), Bars.TimeFrame.ToString(), OrderLabel) ??
                       StartupCheck.FindRelayError(RelayEnvironment, RelayAccessKey);

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
        OrderExecutor orderExecutor = BuildOrderExecutor(settings);
        BuildApprovalPipeline(orderExecutor);

        // Pattern: Observer. Everything that follows a trade opening or closing, in one place.
        orderExecutor.PositionOpened += tradeLog.RecordEntry;
        orderExecutor.PositionClosed += tradeLog.RecordClose;
        orderExecutor.PositionClosed += position => SendToApp(TradeMessages.Closed(SymbolName, position.NetProfit));

        Print("*****VWAP break and reverse started.");
    }

    protected override void OnBar() {
        // The newly closed bar's VWAP first: drawing and signal detection both read it.
        _vwapSeries.Update();
        _vwapLines.Draw();

        // Never traded here: a signal only becomes an order once the user approves it in the app.
        SignalModel signal = _signalDetector.DetectOnClosedBar();

        if (signal == null)
            return;

        // The chart shows every opportunity, approved or not.
        _signalMarkers.Draw(signal);
        _approvalDesk.OnSignal(signal);
    }

    // A backtest never waits for the user, so an approval is picked up here, on the next tick, and
    // enters at that tick's price. Live, BeginInvokeOnMainThread usually gets there first.
    protected override void OnTick() {
        _approvalDesk.ApplyReceivedDecisions();
    }

    protected override void OnStop() {
        _relay?.Dispose();
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

    // Order gates → sizing → the broker, reached only through its adapter.
    private OrderExecutor BuildOrderExecutor(TradeSettingsModel settings) {
        var planner = new OrderPlanner(new CAlgoSymbolModel(Symbol), settings);
        return new OrderExecutor(new CAlgoBroker(this), planner, OrderLabel.Trim(), Log);
    }

    // A signal is only stored and announced; an execute_trade from the app is what sends it to
    // OrderExecutor. The desk is built before the relay link, so nothing the link receives can find
    // it missing.
    private void BuildApprovalPipeline(OrderExecutor orderExecutor) {
        string relayOffReason = FindRelayOffReason();
        bool isRelayOn = relayOffReason == null;

        // Said out loud, so a run that can never trade does not look like one without signals.
        if (!isRelayOn)
            Print("*****{0}", relayOffReason);

        var approval = new TradeApproval(new PendingTradeSignals(), SymbolName, orderExecutor.TryEnter, SendToApp, Log);
        // Server time, so in a backtest the app shows when the signal happened on the chart.
        _approvalDesk = new ApprovalDesk(approval, () => Server.TimeInUtc);

        if (isRelayOn)
            _relay = ConnectRelay();
    }

    // Null when the relay should be connected.
    private string FindRelayOffReason() {
        // Optimization runs many passes at once; nobody could approve them.
        if (RunningMode == RunningMode.Optimization)
            return "Optimization: no relay, so no signal can be approved and no trade opens.";

        if (RelayEnvironment == RelayEnvironmentModel.Off)
            return "Relay off: signals are logged but can never be approved, so no trade opens.";

        return null;
    }

    private TradeNotificationClient ConnectRelay() {
        RelayEndpointModel endpoint = RelayEnvironments.Endpoint(RelayEnvironment, RelayAccessKey);
        // Always say where signals go, so a test run can never be mistaken for production.
        Print("*****Relay: {0} ({1})", RelayEnvironment, endpoint.Url);

        if (IsBacktesting)
            Print("*****Backtest: it does not pause for signals. An approval enters at the price of that moment.");

        return new TradeNotificationClient(endpoint, OnRelayText, LogFromAnyThread);
    }

    // Runs on the relay link's thread. A decision is applied on the cBot thread, via
    // BeginInvokeOnMainThread or on the next tick, whichever comes first.
    private void OnRelayText(string text) {
        if (_approvalDesk.ReceiveRelayText(text))
            BeginInvokeOnMainThread(_approvalDesk.ApplyReceivedDecisions);
    }

    // Without a relay (Off, optimization) the message goes nowhere.
    private void SendToApp(string message) {
        _relay?.Send(message);
    }

    private void Log(string message) {
        Print("*****{0}", message);
    }

    private void LogFromAnyThread(string message) {
        BeginInvokeOnMainThread(() => Log(message));
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
