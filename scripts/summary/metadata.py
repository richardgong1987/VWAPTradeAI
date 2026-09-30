"""导出层：把每份回测报告的配置写成 metadata.json。

用途：CSV 便于人看，但不便于机器入库查询。metadata.json 以「回测报告 JSON 文件名」为 key，
value 里放解析出来的配置（种类/周期/止盈目标/起止日期），方便日后原样导入数据库按条件检索；
同时在 raw 里保留未加工的原始字段（含报告名与报告 JSON 的指标），做到既可查询又不丢信息。

所有配置字段都来自解析报告文件名（见 naming.parse_report_name），本模块不额外读盘。
"""

import json
from datetime import datetime

from .naming import parse_report_name

METADATA_NAME = "metadata.json"


def write_metadata_json(frame, json_path):
    """把汇总 DataFrame 写成 metadata.json：{ 报告文件名: {配置..., raw:{原始..}} }。"""
    metadata = {}
    for row in frame.itertuples(index=False):
        metadata[f"{row.report}.json"] = _build_entry(row)

    with open(json_path, "w", encoding="utf-8") as json_file:
        json.dump(metadata, json_file, ensure_ascii=False, indent=2)


def _build_entry(row):
    """一份报告 -> 一条 metadata：可入库的配置字段 + raw 原始字段。"""
    name = parse_report_name(row.report)
    return {
        "种类": name.symbol,
        "周期": name.period,
        "止盈目标": name.take_profit,
        "起始日期": _to_slash_date(name.start_date),
        "结束日期": _to_slash_date(name.end_date),
        "raw": {
            "report": row.report,
            "start_date": name.start_date,
            "end_date": name.end_date,
            "net_profit": row.net_profit,
            "win_rate": row.win_rate,
            "total_trades": row.total_trades,
            "starting_capital": row.starting_capital,
        },
    }


def _to_slash_date(compact_date):
    """把文件名里的紧凑日期 YYYYMMDD 转成 DD/MM/YYYY；无法解析时原样返回，避免中断导出。"""
    try:
        return datetime.strptime(compact_date, "%Y%m%d").strftime("%d/%m/%Y")
    except (ValueError, TypeError):
        return compact_date
