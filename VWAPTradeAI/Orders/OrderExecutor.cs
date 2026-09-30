using System;

namespace cAlgo.Robots;

// Turns an approved signal into a market order at the current price, or says which gate stopped
// it. The gates, in order:
//
//   no open position on this level → price still between stop and target, and sizing
//   (OrderPlanner) → broker
//
// There is no order window: an approval may trade at any hour the market is open.
//
// Its only caller is TradeApproval.Approve: a signal never trades by itself.
//
// Pattern: Observer. PositionOpened and PositionClosed announce this strategy's trades; the trade
// CSV and the app notice subscribe in the composition root, so the executor knows
// none of them. It reaches cTrader only through IBroker, so it is unit tested.
public class OrderExecutor {
    private const string EntryComment = "ENTRY";

    private readonly IBroker _broker;
    private readonly OrderPlanner _planner;
    private readonly Action<string> _log;

    // Labels are "{OrderLabel}_{level name}", e.g. "DayTradeSelf-label_VWAP". The level suffix gives
    // each level its own position; the prefix tells this instance's positions apart from manual
    // ones and from other cBots on the same symbol.
    private readonly string _labelPrefix;

    public OrderExecutor(IBroker broker, OrderPlanner planner, string orderLabel, Action<string> log) {
        _broker = broker;
        _planner = planner;
        _labelPrefix = orderLabel + "_";
        _log = log;

        _broker.PositionClosed += OnBrokerPositionClosed;
    }

    // An order went out: the plan it was sized from, and the position as the broker opened it.
    public event Action<OrderPlanModel, PositionEntryModel> PositionOpened;

    // One of this strategy's positions closed.
    public event Action<PositionCloseModel> PositionClosed;

    // True when an order went out; otherwise rejectReason says which gate stopped it.
    public bool TryEnter(SignalModel signal, out string rejectReason) {
        string label = _labelPrefix + signal.Level.Name;

        if (_broker.HasOpenPosition(label))
            return Reject($"Level {signal.Level.Name} already has an open position on {_broker.SymbolName}", out rejectReason);

        double entryPrice = signal.Level.Side == SignalSideModel.Buy ? _broker.Ask : _broker.Bid;
        OrderPlanModel plan = _planner.CreatePlan(signal, entryPrice, _broker.AccountEquity);

        if (!plan.IsValid)
            return Reject(plan.RejectReason, out rejectReason);

        LogPlan(plan);
        BrokerOrderResultModel result = _broker.PlaceMarketOrder(plan, label, EntryComment);

        if (!result.IsFilled)
            return Reject($"The broker refused the order: {result.Error}", out rejectReason);

        _log($"Order submitted | Label: {label}");
        PositionOpened?.Invoke(plan, result.Position);
        rejectReason = "";
        return true;
    }

    private bool Reject(string reason, out string rejectReason) {
        _log($"Order not placed | {reason}");
        rejectReason = reason;
        return false;
    }

    private void LogPlan(OrderPlanModel plan) {
        _log(
            $"Order plan | Level: {plan.Signal.Level.Name}, Side: {plan.Direction}, SignalClose: {plan.Signal.Close}, Entry: {plan.EntryPrice}, " +
            $"Stop: {plan.StopPrice}, TakeProfit: {plan.TakeProfitPrice}, RiskPrice: {plan.RiskPrice}, " +
            $"StopLossPips: {plan.StopLossPips}, RiskMoney: {plan.RiskMoney}, EstimatedRiskMoney: {plan.EstimatedRiskMoney}, " +
            $"Lots: {plan.Lots}, VolumeUnits: {plan.VolumeInUnits}");
    }

    private void OnBrokerPositionClosed(PositionCloseModel position) {
        if (IsStrategyLabel(position.Label))
            PositionClosed?.Invoke(position);
    }

    private bool IsStrategyLabel(string label) {
        return !string.IsNullOrWhiteSpace(label) && label.StartsWith(_labelPrefix, StringComparison.Ordinal);
    }
}
