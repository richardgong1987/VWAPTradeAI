"""串行 / 并发执行回测任务。汇总图 final_report.png 由调用方在全部跑完后生成一次。

默认逐条串行；显式传 jobs>1 时用有界线程池并发。子进程等待会释放 GIL，所以线程即可并行。
"""

import subprocess
from collections import namedtuple
from concurrent.futures import ThreadPoolExecutor, as_completed

from summary import update_final_report
from summary.metrics import read_report_stats
from upload_reports import upload_zip

from . import command

# 一条任务的最终结果。index 用来对上运行时打的 [i/total]，方便回头翻日志；
# attempts > 1 说明重试过，用来观察这个偶发故障的真实频率。
TaskResult = namedtuple("TaskResult", ["index", "task", "return_code", "attempts", "is_successful"])

# 默认串行。并发跑多个 cTrader 进程时，回测偶发在写 report-json 那一步抛
# InvalidOperationException: Message expected，且失败的那条会静默从汇总里消失，
# 得不偿失。需要提速时用 --jobs N 显式开启，自行确认报告份数与接口记录条数一致。
DEFAULT_JOBS = 1

# 首次 + 重试一次。上面那个异常是 cTrader 关闭时序上的竞争，重跑通常就过；
# 重试次数会打进日志并计入本批结论，免得把故障频率一起掩盖掉。
MAX_ATTEMPTS = 2


def run_task(task, index, total, config):
    """跑一条任务，失败自动重试到 MAX_ATTEMPTS 次。返回 TaskResult。"""
    print(
        f"\n[{index}/{total}] 回测 {task.report_file_name} "
        f"({task.start_date} -> {task.end_date})",
        flush=True,
    )

    return_code = None
    for attempt in range(1, MAX_ATTEMPTS + 1):
        return_code = subprocess.run(command.build_command(task, config)).returncode

        if has_usable_report(task) and return_code == 0:
            suffix = f"（第 {attempt} 次尝试）" if attempt > 1 else ""
            print(f"[{index}/{total}] 完成{suffix}", flush=True)
            return TaskResult(index, task, return_code, attempt, True)

        reason = _describe_failure(task, return_code)

        if attempt < MAX_ATTEMPTS:
            print(f"[{index}/{total}] {reason}，重试中……", flush=True)

    print(f"[{index}/{total}] {reason}，已试 {MAX_ATTEMPTS} 次，放弃", flush=True)
    return TaskResult(index, task, return_code, MAX_ATTEMPTS, False)


def has_usable_report(task):
    """报告 JSON 是否已落盘且能被汇总读取。

    只看退出码不够：cTrader 偶发在写 report-json 那步内部抛异常，进程仍可能以 0 退出。
    这里用汇总层同一个读取函数判定，确保「成功」等价于「这条会出现在汇总里」。
    """
    report_path = command.CBOT_OUTPUT_DIR / task.report_file_name
    if not report_path.exists():
        return False
    return read_report_stats(report_path) is not None


def _describe_failure(task, return_code):
    if return_code != 0:
        return f"失败(退出码 {return_code})"
    return "失败(退出码 0，但没写出可用的报告 JSON)"


def generate_final_report():
    """全部回测结束后，扫描输出目录里的所有报告，生成一次汇总图和汇总表。生成失败不应中断批次。

    返回 SummaryOutputs（含各产物路径与共用时间戳）供打包复用；无报告或生成失败时返回 None。
    """
    try:
        outputs = update_final_report(command.CBOT_OUTPUT_DIR)
    except Exception as error:  # noqa: BLE001 — 汇总产物是附带结果，任何异常都不该拖垮回测
        print(f"*****汇总报告生成失败（已跳过）：{error}", flush=True)
        return None

    if outputs is not None:
        print(f"*****汇总图已生成：{outputs.chart_path}", flush=True)
        print(f"*****汇总表已生成：{outputs.csv_path}", flush=True)
        print(f"*****元数据已生成：{outputs.metadata_path}", flush=True)
    return outputs


