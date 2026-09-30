namespace cAlgo.Robots;

// The AI trend rule: may this signal go on to OrderExecutor, given what the model saw on the chart?
//
//   Buy  passes when the trend is UP   and the daily VWAP is not FLAT
//   Sell passes when the trend is DOWN and the daily VWAP is not FLAT
//
// Everything else is rejected: a SIDEWAYS trend, a trend against the signal, a FLAT daily VWAP, an
// unreadable chart, and any assessment that is unavailable.
//
// The daily VWAP only has to have a slope; it need not slope the signal's way. A flat one means the
// trend is too weak for this strategy. Confidence plays no part: the model's own estimate is not
// calibrated, so it is logged for evaluation and never used as a threshold.
//
// Pure, no cAlgo dependency, unit tested.
public static class TrendDirectionGate {
    // Null when the signal passes; otherwise the reason it is rejected.
    public static string FindRejectReason(SignalSideModel side, TrendAssessmentResultModel assessment) {
        switch (assessment.Outcome) {
            case TrendAssessmentOutcomeModel.Unavailable:
                return $"Assessment unavailable: {assessment.Reason}";
            case TrendAssessmentOutcomeModel.Unreadable:
                return $"Chart unreadable: {assessment.Reason}";
        }

        if (side == SignalSideModel.None)
            return "The signal has no side";

        if (assessment.Trend == TrendModel.Sideways)
            return "Trend is SIDEWAYS";

        TrendModel trendForSide = side == SignalSideModel.Buy ? TrendModel.Up : TrendModel.Down;

        if (assessment.Trend != trendForSide)
            return $"Trend {Upper(assessment.Trend)} is against a {side} signal";

        if (assessment.DailyVwapDirection == DailyVwapDirectionModel.Flat)
            return "Daily VWAP is FLAT";

        return null;
    }

    private static string Upper(TrendModel? trend) {
        return trend.ToString().ToUpperInvariant();
    }
}
