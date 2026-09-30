using System;
using System.Collections.Generic;
using System.Globalization;

namespace cAlgo.Robots;

// 交易 CSV 的全部列，一列一行：列名 + 怎么从一条记录取出它的值。
//
// 表头和数据行都由这同一张表生成，所以「表头 31 列、数据行 30 列」这种错位在结构上就不可能
// 发生；加一列也只需要在这里加一行。
//
// Changing the columns needs no migration: at start-up, a file written with a different header is
// moved aside and a fresh one started (see TradeCsvFile).
//
// Pattern: Strategy, as a table. Each column carries its own way of reading a record (Text, Raw,
// Positive, Reading), so the header and the row are both produced by walking the same list.
public static class TradeCsvColumns {
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly IReadOnlyList<TradeCsvColumn> Columns = new[] {
        // ── 交易本身 ──────────────────────────────────────────────────────────
        Text("编号", r => r.Id), Text("关键位", r => r.KeyLevel), Text("信号", r => r.Signal), Text("备注", r => r.Comment),
        Text("交易品种", r => r.Symbol), Text("时间周期", r => r.TimeFrame),
        Text("入场时间", r => r.EntryTime.ToString(TimeFormat, CultureInfo.InvariantCulture)), Raw("入场价格", r => r.EntryPrice),
        Positive("平仓价格", r => r.ClosePrice), Raw("止损价格", r => r.StopPrice), Raw("止盈价格", r => r.TakeProfitPrice),
        Raw("风险价格距离", r => r.RiskPrice), Raw("下单数量", r => r.VolumeInUnits), Text("平仓原因", r => r.CloseReason),
        Positive("开仓账户权益", r => r.EntryAccountEquity), Positive("平仓账户权益", r => r.CloseAccountEquity), Raw("平仓盈亏", r => r.ProfitLoss),
        Text("平仓时间", r => r.CloseTime?.ToString(TimeFormat, CultureInfo.InvariantCulture)), Text("持仓ID", r => r.PositionId),
        Text("成交ID", r => r.DealId),

        // ── 开仓当时的 VWAP 读数与结果 ───────────────────────────────────────
        Text("多空", r => r.Side), Reading("DailyVWAP", r => EntrySignal(r)?.DailyVwap),
        Reading("WeeklyVWAP", r => EntrySignal(r)?.WeeklyVwap), Text("最终结果", r => r.FinalResult), Reading("ResultR", r => r.ResultR)
    };

    public static int Count => Columns.Count;

    public static string Header => Join(column => column.Name);

    public static string ToCsvLine(TradeRecordModel record) => Join(column => column.Read(record));

    private static string Join(Func<TradeCsvColumn, string> select) {
        string[] cells = new string[Columns.Count];

        for (int i = 0; i < Columns.Count; i++) {
            cells[i] = CsvCell.Escape(select(Columns[i]));
        }

        return string.Join(",", cells);
    }

    // Rows read the entry's signal through its plan. A close row for a position opened before a
    // restart has no plan, so its VWAP columns are blank.
    private static SignalModel EntrySignal(TradeRecordModel record) => record.EntryPlan?.Signal;

    // ── 列的几种取值方式 ─────────────────────────────────────────────────────
    private static TradeCsvColumn Text(string name, Func<TradeRecordModel, string> read) =>
        new(name, record => read(record) ?? "");

    // 价格、盈亏这类：原样输出，0 和负数都是有意义的值。
    private static TradeCsvColumn Raw(string name, Func<TradeRecordModel, double> read) =>
        new(name, record => read(record).ToString(CultureInfo.InvariantCulture));

    // 只在开仓行或只在平仓行才有的正数（平仓价、账户权益）：没有就留空。
    private static TradeCsvColumn Positive(string name, Func<TradeRecordModel, double> read) =>
        new(name, record => read(record) is var value && value > 0.0 ? value.ToString(CultureInfo.InvariantCulture) : "");

    // Readings can be negative (ResultR on a loss), so the "≤ 0 is blank" rule does not apply.
    // Only a value that could not be had (NaN, infinity, no plan) is blank — never 0, a real reading.
    private static TradeCsvColumn Reading(string name, Func<TradeRecordModel, double?> read) =>
        new(name, record => CsvCell.Number(read(record)));

    private sealed class TradeCsvColumn {
        public TradeCsvColumn(string name, Func<TradeRecordModel, string> read) {
            Name = name;
            Read = read;
        }

        public string Name { get; }

        public Func<TradeRecordModel, string> Read { get; }
    }
}
