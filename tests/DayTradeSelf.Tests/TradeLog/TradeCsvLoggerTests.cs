using System;
using System.IO;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.TradeLog {
    // What goes into the trade CSV: one row when a position opens, one when it closes. The close row
    // must report the entry's VWAP readings and measure R against the entry's risk distance.
    public class TradeCsvLoggerTests : IDisposable {
        private static readonly DateTime EntryTime = new(2026, 9, 22, 11, 0, 0);
        private static readonly DateTime CloseTime = new(2026, 9, 22, 13, 25, 0);

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "TradeCsvLoggerTests-" + Guid.NewGuid());
        private readonly TradeCsvLogger _logger;

        public TradeCsvLoggerTests() {
            _logger = new TradeCsvLogger(new TradeCsvFile(resetOnStart: true, _directory, "trades.csv"), "XAUUSD", "Minute5", _ => { });
        }

        public void Dispose() {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        [Fact]
        public void an_entry_row_records_the_fill_and_the_entry_readings() {
            _logger.RecordEntry(LongPlan(), Entry());

            Assert.Equal("7", Cell(row: 0, "编号"));
            Assert.Equal("VWAP", Cell(row: 0, "关键位"));
            Assert.Equal("L_Pin_1", Cell(row: 0, "信号"));
            Assert.Equal("ENTRY", Cell(row: 0, "备注"));
            Assert.Equal("多", Cell(row: 0, "多空"));
            Assert.Equal("2026-09-22 11:00:00", Cell(row: 0, "入场时间"));
            // The broker's stop (97.5) wins over the plan's (98), and sets the risk distance.
            Assert.Equal("97.5", Cell(row: 0, "止损价格"));
            Assert.Equal("2.5", Cell(row: 0, "风险价格距离"));
            Assert.Equal("99.5", Cell(row: 0, "DailyVWAP"));
            Assert.Equal("", Cell(row: 0, "平仓时间"));
        }

        [Fact]
        public void a_close_row_reuses_the_entry_readings_and_measures_r_against_the_entry_risk() {
            _logger.RecordEntry(LongPlan(), Entry());

            _logger.RecordClose(Close(closePrice: 104.0, netProfit: 200.0, PositionCloseReasonModel.TakeProfit));

            Assert.Equal("7-TP", Cell(row: 1, "编号"));
            Assert.Equal("盈利", Cell(row: 1, "最终结果"));
            Assert.Equal("TP", Cell(row: 1, "平仓原因"));
            Assert.Equal("2026-09-22 13:25:00", Cell(row: 1, "平仓时间"));
            Assert.Equal("99.5", Cell(row: 1, "DailyVWAP"));
            Assert.Equal("2", Cell(row: 1, "ResultR")); // (104 - 100) / 2
            Assert.Equal("10000", Cell(row: 1, "开仓账户权益"));
        }

        [Fact]
        public void a_close_without_its_entry_leaves_the_readings_blank_and_works_back_the_entry_equity() {
            // A position opened before a restart: its plan is gone, so there is nothing to report —
            // 0 would be a fabricated reading.
            _logger.RecordClose(Close(closePrice: 98.0, netProfit: -100.0, PositionCloseReasonModel.StopLoss));

            Assert.Equal("7-SL", Cell(row: 0, "编号"));
            Assert.Equal("亏损", Cell(row: 0, "最终结果"));
            Assert.Equal("", Cell(row: 0, "DailyVWAP"));
            Assert.Equal("", Cell(row: 0, "ResultR"));
            Assert.Equal("10000", Cell(row: 0, "开仓账户权益")); // 9900 after the close, plus the 100 lost
        }

        [Theory]
        [InlineData(PositionCloseReasonModel.StopLoss, "7-SL")]
        [InlineData(PositionCloseReasonModel.StopOut, "7-SO")]
        [InlineData(PositionCloseReasonModel.TakeProfit, "7-TP")]
        [InlineData(PositionCloseReasonModel.Closed, "7-CLOSE")]
        public void a_close_row_id_names_why_it_closed(PositionCloseReasonModel reason, string expectedId) {
            _logger.RecordClose(Close(closePrice: 100.0, netProfit: 0.0, reason));

            Assert.Equal(expectedId, Cell(row: 0, "编号"));
        }

        [Fact]
        public void a_position_is_forgotten_once_closed() {
            _logger.RecordEntry(LongPlan(), Entry());
            _logger.RecordClose(Close(closePrice: 104.0, netProfit: 200.0, PositionCloseReasonModel.TakeProfit));

            // A second close for the same ID (it cannot happen, but the map must not keep the plan).
            _logger.RecordClose(Close(closePrice: 104.0, netProfit: 200.0, PositionCloseReasonModel.TakeProfit));

            Assert.Equal("", Cell(row: 2, "DailyVWAP"));
        }

        private static OrderPlanModel LongPlan() {
            SignalModel signal = TestSignal.Long(close: 100.0, stopLoss: 98.0);
            signal.DailyVwap = 99.5;
            signal.WeeklyVwap = 99.0;

            return new OrderPlanModel {
                IsValid = true,
                Signal = signal,
                Direction = TradeDirectionModel.Long,
                EntryPrice = 100.0,
                StopPrice = 98.0,
                TakeProfitPrice = 104.0,
                RiskPrice = 2.0,
                AccountEquity = 10000.0
            };
        }

        private static PositionEntryModel Entry() {
            return new PositionEntryModel {
                PositionId = 7, EntryTime = EntryTime, EntryPrice = 100.0, StopLoss = 97.5, VolumeInUnits = 50.0, DealId = "70"
            };
        }

        private static PositionCloseModel Close(double closePrice, double netProfit, PositionCloseReasonModel reason) {
            return new PositionCloseModel {
                PositionId = 7,
                Label = "VWAPTradeAI-label_VWAP",
                Direction = TradeDirectionModel.Long,
                EntryTime = EntryTime,
                EntryPrice = 100.0,
                ClosePrice = closePrice,
                CloseTime = CloseTime,
                CloseReason = reason,
                VolumeInUnits = 50.0,
                NetProfit = netProfit,
                AccountEquity = 10000.0 + netProfit,
                DealId = "71"
            };
        }

        // The file's first line is the header; row 0 is the first line after it.
        private string Cell(int row, string columnName) {
            string[] lines = File.ReadAllLines(Path.Combine(_directory, "trades.csv"));
            int column = Array.IndexOf(lines[0].Split(','), columnName);
            return lines[row + 1].Split(',')[column];
        }
    }
}
