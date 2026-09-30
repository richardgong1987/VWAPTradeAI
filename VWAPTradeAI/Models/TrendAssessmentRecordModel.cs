namespace cAlgo.Robots;

// Everything about one AI assessment that is worth reviewing later: the signal it was for, what
// came back, what the gate decided and whether an order went out (see TrendAssessmentRecorder).
public class TrendAssessmentRecordModel {
    public string RequestId { get; init; }

    public SignalModel Signal { get; init; }

    public TrendAssessmentResultModel Assessment { get; init; }

    // Why the signal was not sent to OrderExecutor; null when the gate passed it.
    public string GateRejectReason { get; init; }

    public bool IsOrderPlaced { get; init; }

    // OrderExecutor's own reason when the gate passed the signal but no order went out.
    public string OrderRejectReason { get; init; }

    // From sending the picture to having the answer back on the cBot thread.
    public long ElapsedMs { get; init; }
}
