"""解析回测报告文件名，还原出各字段。

文件名格式（见 backtest/plan.py 里 ConditionRow.report_file_name 的拼接）：

    <symbol>-<period>-<take_profit>-<start_YYYYMMDD>-<end_YYYYMMDD>

例：XAUUSD-m5-2-20240101-20240131

除“胜率/盈利金额”要从报告 JSON 计算外，其余字段解析文件名即可得到。
"""

from collections import namedtuple

ReportName = namedtuple("ReportName", ["symbol", "period", "take_profit", "start_date", "end_date"])

# 固定尾部字段个数：period, take_profit, start, end（symbol 之外的 4 个）
_TRAILING_FIELDS = 4


def parse_report_name(stem):
    """把报告文件名（不含扩展名）解析成 ReportName。

    从右往左取字段，这样即使 symbol 里带连字符也能正确切分。文件名不符合预期格式（字段不足）
    时，只填 symbol=原文件名，其余留空——保证导出表格不因个别异常文件名而中断。
    """
    parts = stem.split("-")
    if len(parts) < _TRAILING_FIELDS + 1:
        return ReportName(stem, "", "", "", "")

    return ReportName(
        symbol="-".join(parts[:-_TRAILING_FIELDS]),
        period=parts[-4],
        take_profit=parts[-3],
        start_date=parts[-2],
        end_date=parts[-1],
    )
