namespace cAlgo.Robots;

// What a decision for a signal ID found (see PendingTradeSignals.Consume).
public enum ApprovalOutcomeModel {
    Approved, // pending: consumed now, may be executed this once
    AlreadyHandled, // decided before; a repeat never executes again
    Unknown // not issued by this cBot instance, or forgotten a day after it was decided
}
