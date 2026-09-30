using System;
using cAlgo.Robots;
using Xunit;

namespace DayTradeSelf.Tests.Approval {
    // Each detected signal gets its own ID, and a decision for that ID is honoured at most once,
    // however long after detection it comes.
    public class PendingTradeSignalsTests {
        private static readonly DateTime DetectedAt = new(2026, 9, 28, 0, 45, 0, DateTimeKind.Utc);

        private static SignalModel Signal() => TestSignal.Long(close: 100.0, stopLoss: 98.0);

        [Fact]
        public void a_new_signal_gets_a_unique_id_and_keeps_the_original_signal() {
            var store = new PendingTradeSignals();
            SignalModel signal = Signal();

            PendingTradeSignalModel first = store.Add(signal, DetectedAt);
            PendingTradeSignalModel second = store.Add(Signal(), DetectedAt);

            Assert.False(string.IsNullOrWhiteSpace(first.SignalId));
            Assert.NotEqual(first.SignalId, second.SignalId);
            Assert.Same(signal, first.Signal);
            Assert.Equal(DetectedAt, first.DetectedAtUtc);
        }

        [Fact]
        public void an_approval_returns_the_very_signal_that_was_stored() {
            var store = new PendingTradeSignals();
            SignalModel signal = Signal();
            string signalId = store.Add(signal, DetectedAt).SignalId;

            ApprovalOutcomeModel outcome = store.Consume(signalId, out PendingTradeSignalModel pending);

            Assert.Equal(ApprovalOutcomeModel.Approved, outcome);
            Assert.Same(signal, pending.Signal);
        }

        [Fact]
        public void an_id_matches_only_its_own_signal() {
            var store = new PendingTradeSignals();
            SignalModel wanted = Signal();
            store.Add(Signal(), DetectedAt);
            string signalId = store.Add(wanted, DetectedAt).SignalId;
            store.Add(Signal(), DetectedAt);

            store.Consume(signalId, out PendingTradeSignalModel pending);

            Assert.Same(wanted, pending.Signal);
        }

        [Fact]
        public void a_second_decision_for_the_same_id_is_not_approved_again() {
            var store = new PendingTradeSignals();
            string signalId = store.Add(Signal(), DetectedAt).SignalId;
            store.Consume(signalId, out _);

            Assert.Equal(ApprovalOutcomeModel.AlreadyHandled, store.Consume(signalId, out _));
            Assert.Equal(ApprovalOutcomeModel.AlreadyHandled, store.Consume(signalId, out _));
        }

        [Theory]
        [InlineData("not-an-issued-id")]
        [InlineData("")]
        [InlineData(null)]
        public void an_id_this_store_never_issued_is_unknown(string signalId) {
            var store = new PendingTradeSignals();
            store.Add(Signal(), DetectedAt);

            ApprovalOutcomeModel outcome = store.Consume(signalId, out PendingTradeSignalModel pending);

            Assert.Equal(ApprovalOutcomeModel.Unknown, outcome);
            Assert.Null(pending);
        }

        [Fact]
        public void an_undecided_signal_stays_approvable_however_many_signals_follow() {
            var store = new PendingTradeSignals();
            string oldId = store.Add(Signal(), DetectedAt).SignalId;

            store.Add(Signal(), DetectedAt.AddDays(3));

            Assert.Equal(ApprovalOutcomeModel.Approved, store.Consume(oldId, out _));
        }

        [Fact]
        public void a_decided_signal_is_forgotten_a_day_after_detection() {
            var store = new PendingTradeSignals();
            string decidedId = store.Add(Signal(), DetectedAt).SignalId;
            store.Consume(decidedId, out _);

            store.Add(Signal(), DetectedAt.AddDays(1).AddMinutes(1));

            Assert.Equal(ApprovalOutcomeModel.Unknown, store.Consume(decidedId, out _));
        }
    }
}
