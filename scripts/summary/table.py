"""导出层：把汇总 DataFrame 写成 final_summary_report.csv。

除“胜率%/盈利金额”来自报告 JSON（已在 DataFrame 里算好），其余列都是解析报告文件名得到的。
"""

import csv

from .naming import parse_report_name

CSV_COLUMNS = [
    "文件名",
    "起始日期",
    "结束日期",
    "周期",
    "止盈目标",
    "胜率%",
    "盈利金额",
    "盈利率%",
]


def write_summary_csv(frame, csv_path):
    """把汇总表写成 CSV。用 utf-8-sig（带 BOM），中文表头在 Excel 里能正确识别。"""
    with open(csv_path, "w", encoding="utf-8-sig", newline="") as csv_file:
        writer = csv.writer(csv_file)
        writer.writerow(CSV_COLUMNS)
        for row in frame.itertuples(index=False):
            writer.writerow(_build_row(row))


def _build_row(row):
    """一份报告 -> 一行 CSV。row 是 DataFrame 的一行（含 report / win_rate / net_profit / starting_capital）。"""
    name = parse_report_name(row.report)
    # 盈利率 = 盈利金额 / 初始资金 x 100
    roi = (row.net_profit / row.starting_capital * 100.0) if row.starting_capital else 0.0
    return [
        row.report,
        name.start_date,
        name.end_date,
        name.period,
        f"{name.take_profit}R" if name.take_profit else "",
        f"{row.win_rate:.0f}%",
        f"{row.net_profit}$",
        f"{roi:.2f}%",
    ]
