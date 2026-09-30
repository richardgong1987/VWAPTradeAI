using System;

namespace cAlgo.Robots;

// OrderExecutor.TryEnter's shape, so the approval flow can be tested without a broker.
public delegate bool TryEnterOrder(SignalModel signal, out string rejectReason);

// Manual trade approval. A detected signal is only stored and announced; it can become an order
// only through Approve, i.e. after the user pressed Place Trade in the app:
//
//   OnSignal  store as pending → send trade_opportunity                         (never trades)
//   Approve   consume the ID once → TryEnter(original signal) → trade_opened / trade_rejected
//   Dismiss   consume the ID once, nothing else                                 (never trades)
//
// There is no approval window: a signal stays approvable until it is decided, and a late approval
// enters at the price of that moment (see OrderPlanner).
//
// Approval asks for an attempt, nothing more: the order still passes every OrderExecutor check
// (open position, price still between stop and target, sizing, broker).
//
// Main-thread only, like PendingTradeSignals. Pure, no cAlgo dependency, unit tested.
public class TradeApproval {
    private readonly PendingTradeSignals _pending;
    private readonly string _symbol;
    private readonly TryEnterOrder _tryEnter;
    private readonly Action<string> _send;
    private readonly Action<string> _log;

    public TradeApproval(PendingTradeSignals pending, string symbol, TryEnterOrder tryEnter, Action<string> send, Action<string> log) {
        _pending = pending;
        _symbol = symbol;
        _tryEnter = tryEnter;
        _send = send;
        _log = log;
    }

    public PendingTradeSignalModel OnSignal(SignalModel signal, DateTime nowUtc) {
        PendingTradeSignalModel pending = _pending.Add(signal, nowUtc);
        _log($"Signal {pending.SignalId} awaiting approval | {signal.Level.Side} {signal.Label} at {signal.Close}");
        _send(TradeMessages.Opportunity(pending, _symbol));
        return pending;
    }

    // True when an order went out.
    public bool Decide(TradeDecisionModel decision) {
        if (decision.IsApproved)
            return Approve(decision.SignalId);

        Dismiss(decision.SignalId);
        return false;
    }

    // The user declined. The signal is spent: a later approval for it never trades.
    public void Dismiss(string signalId) {
        switch (_pending.Consume(signalId, out _)) {
            case ApprovalOutcomeModel.Unknown:
                _log($"Dismissal of unknown signal {signalId} ignored");
                return;
            case ApprovalOutcomeModel.AlreadyHandled:
                _log($"Dismissal of signal {signalId} ignored: already handled");
                return;
            default:
                _log($"Signal {signalId} dismissed by the user");
                return;
        }
    }

    // True when an order went out.
    public bool Approve(string signalId) {
        switch (_pending.Consume(signalId, out PendingTradeSignalModel pending)) {
            case ApprovalOutcomeModel.Unknown:
                // Every cBot on the relay receives every approval; only the one that issued the ID acts.
                _log($"Approval for unknown signal {signalId} ignored");
                return false;
            case ApprovalOutcomeModel.AlreadyHandled:
                _log($"Repeated approval for signal {signalId} ignored");
                return false;
        }

        _log($"Signal {signalId} approved; attempting the order");

        if (!_tryEnter(pending.Signal, out string rejectReason)) {
            _send(TradeMessages.Rejected(pending, _symbol, TradeMessages.OrderRejected, rejectReason));
            return false;
        }

        _send(TradeMessages.Opened(pending, _symbol));
        return true;
    }
}
