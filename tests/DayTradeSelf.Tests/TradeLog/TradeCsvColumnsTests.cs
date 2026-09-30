using cAlgo.Robots;
using Xunit;

namespace DayTradeSelf.Tests.TradeLog {
    // The header and every row come from the same table. These tests pin the schema's shape, and
    // that every row the logger writes is as wide as the header — a row one column short silently
    // shifts every value after it.
    public class TradeCsvColumnsTests {
        [Fact]
        public void the_header_has_one_name_per_column() {
            Assert.Equal(TradeCsvColumns.Count, TradeCsvColumns.Header.Split(',').Length);
        }

        [Fact]
        public void the_schema_carries_the_entry_readings_and_the_result() {
            string[] columns = TradeCsvColumns.Header.Split(',');

            Assert.Contains("入场时间", columns);
            Assert.Contains("多空", columns);
            Assert.Contains("DailyVWAP", columns);
            Assert.Contains("WeeklyVWAP", columns);
            Assert.Contains("最终结果", columns);
            Assert.Contains("ResultR", columns);
        }

        [Fact]
        public void no_column_name_is_repeated() {
            string[] columns = TradeCsvColumns.Header.Split(',');

            Assert.Equal(columns.Length, new System.Collections.Generic.HashSet<string>(columns).Count);
        }

        [Fact]
        public void a_row_is_as_wide_as_the_header() {
            string line = TradeCsvColumns.ToCsvLine(new TradeRecordModel());

            Assert.Equal(TradeCsvColumns.Count, line.Split(',').Length);
        }

        [Fact]
        public void a_row_reads_the_vwap_readings_through_the_entry_plan() {
            SignalModel signal = new() { DailyVwap = 104.0, WeeklyVwap = 100.5 };
            TradeRecordModel record = new() { EntryPlan = new OrderPlanModel { Signal = signal } };

            string[] cells = TradeCsvColumns.ToCsvLine(record).Split(',');

            Assert.Equal("104", Cell(cells, "DailyVWAP"));
            Assert.Equal("100.5", Cell(cells, "WeeklyVWAP"));
        }

        [Fact]
        public void a_row_without_an_entry_plan_leaves_the_readings_blank() {
            // 0 would be a fabricated reading.
            string[] cells = TradeCsvColumns.ToCsvLine(new TradeRecordModel()).Split(',');

            Assert.Equal("", Cell(cells, "DailyVWAP"));
            Assert.Equal("", Cell(cells, "WeeklyVWAP"));
            Assert.Equal("", Cell(cells, "ResultR"));
        }

        private static string Cell(string[] cells, string columnName) =>
            cells[System.Array.IndexOf(TradeCsvColumns.Header.Split(','), columnName)];
    }
}
