using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.TrendAssessment {
    // The evaluation data set: the exact picture the AI was sent, and what came of it, side by side.
    // A reset at start-up clears the earlier runs' records, and only those. A recording picked for
    // labelling moves to the evaluation folder as a pair, or not at all.
    public class TrendAssessmentRecorderTests : IDisposable {
        private const string RequestId = "7f3c2a9e5d414b0fa1c6e2b8d4a90c13";
        private const string RecordingName = "20260918-130000_XAUUSD_7f3c2a9e";
        private static readonly DateTime SignalBarTime = new(2026, 9, 18, 13, 0, 0, DateTimeKind.Utc);
        private static readonly byte[] ChartPng = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "TrendAssessmentRecorderTests-" + Guid.NewGuid());
        private readonly string _evalDirectory = Path.Combine(Path.GetTempPath(), "TrendAssessmentRecorderTests-eval-" + Guid.NewGuid());

        public void Dispose() {
            foreach (string directory in new[] { _directory, _evalDirectory })
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
        }

        private TrendAssessmentRecorder Recorder(bool resetOnStart = false) => new(resetOnStart, _directory, _evalDirectory, "XAUUSD");

        private string[] FileNames() => FileNamesIn(_directory);

        private static string[] FileNamesIn(string directory) => Directory.Exists(directory)
            ? Directory.GetFiles(directory).Select(Path.GetFileName).OrderBy(name => name).ToArray()
            : Array.Empty<string>();

        private static TrendAssessmentRecordModel Passed() => Record(TestAssessment.Of(TrendModel.Up, DailyVwapDirectionModel.Rising));

        private static TrendAssessmentRecordModel Record(TrendAssessmentResultModel assessment, string gateRejectReason = null,
            bool isOrderPlaced = true) {
            return new TrendAssessmentRecordModel {
                RequestId = RequestId,
                Signal = TestSignal.Long(close: 100.0, stopLoss: 98.0), // bar time 2026-09-18 13:00:00
                Assessment = assessment,
                GateRejectReason = gateRejectReason,
                IsOrderPlaced = isOrderPlaced,
                ElapsedMs = 8120
            };
        }

        private JsonElement SavedJson() {
            string path = Assert.Single(Directory.GetFiles(_directory, "*.json"));
            return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        }

        [Fact]
        public void the_folder_is_trendassessment_under_documents() {
            Assert.Equal(Path.Combine("/docs", "TrendAssessment"), TrendAssessmentRecorder.DirectoryIn("/docs"));
        }

        [Fact]
        public void the_picture_is_saved_exactly_as_sent_with_a_json_file_beside_it() {
            string picturePath = Recorder().Save(Record(TestAssessment.Of(TrendModel.Up, DailyVwapDirectionModel.Rising)), ChartPng);

            Assert.Equal(Path.Combine(_directory, "20260918-130000_XAUUSD_7f3c2a9e.png"), picturePath);
            Assert.Equal(ChartPng, File.ReadAllBytes(picturePath));
            Assert.True(File.Exists(Path.Combine(_directory, "20260918-130000_XAUUSD_7f3c2a9e.json")));
        }

        [Fact]
        public void a_passed_assessment_records_the_signal_the_judgement_and_the_order() {
            Recorder().Save(Record(TestAssessment.Of(TrendModel.Up, DailyVwapDirectionModel.Rising)), ChartPng);

            JsonElement json = SavedJson();
            Assert.Equal(RequestId, json.GetProperty("request_id").GetString());
            Assert.Equal("XAUUSD", json.GetProperty("symbol").GetString());
            Assert.Equal("Buy", json.GetProperty("signal_side").GetString());
            Assert.Equal("L_Pin_1", json.GetProperty("signal_label").GetString());
            Assert.Equal("2026-09-18T13:00:00", json.GetProperty("signal_bar_time").GetString());
            Assert.Equal("OK", json.GetProperty("outcome").GetString());
            Assert.Equal("UP", json.GetProperty("trend").GetString());
            Assert.Equal(0.91, json.GetProperty("confidence").GetDouble());
            Assert.Equal("RISING", json.GetProperty("daily_vwap_direction").GetString());
            Assert.Equal(0.88, json.GetProperty("daily_vwap_confidence").GetDouble());
            Assert.Equal("gemma3:27b", json.GetProperty("model").GetString());
            Assert.Equal("PASS", json.GetProperty("gate").GetString());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("gate_reject_reason").ValueKind);
            Assert.True(json.GetProperty("order_placed").GetBoolean());
            Assert.Equal(8120, json.GetProperty("elapsed_ms").GetInt64());
        }

        [Fact]
        public void a_failed_assessment_records_the_reason_and_leaves_the_judgement_null() {
            Recorder().Save(Record(TestAssessment.Unavailable(), gateRejectReason: "Assessment unavailable: timed out", isOrderPlaced: false),
                ChartPng);

            JsonElement json = SavedJson();
            Assert.Equal("UNAVAILABLE", json.GetProperty("outcome").GetString());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("trend").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("confidence").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("model").ValueKind);
            Assert.Equal("Local assessment request timed out after 30 seconds", json.GetProperty("reason").GetString());
            Assert.Equal("REJECT", json.GetProperty("gate").GetString());
            Assert.Equal("Assessment unavailable: timed out", json.GetProperty("gate_reject_reason").GetString());
            Assert.False(json.GetProperty("order_placed").GetBoolean());
        }

        [Fact]
        public void an_unreadable_chart_is_recorded_as_unreadable() {
            Recorder().Save(Record(TestAssessment.Unreadable(), gateRejectReason: "Chart unreadable", isOrderPlaced: false), ChartPng);

            Assert.Equal("UNREADABLE", SavedJson().GetProperty("outcome").GetString());
        }

        [Fact]
        public void without_a_reset_a_restart_keeps_the_earlier_records() {
            Recorder().Save(Record(TestAssessment.Of(TrendModel.Up, DailyVwapDirectionModel.Rising)), ChartPng);
            string[] before = FileNames();

            Recorder(); // a restart of the cBot

            Assert.Equal(before, FileNames());
        }

        [Fact]
        public void reset_on_start_deletes_the_earlier_runs_pictures_and_json_files() {
            Recorder().Save(Record(TestAssessment.Of(TrendModel.Up, DailyVwapDirectionModel.Rising)), ChartPng);

            Recorder(resetOnStart: true);

            Assert.Empty(FileNames());
        }

        [Fact]
        public void reset_on_start_leaves_files_the_recorder_did_not_write() {
            Recorder().Save(Record(TestAssessment.Of(TrendModel.Up, DailyVwapDirectionModel.Rising)), ChartPng);
            File.WriteAllText(Path.Combine(_directory, "notes.txt"), "");

            Recorder(resetOnStart: true);

            Assert.Equal(new[] { "notes.txt" }, FileNames());
        }

        [Fact]
        public void the_evaluation_folder_is_trendassessmenteval_under_documents() {
            Assert.Equal(Path.Combine("/docs", "TrendAssessmentEval"), TrendAssessmentRecorder.EvalDirectoryIn("/docs"));
        }

        [Fact]
        public void a_picked_signal_moves_its_picture_and_json_to_the_evaluation_folder() {
            TrendAssessmentRecorder recorder = Recorder();
            recorder.Save(Passed(), ChartPng);

            string picturePath = recorder.MoveToEvalSet(SignalBarTime);

            Assert.Equal(Path.Combine(_evalDirectory, RecordingName + ".png"), picturePath);
            Assert.Equal(ChartPng, File.ReadAllBytes(picturePath));
            Assert.Equal(new[] { RecordingName + ".json", RecordingName + ".png" }, FileNamesIn(_evalDirectory));
            Assert.Empty(FileNames());
        }

        [Fact]
        public void a_signal_recorded_by_an_earlier_run_cannot_be_moved() {
            Recorder().Save(Passed(), ChartPng);
            TrendAssessmentRecorder restarted = Recorder();

            Assert.Throws<FileNotFoundException>(() => restarted.MoveToEvalSet(SignalBarTime));
            Assert.Equal(new[] { RecordingName + ".json", RecordingName + ".png" }, FileNames());
        }

        [Fact]
        public void a_bar_without_a_signal_moves_nothing() {
            TrendAssessmentRecorder recorder = Recorder();
            recorder.Save(Passed(), ChartPng);

            Assert.Throws<FileNotFoundException>(() => recorder.MoveToEvalSet(SignalBarTime.AddMinutes(5)));
            Assert.Empty(FileNamesIn(_evalDirectory));
        }

        [Fact]
        public void picking_the_same_signal_twice_says_it_is_already_there() {
            TrendAssessmentRecorder recorder = Recorder();
            recorder.Save(Passed(), ChartPng);
            recorder.MoveToEvalSet(SignalBarTime);

            IOException error = Assert.Throws<IOException>(() => recorder.MoveToEvalSet(SignalBarTime));

            Assert.StartsWith("Already in the evaluation set", error.Message);
        }

        [Fact]
        public void a_file_of_the_same_name_in_the_evaluation_folder_is_never_overwritten_and_neither_file_moves() {
            TrendAssessmentRecorder recorder = Recorder();
            recorder.Save(Passed(), ChartPng);
            Directory.CreateDirectory(_evalDirectory);
            File.WriteAllText(Path.Combine(_evalDirectory, RecordingName + ".json"), "labelled by hand");

            Assert.Throws<IOException>(() => recorder.MoveToEvalSet(SignalBarTime));

            Assert.Equal("labelled by hand", File.ReadAllText(Path.Combine(_evalDirectory, RecordingName + ".json")));
            Assert.Equal(new[] { RecordingName + ".json", RecordingName + ".png" }, FileNames());
        }

        [Fact]
        public void a_recording_missing_its_json_leaves_the_picture_where_it_was() {
            TrendAssessmentRecorder recorder = Recorder();
            recorder.Save(Passed(), ChartPng);
            File.Delete(Path.Combine(_directory, RecordingName + ".json"));

            Assert.Throws<FileNotFoundException>(() => recorder.MoveToEvalSet(SignalBarTime));

            Assert.Equal(new[] { RecordingName + ".png" }, FileNames());
            Assert.Empty(FileNamesIn(_evalDirectory));
        }
    }
}