def archive_reports(timestamp=None):
    """把报告目录打包成 zip 存档，返回 zip 路径供上传复用；无产物或失败时返回 None。

    传入汇总产物的时间戳，让 zip 后缀与 final_report_<ts>.png 一致（trading_reports_<ts>.zip）。
    打包是附带产物，任何异常都不该拖垮回测批次。
    """
    try:
        archive_path = command.archive_output_dir(timestamp)
    except Exception as error:  # noqa: BLE001
        print(f"*****报告打包失败（已跳过）：{error}", flush=True)
        return None

    print(f"*****报告已打包：{archive_path}", flush=True)
    return archive_path


def upload_report_archive(upload_url, archive_path):
    """把刚打包的报告 zip 上传到后端。上传是附带产物，任何异常都不该拖垮调用方。

    上传地址随环境不同，由调用方从 .env 的 REPORT_UPLOAD_URL 读入；未配置则跳过。
    只上传本次生成的这一个 zip（archive_path），不扫描目录，避免误传旧存档。
    """
    if not upload_url:
        print("*****未配置 REPORT_UPLOAD_URL，跳过上传。", flush=True)
        return
    if archive_path is None:
        print("*****没有可上传的报告 zip，跳过上传。", flush=True)
        return

    try:
        upload_zip(upload_url, archive_path)
    except Exception as error:  # noqa: BLE001
        print(f"*****报告上传失败（已跳过）：{error}", flush=True)


def run_tasks_sequentially(tasks, config):
    """逐条串行执行（jobs 1）：保持顺序。返回每条的 TaskResult。"""
    total = len(tasks)
    return [run_task(task, index, total, config) for index, task in enumerate(tasks, start=1)]


def run_tasks_in_parallel(tasks, config, jobs):
    """有界并发执行（jobs N）：最多同时跑 jobs 条。返回按接口记录顺序排好的 TaskResult。"""
    total = len(tasks)
    results = []
    with ThreadPoolExecutor(max_workers=jobs) as executor:
        futures = [
            executor.submit(run_task, task, index, total, config)
            for index, task in enumerate(tasks, start=1)
        ]
        for future in as_completed(futures):
            results.append(future.result())  # 让意外异常冒出来（正常失败只是 is_successful=False）
    return sorted(results, key=lambda result: result.index)


def run_tasks(tasks, config, jobs):
    """跑完整批，返回每条的 TaskResult。"""
    # 首条回测写盘前先清空并重建 trading_reports 目录，确保只保留本次批量生成的数据。
    print(f"清空并重建报告目录：{command.CBOT_OUTPUT_DIR}", flush=True)
    command.reset_output_dir()
    if jobs <= 1:
        return run_tasks_sequentially(tasks, config)
    return run_tasks_in_parallel(tasks, config, jobs)


def find_missing_reports(tasks):
    """收尾复核：哪些任务最终没有可用的报告 JSON。

    正常情况下这与 TaskResult 的失败集一致；两者对不上说明报告在跑完之后又丢了或被覆盖
    （例如两条记录只差一个不进文件名的参数，后一条盖掉了前一条）。
    """
    return [task for task in tasks if not has_usable_report(task)]


def print_batch_summary(tasks, results, missing_reports):
    """把本批结论集中打印一次。中途那行 [5/7] 失败 很容易被回测日志淹掉。"""
    total = len(tasks)
    failures = [result for result in results if not result.is_successful]
    retried = [result for result in results if result.is_successful and result.attempts > 1]

    print(f"\n===== 本批结果：计划 {total} 条，成功 {total - len(failures)} 条，失败 {len(failures)} 条 =====")

    for result in retried:
        print(f"  重试后成功 [{result.index}/{total}]（试了 {result.attempts} 次）：{result.task.report_file_name}")

    for result in failures:
        print(f"  失败 [{result.index}/{total}]：{result.task.report_file_name}（试了 {result.attempts} 次）")

    failed_reports = {result.task.report_file_name for result in failures}
    for task in missing_reports:
        if task.report_file_name not in failed_reports:
            print(f"  无可用报告（不会出现在汇总里）：{task.report_file_name}")

    if not failures and not missing_reports:
        note = f"，其中 {len(retried)} 条靠重试才过" if retried else ""
        print(f"  全部成功，报告份数与接口记录一致{note}。")
