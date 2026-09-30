namespace cAlgo.Robots;

// What came back for one chart picture: the model's judgement, or why there is none. An unusable
// chart and a failed request are expected outcomes, not exceptions; both mean no trade.
public class TrendAssessmentResultModel {
    private TrendAssessmentResultModel(TrendAssessmentOutcomeModel outcome, string reason) {
        Outcome = outcome;
        Reason = reason;
    }

    public static TrendAssessmentResultModel Assessed(TrendModel trend, double trendConfidence, DailyVwapDirectionModel dailyVwapDirection,
        double dailyVwapConfidence, string structure, string reason, string modelName) {
        return new TrendAssessmentResultModel(TrendAssessmentOutcomeModel.Assessed, reason) {
            Trend = trend,
            TrendConfidence = trendConfidence,
            DailyVwapDirection = dailyVwapDirection,
            DailyVwapConfidence = dailyVwapConfidence,
            Structure = structure,
            ModelName = modelName
        };
    }

    public static TrendAssessmentResultModel Unreadable(string reason, string modelName) {
        return new TrendAssessmentResultModel(TrendAssessmentOutcomeModel.Unreadable, reason) { ModelName = modelName };
    }

    public static TrendAssessmentResultModel Unavailable(string reason) {
        return new TrendAssessmentResultModel(TrendAssessmentOutcomeModel.Unavailable, reason);
    }

    public TrendAssessmentOutcomeModel Outcome { get; }

    // Assessed: why the model chose its classifications. Unreadable: what the picture lacks.
    // Unavailable: what went wrong.
    public string Reason { get; }

    // The judgement. Null unless Assessed.
    public TrendModel? Trend { get; private init; }

    public DailyVwapDirectionModel? DailyVwapDirection { get; private init; }

    // The model's own estimates, 0 to 1. Not calibrated: logged and recorded, never a trading rule.
    public double? TrendConfidence { get; private init; }

    public double? DailyVwapConfidence { get; private init; }

    public string Structure { get; private init; }

    // The model the service used; null when the service never answered.
    public string ModelName { get; private init; }
}
