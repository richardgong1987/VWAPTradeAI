#!/usr/bin/env python3
"""命令行入口：汇总输出目录里的回测报告 JSON，生成柱状图 final_report.png
和汇总表 final_summary_report.csv。

run_conditions.py 在全部回测结束后会调用一次 summary.update_final_report()；这个脚本
用来在不重跑回测的情况下手动重新生成汇总产物：

    python3 scripts/report_summary.py                 # 默认扫 ~/Documents
    python3 scripts/report_summary.py --dir <目录>

实现按职责拆在 summary/ 包里（metrics 读数 / naming 解析文件名 / chart 出图 / table 出表 / report 编排）。
"""

import argparse
import sys
from pathlib import Path

from summary import update_final_report


def parse_args(argv):
    parser = argparse.ArgumentParser(description="汇总回测报告 JSON，生成 final_report.png。")
    parser.add_argument(
        "--dir",
        default=str(Path.home() / "Documents" / "trading_reports"),
        help="报告 JSON 所在目录（默认 ~/Documents/trading_reports，与回测输出一致）。",
    )
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(argv)
    outputs = update_final_report(args.dir)

    if outputs is None:
        print(f"目录里没有可用的回测报告 JSON：{args.dir}")
        return 0

    print(f"已生成汇总图：{outputs.chart_path}")
    print(f"已生成汇总表：{outputs.csv_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
