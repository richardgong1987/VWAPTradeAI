#!/usr/bin/env python3
"""命令行入口：把回测批次的 trading_reports_<时间>.zip 压缩包通过 HTTP 上传到后端。

压缩包由 backtest 的 archive_output_dir 生成（见 backtest/command.py），落在 ~/Documents 下，
文件名形如 trading_reports_20260723092729.zip，内含 metadata_20260723092729.json 等报告产物。

后端 POST /trd_trade_record/record/upload（multipart，字段名 file，允许匿名）会：
  1. 从文件名解析批次时间 20260723092729 作为 update_time；
  2. 读取包内 metadata_<时间>.json，key 作为 filename、value 作为 extra 入库；
  3. 留存整个 zip，供后台按批次时间下载。

后端按 (update_time, filename) 幂等 upsert，重复上传同一批次不会产生重复数据。

用法：

    python3 scripts/upload_reports.py                      # 默认扫 ~/Documents 下的 trading_reports_*.zip
    python3 scripts/upload_reports.py --dir <目录>
    python3 scripts/upload_reports.py --url <后端上传地址>
    REPORT_UPLOAD_URL=<后端上传地址> python3 scripts/upload_reports.py

只依赖标准库，无需额外安装。
"""

import argparse
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

DEFAULT_ZIP_DIR = Path.home() / "Documents"
DEFAULT_UPLOAD_URL = "http://127.0.0.1:9099/trd_trade_record/record/upload"
ZIP_GLOB = "trading_reports_*.zip"
MULTIPART_BOUNDARY = "----TrdReportUploadBoundary7MA4YWxkTrZu0gW"
REQUEST_TIMEOUT_SECONDS = 60


def parse_args(argv):
    parser = argparse.ArgumentParser(description="上传交易研究报告压缩包 trading_reports_<时间>.zip 到后端。")
    parser.add_argument(
        "--dir",
        default=str(DEFAULT_ZIP_DIR),
        help="压缩包所在目录（默认 ~/Documents，与 archive_output_dir 输出一致）。",
    )
    parser.add_argument(
        "--url",
        default=os.environ.get("REPORT_UPLOAD_URL", DEFAULT_UPLOAD_URL),
        help="后端上传地址（默认取环境变量 REPORT_UPLOAD_URL，否则本机 9099）。",
    )
    return parser.parse_args(argv)


def find_zip_files(zip_dir):
    """列出目录下全部 trading_reports_<时间>.zip，按文件名排序。"""
    return sorted(Path(zip_dir).expanduser().glob(ZIP_GLOB))


def build_multipart_body(filename, file_bytes):
    """把单个文件编码成 multipart/form-data 请求体，字段名固定为 file。"""
    head = (
        f"--{MULTIPART_BOUNDARY}\r\n"
        f'Content-Disposition: form-data; name="file"; filename="{filename}"\r\n'
        "Content-Type: application/zip\r\n\r\n"
    ).encode("utf-8")
    tail = f"\r\n--{MULTIPART_BOUNDARY}--\r\n".encode("utf-8")
    body = head + file_bytes + tail
    content_type = f"multipart/form-data; boundary={MULTIPART_BOUNDARY}"
    return body, content_type


def upload_zip(url, zip_file):
    """上传单个压缩包，返回后端响应的 data 字段（导入统计）。"""
    body, content_type = build_multipart_body(zip_file.name, zip_file.read_bytes())
    request = urllib.request.Request(url, data=body, headers={"Content-Type": content_type}, method="POST")
    with urllib.request.urlopen(request, timeout=REQUEST_TIMEOUT_SECONDS) as response:
        payload = json.loads(response.read().decode("utf-8"))
    result = payload.get("data", payload)
    print(
        f"已上传 {zip_file.name}："
        f"共 {result.get('imported')}，新增 {result.get('inserted')}，更新 {result.get('updated')}"
    )


def upload_report_archives(url, zip_dir):
    """扫描 zip_dir 下的 trading_reports_<时间>.zip 并逐个上传（找不到就跳过）。

    供命令行 main() 与批量回测收尾（backtest/runner.py）复用。上传失败会抛出
    urllib.error.URLError，由调用方决定如何处理（CLI 转成退出码，批量收尾则跳过不中断）。
    """
    zip_files = find_zip_files(zip_dir)
    if not zip_files:
        print(f"目录里没有 trading_reports_<时间>.zip：{zip_dir}")
        return
    for zip_file in zip_files:
        upload_zip(url, zip_file)


def main(argv=None):
    args = parse_args(argv)
    try:
        upload_report_archives(args.url, args.dir)
    except urllib.error.URLError as error:
        print(f"上传失败：{error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
