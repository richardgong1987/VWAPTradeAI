using System;
using System.Text.Json;
using cAlgo.Robots;
using Xunit;

namespace DayTradeSelf.Tests.Notifications {
    // The JSON the cBot exchanges with the app through the relay. Field names are the contract
    // with rustapp/src-tauri/src/protocol.rs.
    public class TradeMessagesTests {
        private static readonly DateTime DetectedAt = new(2026, 9, 28, 0, 45, 0, DateTimeKind.Utc);

        private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

        private static PendingTradeSignalModel Pending(SignalModel signal) => new("abc123", signal, DetectedAt);

        [Fact]
        public void an_opportunity_carries_what_the_user_needs_to_decide() {
            JsonElement message = Parse(TradeMessages.Opportunity(Pending(TestSignal.Long(close: 2412.35, stopLoss: 2405.0)), "XAUUSD"));

            Assert.Equal("trade_opportunity", message.GetProperty("type").GetString());
            Assert.Equal("ctrader", message.GetProperty("source").GetString());
            Assert.Equal("abc123", message.GetProperty("signal_id").GetString());
            Assert.Equal("XAUUSD", message.GetProperty("symbol").GetString());
            Assert.Equal("buy", message.GetProperty("side").GetString());
            Assert.Equal("VWAP", message.GetProperty("level").GetString());
            Assert.Equal("L_Pin_1", message.GetProperty("pattern").GetString());
            Assert.Equal(2412.35, message.GetProperty("price").GetDouble());
            Assert.Equal("2026-09-28T00:45:00Z", message.GetProperty("timestamp").GetString());
        }

        [Fact]
        public void a_short_signal_is_announced_as_sell() {
            JsonElement message = Parse(TradeMessages.Opportunity(Pending(TestSignal.Short(close: 100.0, stopLoss: 102.0)), "XAUUSD"));

            Assert.Equal("sell", message.GetProperty("side").GetString());
        }

        [Fact]
        public void fields_a_message_does_not_use_are_left_out() {
            JsonElement message = Parse(TradeMessages.Opened(Pending(TestSignal.Long(100.0, 98.0)), "XAUUSD"));

            Assert.Equal("trade_opened", message.GetProperty("type").GetString());
            Assert.False(message.TryGetProperty("reason", out _));
            Assert.False(message.TryGetProperty("net_profit", out _));
        }

        [Fact]
        public void a_rejection_names_its_reason_and_the_pipelines_words() {
            JsonElement message = Parse(TradeMessages.Rejected(Pending(TestSignal.Long(100.0, 98.0)), "XAUUSD",
                TradeMessages.OrderRejected, "Level VWAP already has an open position on XAUUSD"));

            Assert.Equal("trade_rejected", message.GetProperty("type").GetString());
            Assert.Equal("abc123", message.GetProperty("signal_id").GetString());
            Assert.Equal("order_rejected", message.GetProperty("reason").GetString());
            Assert.Contains("already has an open position", message.GetProperty("message").GetString());
        }

        [Theory]
        [InlineData(25.30, "trade_profit")]
        [InlineData(0.01, "trade_profit")]
        [InlineData(-12.00, "trade_loss")]
        [InlineData(-0.01, "trade_loss")]
        [InlineData(0.0, "trade_breakeven")]
        [InlineData(0.004, "trade_breakeven")]
        [InlineData(-0.004, "trade_breakeven")]
        public void a_closed_trade_is_classified_by_its_net_profit(double netProfit, string expectedType) {
            JsonElement message = Parse(TradeMessages.Closed("XAUUSD", netProfit));

            Assert.Equal(expectedType, message.GetProperty("type").GetString());
            Assert.Equal(netProfit, message.GetProperty("net_profit").GetDouble());
            Assert.Equal("XAUUSD", message.GetProperty("symbol").GetString());
        }

        [Fact]
        public void place_trade_arrives_as_an_approval_for_its_signal() {
            Assert.True(TradeMessages.TryReadDecision("""{"type":"execute_trade","source":"tauri","signal_id":"abc123"}""",
                out TradeDecisionModel decision));
            Assert.Equal("abc123", decision.SignalId);
            Assert.True(decision.IsApproved);
        }

        [Fact]
        public void dismiss_arrives_as_a_refusal_for_its_signal() {
            Assert.True(TradeMessages.TryReadDecision("""{"type":"dismiss_trade","source":"tauri","signal_id":"abc123"}""",
                out TradeDecisionModel decision));
            Assert.Equal("abc123", decision.SignalId);
            Assert.False(decision.IsApproved);
        }

        [Fact]
        public void a_decision_with_extra_fields_is_still_read() {
            Assert.True(TradeMessages.TryReadDecision("""{"type":"execute_trade","signal_id":" abc123 ","note":"from phone"}""",
                out TradeDecisionModel decision));
            Assert.Equal("abc123", decision.SignalId);
        }

        [Theory]
        [InlineData("""{"type":"trade_opportunity","signal_id":"abc123"}""")]
        [InlineData("""{"type":"ack","source":"tauri"}""")]
        [InlineData("""{"type":"execute_trade","source":"tauri"}""")]
        [InlineData("""{"type":"dismiss_trade"}""")]
        [InlineData("""{"type":"execute_trade","signal_id":"  "}""")]
        [InlineData("""{"type":"EXECUTE_TRADE","signal_id":"abc123"}""")]
        [InlineData("""{"type":"execute_trade","signal_id":42}""")]
        [InlineData("ALARM")]
        [InlineData("{not json")]
        [InlineData("[]")]
        [InlineData("null")]
        [InlineData("")]
        public void anything_but_a_well_formed_decision_is_ignored(string text) {
            Assert.False(TradeMessages.TryReadDecision(text, out _));
        }
    }
}
