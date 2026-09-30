"""cTrader 批量回测的内部实现，按职责拆成几个模块：

- config  ：从 .env 读环境相关配置（账户 / 路径 / 鉴权 / 接口地址 / 回测资金 / 数据模式）。
- records ：请求后端参数接口，取回回测参数记录（HTTP 细节都在这里）。
- plan    ：把每条参数记录变成回测任务对象 ConditionRow。
- command ：把一条回测任务翻译成 cTrader CLI 的 backtest 命令。
- runner  ：串行 / 并发执行任务，并在每条完成后刷新汇总图。

命令行入口在上一层的 run_conditions.py（组合根：解析参数、装配各模块）。
"""
