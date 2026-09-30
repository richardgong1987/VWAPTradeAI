using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.Approval {
    // The Robot's side of manual approval. Nothing waits for the user, in a backtest either, and
    // nothing expires: a decision arriving on the relay's thread is queued and applied later on the
    // cBot thread.
    public class ApprovalDeskTests {
        private static readonly DateTime DetectedAt = new(2026, 9, 28, 0, 45, 0, DateTimeKind.Utc);

        private readonly List<string> _sent = new();
        private readonly List<SignalModel> _orderAttempts = new();

        private ApprovalDesk Desk() {
            var approval = new TradeApproval(new PendingTradeSignals(), "XAUUSD", FakeTryEnter, _sent.Add, _ => { });
            return new ApprovalDesk(approval, () => DetectedAt);
        }

        private bool FakeTryEnter(SignalModel signal, out string rejectReason) {
            _orderAttempts.Add(signal);
            rejectReason = "";
            return true;
        }

        private static SignalModel Signal() => TestSignal.Long(close: 100.0, stopLoss: 98.0);

        private static string Approval(string signalId) => $$"""{"type":"execute_trade","signal_id":"{{signalId}}"}""";

        private JsonElement LastSent() => JsonDocument.Parse(_sent.Last()).RootElement;

        [Fact]
        public void a_signal_is_announced_and_returns_at_once_without_waiting_for_a_decision() {
            ApprovalDesk desk = Desk();

            desk.OnSignal(Signal());

            Assert.Equal("trade_opportunity", LastSent().GetProperty("type").GetString());
            Assert.Empty(_orderAttempts);
        }

        [Fact]
        public void a_decision_is_only_applied_when_the_cbot_thread_picks_it_up() {
            ApprovalDesk desk = Desk();
            SignalModel signal = Signal();
            desk.OnSignal(signal);
            string signalId = LastSent().GetProperty("signal_id").GetString();

            bool isQueued = desk.ReceiveRelayText(Approval(signalId));

            Assert.True(isQueued);
            Assert.Empty(_orderAttempts);

            desk.ApplyReceivedDecisions();

            Assert.Same(signal, Assert.Single(_orderAttempts));
            Assert.Equal("trade_opened", LastSent().GetProperty("type").GetString());
        }

        [Fact]
        public void a_later_signal_does_not_stop_an_earlier_one_being_approved() {
            // A backtest carries on past a signal, so the next one may come before the first is decided.
            ApprovalDesk desk = Desk();
            SignalModel first = Signal();
            desk.OnSignal(first);
            string firstId = LastSent().GetProperty("signal_id").GetString();
            desk.OnSignal(Signal());

            desk.ReceiveRelayText(Approval(firstId));
            desk.ApplyReceivedDecisions();

            Assert.Same(first, Assert.Single(_orderAttempts));
        }

        [Fact]
        public void the_signal_time_the_app_shows_comes_from_the_desks_clock() {
            Desk().OnSignal(Signal());

            Assert.Equal("2026-09-28T00:45:00Z", LastSent().GetProperty("timestamp").GetString());
        }

        [Theory]
        [InlineData("""{"type":"ack","source":"tauri"}""")]
        [InlineData("{not json")]
        public void relay_text_that_is_not_a_decision_is_not_queued(string text) {
            Assert.False(Desk().ReceiveRelayText(text));
        }
    }
}
