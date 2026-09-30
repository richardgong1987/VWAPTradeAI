using System;

namespace cAlgo.Robots;

// What the order side needs from cTrader, in plain models: the account, this symbol's quote and
// positions, market orders and closes. OrderExecutor and the trade CSV see nothing else, so they
// are unit tested against a fake.
//
// Pattern: Adapter — this is the target interface; CAlgoBroker adapts cTrader's Robot API to it.
// An interface with one real implementation is justified here only because the tests cannot load
// cAlgo.API (same reason as ISymbolModel).
public interface IBroker {
    string SymbolName { get; }

    double AccountEquity { get; }

    // This symbol's current quote: a market buy fills at the ask, a market sell at the bid.
    double Bid { get; }
    double Ask { get; }

    bool HasOpenPosition(string label);

    BrokerOrderResultModel PlaceMarketOrder(OrderPlanModel plan, string label, string comment);

    // Every position on this symbol that closes, whoever opened it; telling ours apart is
    // OrderExecutor's job.
    event Action<PositionCloseModel> PositionClosed;
}
