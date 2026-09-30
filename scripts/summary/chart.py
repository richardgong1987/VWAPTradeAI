"""展示层：把汇总 DataFrame 画成上下两幅柱状图（净利润 / 胜率）并存成 PNG。"""

import matplotlib

matplotlib.use("Agg")  # 无界面后端：脚本里存图不需要弹窗
import matplotlib.pyplot as plt  # noqa: E402

# 柱状图配色
PROFIT_POSITIVE_COLOR = "#2e7d32"
PROFIT_NEGATIVE_COLOR = "#c62828"
WIN_RATE_COLOR = "#1565c0"

CHART_TITLE = "Backtest summary — net profit & win rate per report"


def render_report_chart(frame, image_path):
    """把汇总表画成上下两幅柱状图并存成 PNG。X 轴用报告的完整文件名。"""
    labels = frame["report"].tolist()
    figure_width = max(12.0, len(frame) * 0.45)

    figure, (profit_axes, win_rate_axes) = plt.subplots(2, 1, figsize=(figure_width, 9), sharex=True)
    _draw_net_profit(profit_axes, frame)
    _draw_win_rate(win_rate_axes, frame, labels)

    figure.tight_layout()
    # bbox_inches="tight" 保证竖排的完整文件名标签不会被裁掉
    figure.savefig(image_path, dpi=150, bbox_inches="tight")
    plt.close(figure)


def _draw_net_profit(axes, frame):
    """上图：每份报告的净利润，盈利绿色、亏损红色。"""
    colors = [PROFIT_POSITIVE_COLOR if value >= 0 else PROFIT_NEGATIVE_COLOR for value in frame["net_profit"]]
    axes.bar(range(len(frame)), frame["net_profit"], color=colors)
    axes.axhline(0, color="black", linewidth=0.8)
    axes.set_ylabel("Net profit")
    axes.set_title(CHART_TITLE)
    axes.grid(axis="y", linestyle=":", alpha=0.4)


def _draw_win_rate(axes, frame, labels):
    """下图：每份报告的胜率（0–100%），X 轴标注报告完整文件名。"""
    axes.bar(range(len(frame)), frame["win_rate"], color=WIN_RATE_COLOR)
    axes.set_ylabel("Win rate (%)")
    axes.set_ylim(0, 100)
    axes.grid(axis="y", linestyle=":", alpha=0.4)
    axes.set_xticks(range(len(frame)))
    axes.set_xticklabels(labels, rotation=90, fontsize=7)
