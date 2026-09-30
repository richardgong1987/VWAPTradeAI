using System.Collections.Generic;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.Orders {
    // The order gates, in order: an open position on the level, price and sizing, the broker.
    // Each one stops the order and says why; only an order that passes all of them is announced.
    public class OrderExecutorTests {
        private const string OrderLabel = "VWAPTradeAI-label";

        // Quoted right at the signal's close, as if the next bar opened there with no spread.
        private readonly FakeBroker _broker = new() { Bid = 100.0, Ask = 100.0 };
        private readonly List<(OrderPlanModel Plan, PositionEntryModel Position)> _opened = new();
        private readonly List<PositionCloseModel> _closed = new();

        private OrderExecutor Executor() {
            var symbol = new FakeSymbolModel { PipSize = 0.1, LotSize = 100.0, PipValue = 0.1 };
            var executor = new OrderExecutor(_broker, new OrderPlanner(symbol, TestSettings.Create()), OrderLabel, _ => { });
            executor.PositionOpened += (plan, position) => _opened.Add((plan, position));
            executor.PositionClosed += _closed.Add;
            return executor;
        }

        private static SignalModel Signal() => TestSignal.Long(close: 100.0, stopLoss: 98.0);

        [Fact]
        public void a_long_is_planned_from_the_ask_and_a_short_from_the_bid() {
            _broker.Bid = 100.4;
            _broker.Ask = 100.6;
            OrderExecutor executor = Executor();

            executor.TryEnter(TestSignal.Long(close: 100.0, stopLoss: 98.0), out _);
            executor.TryEnter(TestSignal.Short(close: 100.0, stopLoss: 102.0), out _);

            Assert.Equal(new[] { 100.6, 100.4 }, _broker.Orders.ConvertAll(order => order.Plan.EntryPrice));
        }

        [Fact]
        public void a_signal_whose_target_price_has_already_reached_goes_to_no_broker() {
            // The signal's 2R target is 104; price is already there.
            _broker.Ask = 104.0;

            bool isOrdered = Executor().TryEnter(Signal(), out string rejectReason);

            Assert.False(isOrdered);
            Assert.Contains("has already reached the take profit", rejectReason);
            Assert.Empty(_broker.Orders);
        }

        [Fact]
        public void an_order_that_passes_every_gate_goes_out_under_its_level_label_and_is_announced() {
            OrderExecutor executor = Executor();
            SignalModel signal = Signal();

            bool isOrdered = executor.TryEnter(signal, out string rejectReason);

            Assert.True(isOrdered, rejectReason);
            var order = Assert.Single(_broker.Orders);
            Assert.Equal("VWAPTradeAI-label_VWAP", order.Label);
            Assert.Equal("ENTRY", order.Comment);
            var opened = Assert.Single(_opened);
            Assert.Same(signal, opened.Plan.Signal);
            Assert.Equal(7, opened.Position.PositionId);
        }

        [Fact]
        public void a_level_that_already_has_a_position_takes_no_second_one() {
            _broker.OpenLabels.Add("VWAPTradeAI-label_VWAP");

            bool isOrdered = Executor().TryEnter(Signal(), out string rejectReason);

            Assert.False(isOrdered);
            Assert.Contains("already has an open position on XAUUSD", rejectReason);
            Assert.Empty(_broker.Orders);
        }

        [Fact]
        public void a_trade_that_cannot_be_sized_is_rejected_with_the_planners_reason() {
            _broker.AccountEquity = 0.0;

            bool isOrdered = Executor().TryEnter(Signal(), out string rejectReason);

            Assert.False(isOrdered);
            Assert.Contains("below broker minimum", rejectReason);
            Assert.Empty(_broker.Orders);
        }

        [Fact]
        public void a_broker_refusal_is_reported_and_nothing_is_announced() {
            _broker.Refusal = "NotEnoughMoney";

            bool isOrdered = Executor().TryEnter(Signal(), out string rejectReason);

            Assert.False(isOrdered);
            Assert.Equal("The broker refused the order: NotEnoughMoney", rejectReason);
            Assert.Empty(_opened);
        }

        [Theory]
        [InlineData("VWAPTradeAI-label_VWAP", true)]
        [InlineData("OtherBot_VWAP", false)] // another cBot on the same symbol
        [InlineData("VWAPTradeAI-label", false)] // no level suffix: not one of ours
        [InlineData("", false)] // a manual trade
        public void only_this_strategys_positions_are_announced_as_closed(string label, bool isAnnounced) {
            Executor();

            _broker.Close(new PositionCloseModel { PositionId = 7, Label = label });

            Assert.Equal(isAnnounced ? 1 : 0, _closed.Count);
        }
    }
}
