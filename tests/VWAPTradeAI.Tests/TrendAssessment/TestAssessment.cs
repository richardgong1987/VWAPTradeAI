using cAlgo.Robots;

namespace VWAPTradeAI.Tests.TrendAssessment {
    // Builds the assessments the gate and the filter are tested with. Tests state only the two
    // classifications that decide a trade; the rest is filled in.
    internal static class TestAssessment {
        public const string ModelName = "gemma3:27b";

        public static TrendAssessmentResultModel Of(TrendModel trend, DailyVwapDirectionModel dailyVwapDirection) {
            return TrendAssessmentResultModel.Assessed(trend, trendConfidence: 0.91, dailyVwapDirection, dailyVwapConfidence: 0.88,
                structure: "Higher highs and higher lows", reason: "Price rises and the daily VWAP slopes.", ModelName);
        }

        public static TrendAssessmentResultModel Unreadable() {
            return TrendAssessmentResultModel.Unreadable("No candlesticks are visible.", ModelName);
        }

        public static TrendAssessmentResultModel Unavailable() {
            return TrendAssessmentResultModel.Unavailable("Local assessment request timed out after 30 seconds");
        }
    }
}
