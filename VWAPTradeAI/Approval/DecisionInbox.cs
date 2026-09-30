using System.Collections.Concurrent;
using System.Collections.Generic;

namespace cAlgo.Robots;

// The user's decisions, on their way from the relay link's thread to the cBot thread. Post is
// safe from any thread; TakeAll is for the cBot thread, which picks decisions up when
// BeginInvokeOnMainThread gets round to it, or on its next tick.
//
// Pattern: Producer–Consumer. The relay link's thread produces, the cBot thread consumes, and a
// thread-safe queue between them is the only shared state. This is the one class in Approval/
// that may be touched from two threads.
//
// No cAlgo dependency, unit tested.
public class DecisionInbox {
    private readonly ConcurrentQueue<TradeDecisionModel> _decisions = new();

    public void Post(TradeDecisionModel decision) {
        _decisions.Enqueue(decision);
    }

    // Every decision that has arrived, oldest first.
    public IEnumerable<TradeDecisionModel> TakeAll() {
        while (_decisions.TryDequeue(out TradeDecisionModel decision))
            yield return decision;
    }
}
