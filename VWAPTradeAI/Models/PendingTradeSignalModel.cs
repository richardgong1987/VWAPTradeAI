using System;

namespace cAlgo.Robots;

// A detected signal waiting for the user's approval. The original SignalModel stays here, inside
// the cBot: an approval carries only SignalId back, so nothing about the trade is ever rebuilt
// from JSON.
public class PendingTradeSignalModel {
    public PendingTradeSignalModel(string signalId, SignalModel signal, DateTime detectedAtUtc) {
        SignalId = signalId;
        Signal = signal;
        DetectedAtUtc = detectedAtUtc;
    }

    public string SignalId { get; }

    public SignalModel Signal { get; }

    public DateTime DetectedAtUtc { get; }
}
