"""数据层：把 cTrader 回测报告 JSON 读成一张按报告名排序的 DataFrame。

每个报告读两项指标：
    win_rate   胜率 = tradeStatistics.winningTrades.all / totalTrades.all
    net_profit 盈利金额 = main.netProfit
"""

import json
from pathlib import Path

import pandas as pd

# DataFrame 的列（同时也是 read_report_stats 返回 dict 的键）
REPORT_COLUMNS = ["report", "net_profit", "win_rate", "total_trades", "starting_capital"]


def read_report_stats(report_path):
    """从单个报告 JSON 读出胜率、净利润、初始资金；不是回测报告（缺 main/tradeStatistics）就返回 None。"""
    report = _load_json(report_path)
    if report is None or "main" not in report or "tradeStatistics" not in report:
        return None

    main = report["main"]
    statistics = report["tradeStatistics"]
    total_trades = statistics.get("totalTrades", {}).get("all", 0) or 0
    winning_trades = statistics.get("winningTrades", {}).get("all", 0) or 0

    return {
        "report": report_path.stem,
        "net_profit": main.get("netProfit", 0.0),
        "win_rate": (winning_trades / total_trades * 100.0) if total_trades else 0.0,
        "total_trades": total_trades,
        "starting_capital": main.get("startingCapital", 0.0),
    }


def load_report_frame(output_dir):
    """扫描目录下所有回测报告 JSON，汇总成按报告名排序的 DataFrame。"""
    records = [
        stats
        for report_path in sorted(Path(output_dir).glob("*.json"))
        if (stats := read_report_stats(report_path)) is not None
    ]
    frame = pd.DataFrame(records, columns=REPORT_COLUMNS)
    return frame.sort_values("report").reset_index(drop=True)


def _load_json(report_path):
    """读并解析 JSON；文件读不了或不是合法 JSON/对象则返回 None。"""
    try:
        with open(report_path, encoding="utf-8") as report_file:
            report = json.load(report_file)
    except (json.JSONDecodeError, OSError):
        return None
    return report if isinstance(report, dict) else None
