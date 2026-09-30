using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.Approval {
    // The acceptance test for manual trading: a detected signal never places an order by itself.
    // Only an approval for its ID may call TryEnter, and only once, with the original signal.
    public class TradeApprovalTests {
        private static readonly DateTime DetectedAt = new(2026, 9, 28, 0, 45, 0, DateTimeKind.Utc);

        private readonly List<SignalModel> _orderAttempts = new();
        private readonly List<string> _sent = new();
        private string _brokerRejection;

        private TradeApproval Approval() {
            return new TradeApproval(new PendingTradeSignals(), "XAUUSD", FakeTryEnter, _sent.Add, _ => { });
        }

        // Stands in for OrderExecutor.TryEnter: records every attempt, and fails when told to.
        private bool FakeTryEnter(SignalModel signal, out string rejectReason) {
            _orderAttempts.Add(signal);
            rejectReason = _brokerRejection ?? "";
            return _brokerRejection == null;
        }

        private JsonElement LastSent() => JsonDocument.Parse(_sent.Last()).RootElement;

        private static SignalModel Signal() => TestSignal.Long(close: 100.0, stopLoss: 98.0);

        [Fact]
        public void a_detected_signal_places_no_order_and_announces_the_opportunity() {
            TradeApproval approval = Approval();

            PendingTradeSignalModel pending = approval.OnSignal(Signal(), DetectedAt);

            Assert.Empty(_orderAttempts);
            Assert.Equal("trade_opportunity", LastSent().GetProperty("type").GetString());
            Assert.Equal(pending.SignalId, LastSent().GetProperty("signal_id").GetString());
        }

        [Fact]
        public void many_detected_signals_still_place_no_order() {
            TradeApproval approval = Approval();

            for (int i = 0; i < 10; i++)
                approval.OnSignal(Signal(), DetectedAt.AddMinutes(5 * i));

            Assert.Empty(_orderAttempts);
        }

        [Fact]
        public void an_approval_orders_the_original_signal_once_and_reports_it_opened() {
            TradeApproval approval = Approval();
            SignalModel signal = Signal();
            string signalId = approval.OnSignal(signal, DetectedAt).SignalId;

            bool isOrdered = approval.Approve(signalId);

            Assert.True(isOrdered);
            Assert.Same(signal, Assert.Single(_orderAttempts));
            Assert.Equal("trade_opened", LastSent().GetProperty("type").GetString());
            Assert.Equal(signalId, LastSent().GetProperty("signal_id").GetString());
        }

        [Fact]
        public void a_repeated_approval_never_places_a_second_order() {
            TradeApproval approval = Approval();
            string signalId = approval.OnSignal(Signal(), DetectedAt).SignalId;
            approval.Approve(signalId);
            int messagesAfterFirst = _sent.Count;

            bool isOrderedAgain = approval.Approve(signalId);

            Assert.False(isOrderedAgain);
            Assert.Single(_orderAttempts);
            // Ignored silently: the app already has its result from the first approval.
            Assert.Equal(messagesAfterFirst, _sent.Count);
        }

        [Fact]
        public void an_approval_long_after_the_signal_still_attempts_the_order() {
            // No approval window: many later signals, and still the first one can be placed.
            TradeApproval approval = Approval();
            SignalModel first = Signal();
            string firstId = approval.OnSignal(first, DetectedAt).SignalId;

            for (int i = 1; i <= 300; i++)
                approval.OnSignal(Signal(), DetectedAt.AddMinutes(5 * i));

            Assert.True(approval.Approve(firstId));
            Assert.Same(first, Assert.Single(_orderAttempts));
        }

        [Fact]
        public void an_approval_for_an_unknown_id_places_no_order_and_sends_nothing() {
            // Several cBots can share the relay; an ID this one never issued belongs to another.
            TradeApproval approval = Approval();
            approval.OnSignal(Signal(), DetectedAt);
            int messagesBefore = _sent.Count;

            Assert.False(approval.Approve("another-bots-signal"));
            Assert.Empty(_orderAttempts);
            Assert.Equal(messagesBefore, _sent.Count);
        }

        [Fact]
        public void an_approved_order_the_pipeline_refuses_is_reported_with_its_reason() {
            TradeApproval approval = Approval();
            string signalId = approval.OnSignal(Signal(), DetectedAt).SignalId;
            _brokerRejection = "Level VWAP already has an open position on XAUUSD";

            bool isOrdered = approval.Approve(signalId);

            Assert.False(isOrdered);
            Assert.Single(_orderAttempts);
            Assert.Equal("trade_rejected", LastSent().GetProperty("type").GetString());
            Assert.Equal("order_rejected", LastSent().GetProperty("reason").GetString());
            Assert.Contains("already has an open position", LastSent().GetProperty("message").GetString());
        }

        [Fact]
        public void a_dismissed_signal_places_no_order_now_or_after_a_late_approval() {
            TradeApproval approval = Approval();
            string signalId = approval.OnSignal(Signal(), DetectedAt).SignalId;

            approval.Decide(new TradeDecisionModel(signalId, isApproved: false));
            bool isOrdered = approval.Decide(new TradeDecisionModel(signalId, isApproved: true));

            Assert.False(isOrdered);
            Assert.Empty(_orderAttempts);
        }

        [Fact]
        public void an_approving_decision_orders_like_approve() {
            TradeApproval approval = Approval();
            SignalModel signal = Signal();
            string signalId = approval.OnSignal(signal, DetectedAt).SignalId;

            bool isOrdered = approval.Decide(new TradeDecisionModel(signalId, isApproved: true));

            Assert.True(isOrdered);
            Assert.Same(signal, Assert.Single(_orderAttempts));
        }
    }
}
