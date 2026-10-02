using System.Globalization;
using System.IO;
using System.Text.Json;

// cAlgo.API has a File type of its own, which clashes with System.IO.File.
using IoFile = System.IO.File;

namespace cAlgo.Robots;

// Keeps the AI's input and what came of it, for evaluating the model later: for every assessment,
// the exact PNG that was sent and a JSON file beside it.
//
//   20260930-220500_XAUUSD_7f3c2a9e.png
//   20260930-220500_XAUUSD_7f3c2a9e.json
//
// This is evaluation data, so it only ever grows: nothing here is cleared at start-up. It is a
// separate thing from the numbered pictures of opened trades (ChartshotFolder), which are taken
// after the entry as a trade record.
//
// Pure: no cAlgo dependency, unit tested.
public class TrendAssessmentRecorder {
    private const string FolderName = "TrendAssessment";

    private static readonly JsonWriterOptions Indented = new() { Indented = true };

    private readonly string _symbol;

    public TrendAssessmentRecorder(string directoryPath, string symbol) {
        DirectoryPath = directoryPath;
        _symbol = symbol;
        Directory.CreateDirectory(directoryPath);
    }

    public static string DirectoryIn(string documentsPath) {
        return Path.Combine(documentsPath, FolderName);
    }

    public string DirectoryPath { get; }

    // Writes both files and returns the picture's path.
    public string Save(TrendAssessmentRecordModel record, byte[] chartPng) {
        // The bar time orders the files; the request ID keeps two instances on one symbol apart.
        string name = string.Format(CultureInfo.InvariantCulture, "{0:yyyyMMdd-HHmmss}_{1}_{2}", record.Signal.BarTime, _symbol,
            record.RequestId.Substring(0, 8));
        string picturePath = Path.Combine(DirectoryPath, name + ".png");

        IoFile.WriteAllBytes(picturePath, chartPng);
        IoFile.WriteAllBytes(Path.Combine(DirectoryPath, name + ".json"), ToJson(record));
        return picturePath;
    }

    private byte[] ToJson(TrendAssessmentRecordModel record) {
        SignalModel signal = record.Signal;
        TrendAssessmentResultModel assessment = record.Assessment;

        using var stream = new MemoryStream();

        using (var json = new Utf8JsonWriter(stream, Indented)) {
            json.WriteStartObject();
            json.WriteString("request_id", record.RequestId);
            json.WriteString("symbol", _symbol);
            json.WriteString("signal_side", signal.Level.Side.ToString());
            json.WriteString("signal_label", signal.Label);
            json.WriteString("signal_bar_time", signal.BarTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
            // OK and UNREADABLE are the service's own status words; UNAVAILABLE is when it gave none.
            json.WriteString("outcome", assessment.Outcome == TrendAssessmentOutcomeModel.Assessed ? "OK" : Upper(assessment.Outcome.ToString()));
            json.WriteString("trend", Upper(assessment.Trend?.ToString()));
            WriteNumber(json, "confidence", assessment.TrendConfidence);
            json.WriteString("daily_vwap_direction", Upper(assessment.DailyVwapDirection?.ToString()));
            WriteNumber(json, "daily_vwap_confidence", assessment.DailyVwapConfidence);
            json.WriteString("structure", assessment.Structure);
            json.WriteString("reason", assessment.Reason);
            json.WriteString("model", assessment.ModelName);
            json.WriteString("gate", record.GateRejectReason == null ? "PASS" : "REJECT");
            json.WriteString("gate_reject_reason", record.GateRejectReason);
            json.WriteBoolean("order_placed", record.IsOrderPlaced);
            json.WriteString("order_reject_reason", record.OrderRejectReason);
            json.WriteNumber("elapsed_ms", record.ElapsedMs);
            json.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteNumber(Utf8JsonWriter json, string name, double? value) {
        if (value == null)
            json.WriteNull(name);
        else
            json.WriteNumber(name, value.Value);
    }

    private static string Upper(string text) {
        return text?.ToUpperInvariant();
    }
}
