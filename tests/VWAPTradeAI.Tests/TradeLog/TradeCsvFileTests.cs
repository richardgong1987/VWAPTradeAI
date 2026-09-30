using System;
using System.IO;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.TradeLog {
    // How the trade CSV behaves as a file across runs. Demo and live files accumulate trades, so
    // what matters most is that an existing file is never overwritten when its columns differ.
    public class TradeCsvFileTests : IDisposable {
        private const string FileName = "trades.csv";

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "TradeCsvFileTests-" + Guid.NewGuid());

        private string FilePath => Path.Combine(_directory, FileName);

        public void Dispose() {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private TradeCsvFile Open(bool resetOnStart = false) => new(resetOnStart, _directory, FileName);

        [Fact]
        public void a_new_file_starts_with_the_current_header() {
            TradeCsvFile file = Open();

            Assert.Equal(new[] { TradeCsvColumns.Header }, File.ReadAllLines(file.FilePath));
            Assert.Null(file.ArchivedFilePath);
        }

        [Fact]
        public void a_file_with_the_current_header_keeps_its_rows_and_is_appended_to() {
            Open().AppendLine("first run");

            TradeCsvFile second = Open();
            second.AppendLine("second run");

            Assert.Equal(new[] { TradeCsvColumns.Header, "first run", "second run" }, File.ReadAllLines(FilePath));
            Assert.Null(second.ArchivedFilePath);
        }

        [Fact]
        public void a_file_with_other_columns_is_moved_aside_intact_and_a_fresh_one_started() {
            Directory.CreateDirectory(_directory);
            File.WriteAllLines(FilePath, new[] { "old,header", "1,2" });

            TradeCsvFile file = Open();

            Assert.Equal(new[] { TradeCsvColumns.Header }, File.ReadAllLines(FilePath));
            Assert.NotNull(file.ArchivedFilePath);
            Assert.Equal(_directory, Path.GetDirectoryName(file.ArchivedFilePath));
            Assert.StartsWith("trades.old-", Path.GetFileName(file.ArchivedFilePath));
            Assert.Equal(new[] { "old,header", "1,2" }, File.ReadAllLines(file.ArchivedFilePath));
        }

        [Fact]
        public void reset_on_start_empties_the_file_without_archiving_it() {
            // Backtests reset every run; archiving each one would only pile up copies.
            Open().AppendLine("previous run");

            TradeCsvFile file = Open(resetOnStart: true);

            Assert.Equal(new[] { TradeCsvColumns.Header }, File.ReadAllLines(FilePath));
            Assert.Null(file.ArchivedFilePath);
            Assert.Single(Directory.GetFiles(_directory));
        }

        [Theory]
        [InlineData(true, false, "trading_reports")]
        [InlineData(false, false, "simulate_trading_reports")]
        [InlineData(false, true, "release_trading_reports")]
        public void each_running_mode_writes_to_its_own_folder(bool isBacktesting, bool isLiveAccount, string folder) {
            Assert.Equal(Path.Combine("/docs", folder), TradeCsvFile.ReportsDirectory("/docs", isBacktesting, isLiveAccount));
        }
    }
}
