using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace cAlgo.Robots;

// OrderExecutor.TryEnter's shape, so the flow can be tested without a broker.
public delegate bool TryEnterOrder(SignalModel signal, out string rejectReason);

// The optional AI gate between a detected signal and OrderExecutor. The model needs several
// seconds, so nothing here waits on the cBot thread:
//
//   Submit     cBot thread   send the chart picture for assessment, return at once
//   (wait)     thread pool   the HTTP request to the local AI service
//   Complete   cBot thread   signal still current? → TrendDirectionGate → TryEnter
//
// Fail closed: no picture, no valid answer or a signal that has gone stale all mean no trade, and
// the log says why. A passed signal goes to OrderExecutor.TryEnter unchanged, which reads the
// Ask/Bid of that moment and applies every order gate as it always does; no price is kept from
// before the wait.
//
// No cAlgo dependency: the thread hand-off, the bar count and the order entry come in as
// functions, so it is unit tested.
public class AiTrendFilter {
    private readonly Func<string, byte[], Task<TrendAssessmentResultModel>> _assess;
    private readonly Action<Action> _runOnMainThread;
    private readonly Func<int> _lastClosedBarIndex;
    private readonly TryEnterOrder _tryEnter;
    private readonly TrendAssessmentRecorder _recorder;
    private readonly Action<string> _log;

    // recorder: null unless the assessments are being kept for evaluation.
    public AiTrendFilter(Func<string, byte[], Task<TrendAssessmentResultModel>> assess, Action<Action> runOnMainThread,
        Func<int> lastClosedBarIndex, TryEnterOrder tryEnter, TrendAssessmentRecorder recorder, Action<string> log) {
        _assess = assess;
        _runOnMainThread = runOnMainThread;
        _lastClosedBarIndex = lastClosedBarIndex;
        _tryEnter = tryEnter;
        _recorder = recorder;
        _log = log;
    }

    // chartPng: the chart as the model should see it, taken before the signal's own marker was
    // drawn. Null when cTrader could not take it (the chart is not visible).
    public void Submit(SignalModel signal, byte[] chartPng) {
        if (chartPng == null) {
            _log($"AI trend unavailable | Signal: {signal.Level.Side} | Reason: The chart is not visible, so there is no picture to assess | " +
                 "Trade rejected");
            return;
        }

        string requestId = Guid.NewGuid().ToString("N");
        _log($"AI trend requested | Signal: {signal.Level.Side} {signal.Label} | RequestId: {requestId}");
        _ = AssessThenCompleteAsync(signal, chartPng, requestId);
    }

    private async Task AssessThenCompleteAsync(SignalModel signal, byte[] chartPng, string requestId) {
        var stopwatch = Stopwatch.StartNew();
        TrendAssessmentResultModel assessment;

        // Whatever goes wrong while asking, the signal must end as a logged rejection, never as a
        // lost task or a trade.
        try {
            assessment = await _assess(requestId, chartPng).ConfigureAwait(false);
        } catch (Exception error) {
            assessment = TrendAssessmentResultModel.Unavailable($"The assessment request failed: {error.Message}");
        }

        long elapsedMs = stopwatch.ElapsedMilliseconds;
        _runOnMainThread(() => Complete(signal, chartPng, requestId, assessment, elapsedMs));
    }

    private void Complete(SignalModel signal, byte[] chartPng, string requestId, TrendAssessmentResultModel assessment, long elapsedMs) {
        string gateRejectReason = FindRejectReason(signal, assessment);
        bool isOrderPlaced = false;
        string orderRejectReason = null;

        _log(Describe(signal, assessment, gateRejectReason, elapsedMs));

        if (gateRejectReason == null)
            isOrderPlaced = _tryEnter(signal, out orderRejectReason);

        Record(chartPng, new TrendAssessmentRecordModel {
            RequestId = requestId,
            Signal = signal,
            Assessment = assessment,
            GateRejectReason = gateRejectReason,
            IsOrderPlaced = isOrderPlaced,
            OrderRejectReason = string.IsNullOrEmpty(orderRejectReason) ? null : orderRejectReason,
            ElapsedMs = elapsedMs
        });
    }

    // Null when the signal may go on to OrderExecutor.
    private string FindRejectReason(SignalModel signal, TrendAssessmentResultModel assessment) {
        // A signal is only ever traded during the bar right after its own. The model answers well
        // inside that bar; an answer that arrives later belongs to a chart that has moved on.
        if (signal.BarIndex != _lastClosedBarIndex())
            return "Signal expired: a newer bar closed before the answer arrived";

        return TrendDirectionGate.FindRejectReason(signal.Level.Side, assessment);
    }

    private static string Describe(SignalModel signal, TrendAssessmentResultModel assessment, string gateRejectReason, long elapsedMs) {
        string side = $"Signal: {signal.Level.Side}";

        if (assessment.Outcome != TrendAssessmentOutcomeModel.Assessed) {
            string what = assessment.Outcome == TrendAssessmentOutcomeModel.Unreadable ? "unreadable" : "unavailable";
            return $"AI trend {what} | {side} | Reason: {assessment.Reason} | ElapsedMs: {elapsedMs} | Trade rejected";
        }

        string judgement = $"Trend: {Upper(assessment.Trend)} | TrendConfidence: {Number(assessment.TrendConfidence)} | " +
                           $"DailyVWAP: {Upper(assessment.DailyVwapDirection)} | DailyVWAPConfidence: {Number(assessment.DailyVwapConfidence)}";

        if (gateRejectReason == null)
            return $"AI trend accepted | {side} | {judgement} | ElapsedMs: {elapsedMs}";

        return $"AI trend rejected | {side} | {judgement} | Rejected: {gateRejectReason} | Reason: {assessment.Reason} | ElapsedMs: {elapsedMs}";
    }

    // A record is for later review, never a reason to disturb trading.
    private void Record(byte[] chartPng, TrendAssessmentRecordModel record) {
        if (_recorder == null)
            return;

        try {
            _log($"AI assessment recorded | {_recorder.Save(record, chartPng)}");
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            _log($"AI assessment not recorded | {error.Message}");
        }
    }

    private static string Upper<T>(T? value) where T : struct, Enum {
        return value.ToString().ToUpperInvariant();
    }

    private static string Number(double? value) {
        return value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
    }
}
