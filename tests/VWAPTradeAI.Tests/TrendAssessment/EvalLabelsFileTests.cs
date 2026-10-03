using System;
using System.IO;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.TrendAssessment {
    // labels.csv is read by evaluate.py in TrendAssessmentModel (docs/CORRECTING_THE_MODEL.md). A
    // picked picture is listed once, with its labels left for a person; what is there already stays.
    public class EvalLabelsFileTests : IDisposable {
        private const string Picture = "20260804-154000_XAUUSD_3b372edf.png";
        private const string LabelledRow =
            "20260930-220500_XAUUSD_7f3c2a9e.png,UP,RISING,\"Three swing lows step up, and each high clears the one before.\"";

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "EvalLabelsFileTests-" + Guid.NewGuid());
        private readonly EvalLabelsFile _labels;

        public EvalLabelsFileTests() {
            Directory.CreateDirectory(_directory);
            _labels = new EvalLabelsFile(_directory);
        }

        public void Dispose() {
            Directory.Delete(_directory, recursive: true);
        }

        private string Text() => File.ReadAllText(_labels.FilePath);

        private void WriteText(string text) => File.WriteAllText(_labels.FilePath, text);

        [Fact]
        public void a_new_file_starts_with_the_documented_header_and_the_picture_unlabelled() {
            _labels.EnsureListed(Picture);

            Assert.Equal(Path.Combine(_directory, "labels.csv"), _labels.FilePath);
            Assert.Equal("file,trend,daily_vwap_direction,reason\n" + Picture + ",,,\n", Text());
        }

        [Fact]
        public void a_picture_is_added_below_the_rows_already_there_which_stay_as_they_were() {
            WriteText("file,trend,daily_vwap_direction,reason\n" + LabelledRow + "\n");

            _labels.EnsureListed(Picture);

            Assert.Equal("file,trend,daily_vwap_direction,reason\n" + LabelledRow + "\n" + Picture + ",,,\n", Text());
        }

        [Fact]
        public void a_picture_already_listed_is_not_listed_again() {
            string labelled = "file,trend,daily_vwap_direction,reason\n" + Picture + ",DOWN,FALLING,\"Swing highs step down.\"\n";
            WriteText(labelled);

            _labels.EnsureListed(Picture);
            _labels.EnsureListed(Picture);

            Assert.Equal(labelled, Text());
        }

        [Fact]
        public void a_picture_listed_in_quotes_or_with_windows_line_breaks_counts_as_listed() {
            string labelled = "file,trend,daily_vwap_direction,reason\r\n\"" + Picture + "\",,,\r\n";
            WriteText(labelled);

            _labels.EnsureListed(Picture);

            Assert.Equal(labelled, Text());
        }

        [Fact]
        public void a_file_saved_without_a_final_line_break_gets_the_picture_on_a_line_of_its_own() {
            WriteText("file,trend,daily_vwap_direction,reason\n" + LabelledRow);

            _labels.EnsureListed(Picture);

            Assert.Equal("file,trend,daily_vwap_direction,reason\n" + LabelledRow + "\n" + Picture + ",,,\n", Text());
        }

        [Fact]
        public void an_empty_file_gets_the_header_first() {
            WriteText("");

            _labels.EnsureListed(Picture);

            Assert.Equal("file,trend,daily_vwap_direction,reason\n" + Picture + ",,,\n", Text());
        }
    }
}
