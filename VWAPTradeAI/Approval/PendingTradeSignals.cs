using System;
using System.Collections.Generic;
using System.Linq;

namespace cAlgo.Robots;

// The signals waiting for manual approval, by signal ID. Each one can be consumed once, however
// long after it was detected: there is no approval window. An undecided signal stays here until
// it is decided (or the cBot restarts).
//
// Main-thread only: the cBot adds in OnBar and consumes inside BeginInvokeOnMainThread, so the
// two never overlap and consuming needs no lock to be atomic.
//
// Pure, no cAlgo dependency, unit tested.
public class PendingTradeSignals {
    // Decided signals are kept a while, so a repeated decision is reported as already handled
    // rather than as unknown. They go once they were detected more than this before the newest
    // signal; a day of M5 signals is a few dozen small records.
    private static readonly TimeSpan DecidedRetention = TimeSpan.FromDays(1);

    private readonly Dictionary<string, Entry> _entries = new();

    public PendingTradeSignalModel Add(SignalModel signal, DateTime detectedAtUtc) {
        ForgetDecidedBefore(detectedAtUtc - DecidedRetention);

        var pending = new PendingTradeSignalModel(Guid.NewGuid().ToString("N"), signal, detectedAtUtc);
        _entries[pending.SignalId] = new Entry(pending);
        return pending;
    }

    // Whatever the outcome, a signal ID found here can never be approved again. `pending` is set
    // whenever the ID is known, so a repeated decision can still be reported fully.
    public ApprovalOutcomeModel Consume(string signalId, out PendingTradeSignalModel pending) {
        pending = null;

        if (string.IsNullOrEmpty(signalId) || !_entries.TryGetValue(signalId, out Entry entry))
            return ApprovalOutcomeModel.Unknown;

        pending = entry.Pending;

        if (entry.IsConsumed)
            return ApprovalOutcomeModel.AlreadyHandled;

        entry.IsConsumed = true;
        return ApprovalOutcomeModel.Approved;
    }

    private void ForgetDecidedBefore(DateTime cutoffUtc) {
        List<string> forgotten = _entries.Where(pair => pair.Value.IsConsumed && pair.Value.Pending.DetectedAtUtc < cutoffUtc)
            .Select(pair => pair.Key)
            .ToList();

        foreach (string signalId in forgotten)
            _entries.Remove(signalId);
    }

    private sealed class Entry {
        public Entry(PendingTradeSignalModel pending) {
            Pending = pending;
        }

        public PendingTradeSignalModel Pending { get; }

        public bool IsConsumed { get; set; }
    }
}
