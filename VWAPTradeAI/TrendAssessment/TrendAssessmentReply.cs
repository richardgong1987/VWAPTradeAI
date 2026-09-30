using System.Text.Json;

namespace cAlgo.Robots;

// Reads one HTTP reply of the trend assessment service (POST /v1/assessments) into a result.
//
// Strict: only HTTP 200 with every field inside the contract is an assessment. The service
// validates the model's output itself, and the reply is checked again here, so a trade never
// depends on another project having got it right. Anything else is Unavailable, with the reason.
//
// Pure, no cAlgo dependency, unit tested.
public static class TrendAssessmentReply {
    private const int HttpOk = 200;

    public static TrendAssessmentResultModel Read(int httpStatus, string body) {
        JsonDocument document;

        try {
            document = JsonDocument.Parse(body ?? "");
        } catch (JsonException) {
            return TrendAssessmentResultModel.Unavailable($"The AI service answered HTTP {httpStatus} with a body that is not JSON");
        }

        using (document) {
            JsonElement reply = document.RootElement;

            if (reply.ValueKind != JsonValueKind.Object)
                return TrendAssessmentResultModel.Unavailable($"The AI service answered HTTP {httpStatus} with JSON that is not an object");

            return httpStatus == HttpOk ? ReadAssessment(reply) : ReadFailure(httpStatus, reply);
        }
    }

    private static TrendAssessmentResultModel ReadAssessment(JsonElement reply) {
        string modelName = ReadText(reply, "model");

        switch (ReadText(reply, "status")) {
            case "OK":
                return ReadJudgement(reply, modelName);
            case "UNREADABLE":
                return ReadUnreadable(reply, modelName);
            default:
                return Invalid("status is not OK or UNREADABLE");
        }
    }

    private static TrendAssessmentResultModel ReadJudgement(JsonElement reply, string modelName) {
        TrendModel? trend = ReadText(reply, "trend") switch {
            "UP" => TrendModel.Up,
            "DOWN" => TrendModel.Down,
            "SIDEWAYS" => TrendModel.Sideways,
            _ => null
        };

        if (trend == null)
            return Invalid("trend is not UP, DOWN or SIDEWAYS");

        DailyVwapDirectionModel? dailyVwapDirection = ReadText(reply, "daily_vwap_direction") switch {
            "RISING" => DailyVwapDirectionModel.Rising,
            "FALLING" => DailyVwapDirectionModel.Falling,
            "FLAT" => DailyVwapDirectionModel.Flat,
            _ => null
        };

        if (dailyVwapDirection == null)
            return Invalid("daily_vwap_direction is not RISING, FALLING or FLAT");

        double? trendConfidence = ReadConfidence(reply, "confidence");

        if (trendConfidence == null)
            return Invalid("confidence is not a number from 0 to 1");

        double? dailyVwapConfidence = ReadConfidence(reply, "daily_vwap_confidence");

        if (dailyVwapConfidence == null)
            return Invalid("daily_vwap_confidence is not a number from 0 to 1");

        string structure = ReadText(reply, "structure");

        if (string.IsNullOrWhiteSpace(structure))
            return Invalid("structure is missing");

        string reason = ReadText(reply, "reason");

        if (string.IsNullOrWhiteSpace(reason))
            return Invalid("reason is missing");

        return TrendAssessmentResultModel.Assessed(trend.Value, trendConfidence.Value, dailyVwapDirection.Value, dailyVwapConfidence.Value,
            structure, reason, modelName);
    }

    private static TrendAssessmentResultModel ReadUnreadable(JsonElement reply, string modelName) {
        string reason = ReadText(reply, "reason");

        if (string.IsNullOrWhiteSpace(reason))
            return Invalid("reason is missing");

        return TrendAssessmentResultModel.Unreadable(reason, modelName);
    }

    // The service explains a failure as {"error": {"code": "...", "message": "..."}}.
    private static TrendAssessmentResultModel ReadFailure(int httpStatus, JsonElement reply) {
        if (!reply.TryGetProperty("error", out JsonElement error) || error.ValueKind != JsonValueKind.Object)
            return TrendAssessmentResultModel.Unavailable($"The AI service answered HTTP {httpStatus}");

        return TrendAssessmentResultModel.Unavailable(
            $"The AI service answered HTTP {httpStatus} {ReadText(error, "code")}: {ReadText(error, "message")}");
    }

    private static TrendAssessmentResultModel Invalid(string problem) {
        return TrendAssessmentResultModel.Unavailable($"The AI service's answer is invalid: {problem}");
    }

    // Null unless the property is a JSON string.
    private static string ReadText(JsonElement parent, string name) {
        return parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    // Null unless the property is a JSON number from 0 to 1. A number written as text is refused.
    private static double? ReadConfidence(JsonElement parent, string name) {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            return null;

        double confidence = value.GetDouble();
        return confidence is >= 0.0 and <= 1.0 ? confidence : null;
    }
}
