using System;
using System.Collections.Generic;

namespace cAlgo.Robots;

// The trade CSV's rows: one when a position opens, one when it closes. This class decides which facts
// go into a row; the columns are TradeCsvColumns', the file is TradeCsvFile's.
//
// The close row reports the entry's VWAP readings and measures R against the entry's risk distance,
// so each open position's entry plan is kept until it closes, then dropped (the map never grows). A
// position opened before a restart has no plan: its close row leaves those cells blank.
//
// Pattern: Observer (subscriber). RecordEntry and RecordClose are wired to OrderExecutor's
// PositionOpened and PositionClosed. It sees only plain models, so it is unit tested.
public class TradeCsvLogger {
    private readonly TradeCsvFile _file;
    private readonly string _symbolName;
    private readonly string _timeFrame;
    private readonly Action<string> _log;
    private readonly Dictionary<int, OrderPlanModel> _entryPlans = new();

    public TradeCsvLogger(TradeCsvFile file, string symbolName, string timeFrame, Action<string> log) {
        _file = file;
        _symbolName = symbolName;
        _timeFrame = timeFrame;
        _log = log;
    }

    public void RecordEntry(OrderPlanModel plan, PositionEntryModel position) {
        double stopPrice = position.StopLoss ?? plan.StopPrice;

        Append(new TradeRecordModel {
            Id = position.PositionId.ToString(),
            KeyLevel = plan.Signal.Level.Name,
            Signal = plan.Signal.Label,
            Comment = "ENTRY",
            Symbol = _symbolName,
            TimeFrame = _timeFrame,
            Side = SideText(plan.Direction),
            EntryTime = position.EntryTime,
            EntryPrice = position.EntryPrice,
            StopPrice = stopPrice,
            TakeProfitPrice = plan.TakeProfitPrice,
            RiskPrice = Math.Abs(position.EntryPrice - stopPrice),
            VolumeInUnits = position.VolumeInUnits,
            EntryAccountEquity = plan.AccountEquity,
            PositionId = position.PositionId.ToString(),
            DealId = position.DealId,
            EntryPlan = plan
        });

        _entryPlans[position.PositionId] = plan;
        _log($"CSV trade record added. Path: {_file.FilePath}");
    }

    public void RecordClose(PositionCloseModel position) {
        _entryPlans.Remove(position.PositionId, out OrderPlanModel entryPlan);

        string finalResult = position.NetProfit >= 0.0 ? "盈利" : "亏损";
        string closeReason = CloseReasonCode(position.CloseReason);

        var record = new TradeRecordModel {
            Id = $"{position.PositionId}-{closeReason}",
            Signal = "close",
            Comment = finalResult,
            FinalResult = finalResult,
            Symbol = _symbolName,
            TimeFrame = _timeFrame,
            Side = SideText(position.Direction),
            EntryTime = position.EntryTime,
            EntryPrice = position.EntryPrice,
            ClosePrice = position.ClosePrice,
            VolumeInUnits = position.VolumeInUnits,
            CloseReason = closeReason,
            EntryAccountEquity = EntryEquity(position, entryPlan),
            CloseAccountEquity = position.AccountEquity,
            ProfitLoss = position.NetProfit,
            ResultR = TradeResultR.Calculate(position.Direction, position.EntryPrice, position.ClosePrice,
                entryPlan?.RiskPrice ?? double.NaN),
            CloseTime = position.CloseTime,
            PositionId = position.PositionId.ToString(),
            DealId = position.DealId,
            EntryPlan = entryPlan
        };

        Append(record);
        _log($"CSV close record added. Id: {record.Id}, ProfitLoss: {position.NetProfit}");
    }

    private void Append(TradeRecordModel record) {
        _file.AppendLine(TradeCsvColumns.ToCsvLine(record));
    }

    private static string SideText(TradeDirectionModel direction) {
        return direction == TradeDirectionModel.Long ? "多" : "空";
    }

    // Without the entry plan (a position opened before a restart), the entry equity is worked back
    // from the close.
    private static double EntryEquity(PositionCloseModel position, OrderPlanModel entryPlan) {
        double recordedAtEntry = entryPlan?.AccountEquity ?? 0.0;

        if (recordedAtEntry > 0.0 || position.AccountEquity <= 0.0)
            return recordedAtEntry;

        return position.AccountEquity - position.NetProfit;
    }

    private static string CloseReasonCode(PositionCloseReasonModel reason) {
        switch (reason) {
            case PositionCloseReasonModel.StopLoss:
                return "SL";
            case PositionCloseReasonModel.StopOut:
                return "SO";
            case PositionCloseReasonModel.TakeProfit:
                return "TP";
            default:
                return "CLOSE";
        }
    }
}
