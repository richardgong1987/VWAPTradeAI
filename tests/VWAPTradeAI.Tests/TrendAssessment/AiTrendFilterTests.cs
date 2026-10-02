using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.TrendAssessment {
    // The flow around the AI gate. What matters: a signal reaches OrderExecutor only after the model
    // passed it, only on the cBot thread, only while it is still the last closed bar, and any
    // failure ends as a logged rejection.
    public class AiTrendFilterTests : IDisposable {
        private static readonly byte[] ChartPng = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        private readonly SignalModel _signal = TestSignal.Long(close: 100.0, stopLoss: 98.0); // on bar 42
        private readonly List<(string RequestId, byte[] Png)> _requests = new();
        // Stands in for BeginInvokeOnMainThread. Filled from whichever thread the answer arrives on.
        private readonly ConcurrentQueue<Action> _mainThread = new();
        private readonly List<SignalModel> _entered = new();
        private readonly List<string> _log = new();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "AiTrendFilterTests-" + Guid.NewGuid());

        private Func<Task<TrendAssessmentResultModel>> _answer = () => Task.FromResult(TestAssessment.Of(TrendModel.Up, DailyVwapDirectionModel.Rising));
        private int _lastClosedBarIndex = 42;
        private string _orderRejectReason = "";

        public void Dispose() {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private AiTrendFilter Filter(TrendAssessmentRecorder recorder = null) {
            return new AiTrendFilter(Assess, _mainThread.Enqueue, () => _lastClosedBarIndex, TryEnter, recorder, _log.Add);
        }

        private Task<TrendAssessmentResultModel> Assess(string requestId, byte[] png) {
            _requests.Add((requestId, png));
            return _answer();
        }

        private bool TryEnter(SignalModel signal, out string rejectReason) {
            _entered.Add(signal);
            rejectReason = _orderRejectReason;
            return _orderRejectReason == "";
        }

        // What cTrader does when BeginInvokeOnMainThread gets round to the queued work.
        private void RunMainThread() {
            while (_mainThread.TryDequeue(out Action work))
                work();
        }

        private void Answer(TrendAssessmentResultModel assessment) => _answer = () => Task.FromResult(assessment);

        [Fact]
        public void a_passed_signal_is_entered_with_the_original_signal() {
            Filter().Submit(_signal, ChartPng);
            RunMainThread();

            Assert.Same(_signal, Assert.Single(_entered));
            Assert.Contains(_log, line => line.StartsWith("AI trend accepted | Signal: Buy | Trend: UP | TrendConfidence: 0.91 | " +
                                                          "DailyVWAP: RISING | DailyVWAPConfidence: 0.88 | ElapsedMs: "));
        }

        [Fact]
        public void the_picture_is_sent_exactly_as_it_was_taken() {
            Filter().Submit(_signal, ChartPng);

            (string requestId, byte[] png) = Assert.Single(_requests);
            Assert.Same(ChartPng, png);
            Assert.Equal(32, requestId.Length);
        }

        [Fact]
        public void submit_returns_before_the_answer_and_nothing_is_entered_meanwhile() {
            var pending = new TaskCompletionSource<TrendAssessmentResultModel>();
            _answer = () => pending.Task;

            Filter().Submit(_signal, ChartPng);

            // The model is still thinking: OnBar has already returned, and no order went out.
            Assert.Empty(_mainThread);
            Assert.Empty(_entered);

            pending.SetResult(TestAssessment.Of(TrendModel.Up, DailyVwapDirectionModel.Rising));
            // The answer comes back on a pool thread; wait for it to be handed over.
            Assert.True(SpinWait.SpinUntil(() => !_mainThread.IsEmpty, TimeSpan.FromSeconds(5)));
            RunMainThread();

            Assert.Single(_entered);
        }

        [Fact]
        public void the_order_is_only_ever_entered_from_the_main_thread_hand_off() {
            Filter().Submit(_signal, ChartPng);

            // The answer is back, but until cTrader runs the queued work nothing touches the broker.
            Assert.Single(_mainThread);
            Assert.Empty(_entered);
        }

        [Fact]
        public void a_rejected_signal_is_not_entered_and_the_log_says_why() {
            Answer(TestAssessment.Of(TrendModel.Up, DailyVwapDirectionModel.Flat));

            Filter().Submit(_signal, ChartPng);
            RunMainThread();

            Assert.Empty(_entered);
            Assert.Contains(_log, line => line.StartsWith("AI trend rejected | Signal: Buy | Trend: UP | TrendConfidence: 0.91 | DailyVWAP: FLAT | " +
                                                          "DailyVWAPConfidence: 0.88 | Rejected: Daily VWAP is FLAT | Reason: "));
        }

        [Fact]
        public void an_unreadable_chart_is_not_entered() {
            Answer(TestAssessment.Unreadable());

            Filter().Submit(_signal, ChartPng);
            RunMainThread();

            Assert.Empty(_entered);
            Assert.Contains(_log, line => line.StartsWith("AI trend unreadable | Signal: Buy | Reason: No candlesticks are visible.") &&
                                          line.EndsWith("Trade rejected"));
        }

        [Fact]
        public void an_unavailable_assessment_is_not_entered() {
            Answer(TestAssessment.Unavailable());

            Filter().Submit(_signal, ChartPng);
            RunMainThread();

            Assert.Empty(_entered);
            Assert.Contains(_log, line => line.StartsWith("AI trend unavailable | Signal: Buy | " +
                                                          "Reason: Local assessment request timed out after 30 seconds") &&
                                          line.EndsWith("Trade rejected"));
        }

        [Fact]
        public void a_request_that_throws_is_a_rejection_not_a_lost_signal() {
            _answer = () => throw new InvalidOperationException("boom");

            Filter().Submit(_signal, ChartPng);
            RunMainThread();

            Assert.Empty(_entered);
            Assert.Contains(_log, line => line.Contains("AI trend unavailable") && line.Contains("boom"));
        }

        [Fact]
        public void without_a_picture_nothing_is_asked_and_nothing_is_entered() {
            // AiChartshots hands over null when the chart is hidden or never showed the signal's bar.
            Filter().Submit(_signal, chartPng: null);
            RunMainThread();

            Assert.Empty(_requests);
            Assert.Empty(_entered);
            Assert.Equal("AI trend unavailable | Signal: Buy | Reason: No current picture of the chart to assess | Trade rejected",
                Assert.Single(_log));
        }

        [Fact]
        public void a_passed_signal_is_not_entered_once_a_newer_bar_has_closed() {
            Filter().Submit(_signal, ChartPng);
            _lastClosedBarIndex = 43; // the answer took longer than the bar

            RunMainThread();

            Assert.Empty(_entered);
            Assert.Contains(_log, line => line.Contains("Rejected: Signal expired: a newer bar closed before the answer arrived"));
        }

        [Fact]
        public void nothing_is_written_to_disk_unless_a_recorder_is_given() {
            Filter().Submit(_signal, ChartPng);
            RunMainThread();

            Assert.False(Directory.Exists(_directory));
        }

        [Fact]
        public void with_a_recorder_the_picture_and_the_outcome_are_kept() {
            _orderRejectReason = "Level VWAP already has an open position on XAUUSD";

            Filter(new TrendAssessmentRecorder(resetOnStart: false, _directory, "XAUUSD")).Submit(_signal, ChartPng);
            RunMainThread();

            string picturePath = Assert.Single(Directory.GetFiles(_directory, "*.png"));
            Assert.Equal(ChartPng, File.ReadAllBytes(picturePath));
            string json = File.ReadAllText(Path.ChangeExtension(picturePath, ".json"));
            Assert.Contains("\"gate\": \"PASS\"", json);
            Assert.Contains("\"order_placed\": false", json);
            Assert.Contains("\"order_reject_reason\": \"Level VWAP already has an open position on XAUUSD\"", json);
        }

        [Fact]
        public void a_rejected_assessment_is_recorded_too() {
            Answer(TestAssessment.Unavailable());

            Filter(new TrendAssessmentRecorder(resetOnStart: false, _directory, "XAUUSD")).Submit(_signal, ChartPng);
            RunMainThread();

            string json = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "*.json")));
            Assert.Contains("\"outcome\": \"UNAVAILABLE\"", json);
            Assert.Contains("\"gate\": \"REJECT\"", json);
        }

        // In a backtest the decision must be made before OnBar returns, or the backtest's clock runs
        // on past the bar the signal belongs to.

        [Fact]
        public void in_a_backtest_a_passed_signal_is_entered_before_assess_and_wait_returns() {
            var pending = new TaskCompletionSource<TrendAssessmentResultModel>();
            _answer = () => pending.Task;
            // The model answers a moment later, on another thread, as the real service does.
            _ = Task.Run(async () => {
                await Task.Delay(50);
                pending.SetResult(TestAssessment.Of(TrendModel.Up, DailyVwapDirectionModel.Rising));
            });

            Filter().AssessAndWait(_signal, ChartPng);

            Assert.Same(_signal, Assert.Single(_entered));
            Assert.Empty(_mainThread); // nothing was left for later
            Assert.Contains(_log, line => line.StartsWith("AI trend accepted | Signal: Buy | Trend: UP"));
        }

        [Fact]
        public void in_a_backtest_a_rejected_signal_is_decided_before_assess_and_wait_returns() {
            Answer(TestAssessment.Of(TrendModel.Sideways, DailyVwapDirectionModel.Rising));

            Filter().AssessAndWait(_signal, ChartPng);

            Assert.Empty(_entered);
            Assert.Contains(_log, line => line.Contains("Rejected: Trend is SIDEWAYS"));
        }

        [Fact]
        public void in_a_backtest_a_failed_request_is_a_rejection_and_does_not_escape() {
            _answer = () => throw new InvalidOperationException("boom");

            Filter().AssessAndWait(_signal, ChartPng);

            Assert.Empty(_entered);
            Assert.Contains(_log, line => line.Contains("AI trend unavailable") && line.Contains("boom"));
        }

        [Fact]
        public void in_a_backtest_without_a_picture_nothing_is_asked() {
            Filter().AssessAndWait(_signal, chartPng: null);

            Assert.Empty(_requests);
            Assert.Empty(_entered);
        }

        [Fact]
        public void in_a_backtest_the_assessment_is_recorded_like_any_other() {
            Filter(new TrendAssessmentRecorder(resetOnStart: false, _directory, "XAUUSD")).AssessAndWait(_signal, ChartPng);

            string picturePath = Assert.Single(Directory.GetFiles(_directory, "*.png"));
            Assert.Equal(ChartPng, File.ReadAllBytes(picturePath));
            Assert.Contains("\"order_placed\": true", File.ReadAllText(Path.ChangeExtension(picturePath, ".json")));
        }
    }
}
