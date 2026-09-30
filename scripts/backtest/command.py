"""把一条回测任务翻译成 cTrader CLI 的 backtest 命令。

重要：用的是 cTrader CLI 的 `backtest` 子命令（历史回测，跑完区间自动结束），不是 `run`
（实时/前向运行，会一直连着实盘账户）。环境配置 + 任务自带的 cBot 参数拼成完整命令行。
"""

import shutil
from datetime import datetime
from pathlib import Path

# 所有报告统一落在「我的文档」下的 trading_reports 子目录：回测报告 --report-json 以及批量结束后的
# 汇总产物都在这里，方便集中留档、不再把 ~/Documents 根目录弄乱。
CBOT_OUTPUT_DIR = Path.home() / "Documents" / "trading_reports"


def reset_output_dir():
    """删除并重建报告输出目录：先清掉上次批量的残留文件，再建空目录接收本次生成的数据。

    同时删掉上次批量留在上级目录（~/Documents）的对应 zip 存档（trading_reports_<ts>.zip），
    避免历次存档在 ~/Documents 里越堆越多；这些 zip 由 archive_output_dir 生成，与本目录一一对应。

    只在批量回测开始前调用（run_tasks）。report_summary.py 复用已有报告，不应调用它，否则会把
    要汇总的 JSON 一并删掉。
    """
    if CBOT_OUTPUT_DIR.exists():
        shutil.rmtree(CBOT_OUTPUT_DIR)
    for archive_path in CBOT_OUTPUT_DIR.parent.glob(f"{CBOT_OUTPUT_DIR.name}_*.zip"):
        archive_path.unlink()
    CBOT_OUTPUT_DIR.mkdir(parents=True, exist_ok=True)


def archive_output_dir(timestamp=None):
    """把报告目录打包成带时间戳的 zip 存档，供以后使用；返回 zip 路径。

    zip 放在报告目录的上级（~/Documents），刻意不放进 trading_reports 内部，否则下次批量回测
    reset_output_dir 会把它一并删掉。带时间戳使多次批量的存档可以并存、互不覆盖。

    timestamp 传汇总产物用的那个 YYYYMMDDHHmmss（来自 update_final_report），让 zip 后缀与
    final_report_<ts>.png 完全一致：trading_reports_<ts>.zip。没有汇总产物时回落到当前时间。
    """
    timestamp = timestamp or datetime.now().strftime("%Y%m%d%H%M%S")
    base_name = CBOT_OUTPUT_DIR.parent / f"{CBOT_OUTPUT_DIR.name}_{timestamp}"
    archive_path = shutil.make_archive(
        str(base_name), "zip", root_dir=str(CBOT_OUTPUT_DIR.parent), base_dir=CBOT_OUTPUT_DIR.name
    )
    return Path(archive_path)


def build_command(task, config):
    """把一条任务翻译成完整的 cTrader backtest 命令。"""
    command = [
        "env",
        f"CTRADER_CLI_AUTHTOKEN={config.auth_token}",
        config.ctrader_bin,
        "backtest",
        config.algo_path,
        f"--ctid={config.ctid}",
        f"--account={config.account}",
        f"--data-mode=ticks",
        "--commission=30",
        "--balance=10000",
        "--environment-variables",
        # backtest 跑完后不会自己退出（进程会空转），--exit-on-stop 让它结束，
        # 否则 subprocess.run 永远等待，批量无法进入下一条。
        "--exit-on-stop",
    ]

    # 品种/周期是 CLI 自己的选项（不是 cBot 参数），在接口记录里也是独立字段，单独拼。
    command.append(f"--symbol={task.symbol}")
    command.append(f"--period={task.period}")

    # 接口那条记录里的每个参数字段都在这里变成命令行参数；本脚本不再固定任何 cBot 参数，
    # 要改回测参数就改后端记录（没填的字段留给 cBot 自己的默认值）。
    command.extend(task.cli_args())
    command.append(f"--report-json={CBOT_OUTPUT_DIR / task.report_file_name}")
    return command
