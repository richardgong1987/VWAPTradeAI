using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.TrendAssessment {
    // Reading the service's HTTP reply. Only a complete, in-contract HTTP 200 is an assessment;
    // anything else must come out as Unavailable, which the gate rejects.
    public class TrendAssessmentReplyTests {
        private const string OkReply = @"{
            ""request_id"": ""req-1"",
            ""status"": ""OK"",
            ""trend"": ""UP"",
            ""confidence"": 0.88,
            ""daily_vwap_direction"": ""RISING"",
            ""daily_vwap_confidence"": 0.91,
            ""structure"": ""Higher highs and higher lows"",
            ""reason"": ""Price rises from left to right and the solid yellow Daily VWAP slopes upward."",
            ""model"": ""gemma3:27b"",
            ""elapsed_ms"": 8120
        }";

        private const string UnreadableReply = @"{
            ""request_id"": ""req-1"",
            ""status"": ""UNREADABLE"",
            ""trend"": null,
            ""confidence"": null,
            ""daily_vwap_direction"": null,
            ""daily_vwap_confidence"": null,
            ""structure"": null,
            ""reason"": ""No candlesticks are visible."",
            ""model"": ""gemma3:27b"",
            ""elapsed_ms"": 8450
        }";

        private static TrendAssessmentResultModel ReadOkWith(string original, string replacement) =>
            TrendAssessmentReply.Read(200, OkReply.Replace(original, replacement));

        [Fact]
        public void a_complete_ok_reply_is_an_assessment() {
            TrendAssessmentResultModel result = TrendAssessmentReply.Read(200, OkReply);

            Assert.Equal(TrendAssessmentOutcomeModel.Assessed, result.Outcome);
            Assert.Equal(TrendModel.Up, result.Trend);
            Assert.Equal(0.88, result.TrendConfidence);
            Assert.Equal(DailyVwapDirectionModel.Rising, result.DailyVwapDirection);
            Assert.Equal(0.91, result.DailyVwapConfidence);
            Assert.Equal("Higher highs and higher lows", result.Structure);
            Assert.StartsWith("Price rises", result.Reason);
            Assert.Equal("gemma3:27b", result.ModelName);
        }

        [Theory]
        [InlineData("DOWN", TrendModel.Down)]
        [InlineData("SIDEWAYS", TrendModel.Sideways)]
        public void every_trend_of_the_contract_is_read(string text, TrendModel trend) {
            Assert.Equal(trend, ReadOkWith(@"""UP""", $@"""{text}""").Trend);
        }

        [Theory]
        [InlineData("FALLING", DailyVwapDirectionModel.Falling)]
        [InlineData("FLAT", DailyVwapDirectionModel.Flat)]
        public void every_daily_vwap_direction_of_the_contract_is_read(string text, DailyVwapDirectionModel direction) {
            Assert.Equal(direction, ReadOkWith(@"""RISING""", $@"""{text}""").DailyVwapDirection);
        }

        [Fact]
        public void an_unreadable_reply_is_unreadable_with_what_is_missing() {
            TrendAssessmentResultModel result = TrendAssessmentReply.Read(200, UnreadableReply);

            Assert.Equal(TrendAssessmentOutcomeModel.Unreadable, result.Outcome);
            Assert.Equal("No candlesticks are visible.", result.Reason);
            Assert.Null(result.Trend);
            Assert.Null(result.DailyVwapDirection);
        }

        [Theory]
        [InlineData(@"""status"": ""OK""", @"""status"": ""MAYBE""", "status is not OK or UNREADABLE")]
        [InlineData(@"""status"": ""OK"",", "", "status is not OK or UNREADABLE")]
        [InlineData(@"""trend"": ""UP""", @"""trend"": ""BULLISH""", "trend is not UP, DOWN or SIDEWAYS")]
        [InlineData(@"""trend"": ""UP""", @"""trend"": ""up""", "trend is not UP, DOWN or SIDEWAYS")]
        [InlineData(@"""trend"": ""UP""", @"""trend"": null", "trend is not UP, DOWN or SIDEWAYS")]
        [InlineData(@"""trend"": ""UP"",", "", "trend is not UP, DOWN or SIDEWAYS")]
        [InlineData(@"""RISING""", @"""UP""", "daily_vwap_direction is not RISING, FALLING or FLAT")]
        [InlineData(@"""daily_vwap_direction"": ""RISING"",", "", "daily_vwap_direction is not RISING, FALLING or FLAT")]
        [InlineData(@"""confidence"": 0.88", @"""confidence"": 1.2", "confidence is not a number from 0 to 1")]
        [InlineData(@"""confidence"": 0.88", @"""confidence"": -0.1", "confidence is not a number from 0 to 1")]
        [InlineData(@"""confidence"": 0.88", @"""confidence"": ""0.88""", "confidence is not a number from 0 to 1")]
        [InlineData(@"""confidence"": 0.88", @"""confidence"": null", "confidence is not a number from 0 to 1")]
        [InlineData(@"""daily_vwap_confidence"": 0.91", @"""daily_vwap_confidence"": 9", "daily_vwap_confidence is not a number from 0 to 1")]
        [InlineData(@"""structure"": ""Higher highs and higher lows""", @"""structure"": """"", "structure is missing")]
        [InlineData(@"""Price rises from left to right and the solid yellow Daily VWAP slopes upward.""", "null", "reason is missing")]
        public void an_ok_reply_outside_the_contract_is_unavailable(string original, string replacement, string problem) {
            TrendAssessmentResultModel result = ReadOkWith(original, replacement);

            Assert.Equal(TrendAssessmentOutcomeModel.Unavailable, result.Outcome);
            Assert.Equal($"The AI service's answer is invalid: {problem}", result.Reason);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("the chart goes up")]
        [InlineData(@"{""status"": ""OK""")]
        public void a_body_that_is_not_json_is_unavailable(string body) {
            TrendAssessmentResultModel result = TrendAssessmentReply.Read(200, body);

            Assert.Equal(TrendAssessmentOutcomeModel.Unavailable, result.Outcome);
            Assert.Contains("not JSON", result.Reason);
        }

        [Fact]
        public void json_that_is_not_an_object_is_unavailable() {
            Assert.Equal(TrendAssessmentOutcomeModel.Unavailable, TrendAssessmentReply.Read(200, @"[""UP""]").Outcome);
        }

        [Fact]
        public void a_service_error_is_unavailable_with_its_code_and_message() {
            const string body = @"{""request_id"": ""req-1"", ""error"": {""code"": ""model_timeout"", ""message"": ""Ollama did not answer within 25 s""}}";

            TrendAssessmentResultModel result = TrendAssessmentReply.Read(504, body);

            Assert.Equal(TrendAssessmentOutcomeModel.Unavailable, result.Outcome);
            Assert.Equal("The AI service answered HTTP 504 model_timeout: Ollama did not answer within 25 s", result.Reason);
        }

        [Fact]
        public void an_assessment_under_an_error_status_is_still_unavailable() {
            // Only HTTP 200 may carry a judgement, whatever the body claims.
            TrendAssessmentResultModel result = TrendAssessmentReply.Read(502, OkReply);

            Assert.Equal(TrendAssessmentOutcomeModel.Unavailable, result.Outcome);
            Assert.Null(result.Trend);
        }
    }
}
