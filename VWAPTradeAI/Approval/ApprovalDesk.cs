using System;

namespace cAlgo.Robots;

// The Robot's side of manual approval, in three calls:
//
//   OnSignal                cBot thread   store and announce the signal
//   ReceiveRelayText        link thread   queue a decision; true = apply it on the cBot thread
//   ApplyReceivedDecisions  cBot thread   apply every decision queued so far
//
// Nothing here waits for the user, in a backtest either, and nothing expires: the bars keep coming,
// and an approval that arrives later enters at the price of that moment, with the signal's own
// stop and target (see OrderPlanner).
//
// Pattern: Facade. Behind it TradeApproval applies the rules, DecisionInbox carries decisions across
// threads and TradeMessages reads the relay's JSON; the Robot knows none of them.
//
// No cAlgo dependency: the clock comes in as a function, so it is unit tested.
public class ApprovalDesk {
    private readonly TradeApproval _approval;
    private readonly DecisionInbox _inbox = new();
    private readonly Func<DateTime> _utcNow;

    // utcNow stamps each signal's detection time, which the app shows as the signal time.
    public ApprovalDesk(TradeApproval approval, Func<DateTime> utcNow) {
        _approval = approval;
        _utcNow = utcNow;
    }

    public void OnSignal(SignalModel signal) {
        _approval.OnSignal(signal, _utcNow());
    }

    // Safe from any thread. Only the user's decisions matter; every other message is ignored.
    public bool ReceiveRelayText(string text) {
        if (!TradeMessages.TryReadDecision(text, out TradeDecisionModel decision))
            return false;

        _inbox.Post(decision);
        return true;
    }

    public void ApplyReceivedDecisions() {
        foreach (TradeDecisionModel decision in _inbox.TakeAll())
            _approval.Decide(decision);
    }
}
