using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots;

// Pattern: Adapter. Translates cTrader's Robot API (Positions, History, ExecuteMarketOrder,
// TradeType, PositionCloseReason) into IBroker's plain models. It decides nothing; every rule about
// which order to place, and what to record, lives on the other side of IBroker.
public class CAlgoBroker : IBroker {
    private readonly Robot _robot;

    public CAlgoBroker(Robot robot) {
        _robot = robot;
        _robot.Positions.Closed += OnPositionClosed;
    }

    public event Action<PositionCloseModel> PositionClosed;

    public string SymbolName => _robot.SymbolName;

    public double AccountEquity => _robot.Account.Equity;

    public double Bid => _robot.Symbol.Bid;

    public double Ask => _robot.Symbol.Ask;

    // Asks the broker's live positions rather than a list kept in memory, so a restart never opens
    // a second position on the same level.
    public bool HasOpenPosition(string label) {
        return _robot.Positions.Any(position => position.SymbolName == SymbolName && position.Label == label);
    }

    // cTrader places a market order's stop and target at a distance from its fill, so the plan's
    // prices land exactly only when the fill is the quote the plan was made from; a live fill that
    // slips moves both by the slip.
    public BrokerOrderResultModel PlaceMarketOrder(OrderPlanModel plan, string label, string comment) {
        TradeType tradeType = plan.Direction == TradeDirectionModel.Long ? TradeType.Buy : TradeType.Sell;
        TradeResult result = _robot.ExecuteMarketOrder(tradeType, SymbolName, plan.VolumeInUnits, label, plan.StopLossPips,
            plan.TakeProfitPips, comment);

        if (!result.IsSuccessful)
            return BrokerOrderResultModel.Refused(result.Error?.ToString() ?? "");

        return BrokerOrderResultModel.Filled(ToEntry(result.Position));
    }

    private void OnPositionClosed(PositionClosedEventArgs args) {
        if (args?.Position == null || args.Position.SymbolName != SymbolName)
            return;

        PositionClosed?.Invoke(ToClose(args.Position, args.Reason));
    }

    private static PositionEntryModel ToEntry(Position position) {
        return new PositionEntryModel {
            PositionId = position.Id,
            EntryTime = position.EntryTime,
            EntryPrice = position.EntryPrice,
            StopLoss = position.StopLoss,
            VolumeInUnits = position.VolumeInUnits,
            DealId = DealId(position, isOpening: true)
        };
    }

    // Read at the moment of the close: the server time and the equity are the close's own.
    private PositionCloseModel ToClose(Position position, PositionCloseReason reason) {
        return new PositionCloseModel {
            PositionId = position.Id,
            Label = position.Label ?? "",
            Direction = position.TradeType == TradeType.Buy ? TradeDirectionModel.Long : TradeDirectionModel.Short,
            EntryTime = position.EntryTime,
            EntryPrice = position.EntryPrice,
            ClosePrice = FindClosePrice(position),
            CloseTime = _robot.Server.Time,
            CloseReason = ToCloseReason(reason),
            VolumeInUnits = position.VolumeInUnits,
            NetProfit = position.NetProfit,
            AccountEquity = _robot.Account.Equity,
            DealId = DealId(position, isOpening: false)
        };
    }

    // The trade history's fill price first; failing that, the position's own closing deal.
    private double FindClosePrice(Position position) {
        HistoricalTrade[] closedTrades = _robot.History.FindByPositionId(position.Id);

        if (closedTrades != null && closedTrades.Length > 0)
            return closedTrades.OrderByDescending(trade => trade.ClosingTime).First().ClosingPrice;

        for (int i = position.Deals.Count - 1; i >= 0; i--) {
            Deal deal = position.Deals[i];

            if (deal.PositionImpact == DealPositionImpact.Closing && deal.ExecutionPrice.HasValue)
                return deal.ExecutionPrice.Value;
        }

        return 0.0;
    }

    private static string DealId(Position position, bool isOpening) {
        if (position.Deals == null || position.Deals.Count == 0)
            return "";

        Deal deal = isOpening ? position.Deals[0] : position.Deals[position.Deals.Count - 1];
        return deal.Id.ToString();
    }

    private static PositionCloseReasonModel ToCloseReason(PositionCloseReason reason) {
        switch (reason) {
            case PositionCloseReason.StopLoss:
                return PositionCloseReasonModel.StopLoss;
            case PositionCloseReason.StopOut:
                return PositionCloseReasonModel.StopOut;
            case PositionCloseReason.TakeProfit:
                return PositionCloseReasonModel.TakeProfit;
            default:
                return PositionCloseReasonModel.Closed;
        }
    }
}
