"""从 .env 文件读入环境相关配置（账户、鉴权、cTrader 路径、接口地址、回测资金/数据模式）。

账户、路径、鉴权等“环境相关”配置不写死在脚本里，而是从 .env 文件读取，这样同一套代码
用不同 env 文件即可切换环境（开发 / 生产）。

env 文件分两层：scripts/.env 是各环境共用的底稿，环境专用文件（如 .env-prod）叠在它
上面，只写与底稿不同的键。这样账户、鉴权这类共用值只维护一份，不会两个文件各抄一遍
而后改一处忘另一处。

.algo 的位置不进 .env：cTrader.Automate 编译后固定把 .algo 发布到「仓库根目录的上一层」，
文件名等于仓库根目录名（见 cTrader.Automate.targets 的 _AlgoRootDirectoryName / _AlgoPublish），
所以它可以从仓库路径直接推导出来。
"""

from pathlib import Path

from dotenv import dotenv_values

# .env 必填项；DATA_MODE / BALANCE 选填，未填用默认值。
# CTRADER_PARAMETER_RECORDS_URL 是回测计划的唯一来源，缺了就一条任务也跑不了，所以必填。
REQUIRED_ENV_KEYS = [
    "AUTH_TOKEN",
    "CTRADER_BIN",
    "CTID",
    "ACCOUNT",
    "CTRADER_PARAMETER_RECORDS_URL",
]
DEFAULT_DATA_MODE = "m1"
DEFAULT_BALANCE = "10000"

REPO_ROOT = Path(__file__).resolve().parents[2]
# 各环境共用的底稿；环境专用文件（.env-prod 等）只写差异，同名键覆盖它。
BASE_ENV_FILE = Path(__file__).resolve().parents[1] / ".env"


def resolve_algo_path():
    """推导编译产物 .algo 的路径：仓库上一层目录 / 仓库目录名.algo。"""
    return REPO_ROOT.parent / f"{REPO_ROOT.name}.algo"


class Config:
    """从 .env 文件读入的环境相关配置（账户、鉴权、cTrader 路径、回测资金/数据模式）。"""

    def __init__(self, values):
        self.auth_token = values["AUTH_TOKEN"]
        self.ctrader_bin = values["CTRADER_BIN"]
        self.algo_path = str(resolve_algo_path())
        self.ctid = values["CTID"]
        self.account = values["ACCOUNT"]
        # 回测计划（跑哪些参数组合）来自后端接口，地址随环境不同（开发/生产）。
        self.parameter_records_url = values["CTRADER_PARAMETER_RECORDS_URL"]
        self.data_mode = values.get("DATA_MODE") or DEFAULT_DATA_MODE
        self.balance = values.get("BALANCE") or DEFAULT_BALANCE
        # 上传接口地址随环境不同（开发/生产）；未配置时留空，批量结束后跳过上传。
        self.report_upload_url = values.get("REPORT_UPLOAD_URL") or ""


def parse_env_file(env_file):
    """把 env 文件解析成 key->value 字典：先读共用底稿 .env，再让指定文件覆盖同名键。

    解析交给 python-dotenv：注释（含值后面的行内注释）、引号、转义、export 前缀这些边角
    它都处理好了，自己写一遍只会少处理几种。文件不存在时 dotenv_values 静默返回空字典，
    所以这里先自己检查一次，好告诉人该怎么办。
    """
    if not env_file.exists():
        raise FileNotFoundError(
            f"找不到环境配置文件：{env_file}\n"
            "请复制 scripts/.env.example 为 .env（或 .env-prod）并填好里面的值。"
        )
    values = {}
    if BASE_ENV_FILE.exists() and env_file.resolve() != BASE_ENV_FILE:
        values.update(dotenv_values(BASE_ENV_FILE))
    values.update(dotenv_values(env_file))
    return values


def load_config(env_file):
    values = parse_env_file(env_file)
    missing = [key for key in REQUIRED_ENV_KEYS if not values.get(key)]
    if missing:
        raise ValueError(
            f"环境配置文件 {env_file} 缺少必填项：" + "、".join(missing)
        )
    algo_path = resolve_algo_path()
    if not algo_path.exists():
        raise FileNotFoundError(
            f"找不到编译产物：{algo_path}\n"
            "请先编译 cBot：dotnet build \"DayTradeSelf.sln\" -c Release"
        )
    return Config(values)
