"""编排层：扫描目录里的报告，生成汇总图 final_report.png 和汇总表 final_summary_report.csv。"""

from collections import namedtuple
from datetime import datetime
from pathlib import Path

from .chart import render_report_chart
from .metadata import METADATA_NAME, write_metadata_json
from .metrics import load_report_frame
from .table import write_summary_csv

IMAGE_NAME = "final_report.png"
CSV_NAME = "final_summary_report.csv"

# 一次汇总产出的三个文件：柱状图 + CSV 表 + 可入库的 metadata.json；
# timestamp 是三个文件共用的 YYYYMMDDHHmmss，供打包时给 zip 取同款后缀（见 archive_output_dir）。
SummaryOutputs = namedtuple("SummaryOutputs", ["chart_path", "csv_path", "metadata_path", "timestamp"])


def update_final_report(output_dir, image_path=None):
    """扫描目录里的报告，生成汇总图和汇总表。

    无可用报告时跳过并返回 None；否则返回 SummaryOutputs(chart_path, csv_path, metadata_path)。
    三个产物文件名都带同一个 YYYYMMDDHHmmss 时间戳（如 final_summary_report_20260524120502.csv），
    这样每次批量回测的结果各自留档、不再互相覆盖。image_path 显式给出时按原样使用（不加时间戳）。
    """
    frame = load_report_frame(output_dir)
    if frame.empty:
        return None

    output_dir = Path(output_dir)
    timestamp = datetime.now().strftime("%Y%m%d%H%M%S")
    chart_path = Path(image_path) if image_path else output_dir / _with_timestamp(IMAGE_NAME, timestamp)
    csv_path = output_dir / _with_timestamp(CSV_NAME, timestamp)
    metadata_path = output_dir / _with_timestamp(METADATA_NAME, timestamp)

    render_report_chart(frame, chart_path)
    write_summary_csv(frame, csv_path)
    write_metadata_json(frame, metadata_path)
    return SummaryOutputs(
        chart_path=chart_path, csv_path=csv_path, metadata_path=metadata_path, timestamp=timestamp
    )


def _with_timestamp(file_name, timestamp):
    """在扩展名前插入时间戳：final_report.png -> final_report_20260524120502.png。"""
    name = Path(file_name)
    return f"{name.stem}_{timestamp}{name.suffix}"
