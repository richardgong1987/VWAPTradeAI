"""接口参数字段 -> cTrader CLI 参数 的翻译规则。

参数接口返回的每个 parameterField 形如 {"name", "type", "value"}，name 与 cBot 的 C# 属性名
逐字一致，所以命令行参数就是 --<name>=<格式化后的 value>。加减参数请改后端那张记录表，
脚本这边不再维护参数白名单，本文件只负责按 type 把值格式化成 CLI 能接受的文本。

注意：非法枚举取值 CLI 不报错，会静默退回参数默认值——枚举取值由后端保证，这里原样透传。
"""

from datetime import datetime

API_DATE_FORMAT = "%Y-%m-%d"
CLI_DATE_FORMAT = "%d/%m/%Y"
COMPACT_DATE_FORMAT = "%Y%m%d"


def to_cli_date(value):
    """接口的 YYYY-MM-DD -> cTrader 要的 DD/MM/YYYY。"""
    return _parse_api_date(value).strftime(CLI_DATE_FORMAT)


def to_compact_date(value):
    """接口的 YYYY-MM-DD -> 报告文件名用的紧凑 YYYYMMDD。"""
    return _parse_api_date(value).strftime(COMPACT_DATE_FORMAT)


def to_number_text(value):
    """数字文本：2.0 写成 "2"，1.75 保留成 "1.75"（接口的 double 是 JSON 数字）。"""
    try:
        number = float(value)
    except (TypeError, ValueError):
        raise ValueError(f"的值 {value!r} 不是数字。") from None
    return str(int(number)) if number.is_integer() else str(number)


def to_whole_number_text(value):
    """整数文本（接口的 int / enum 可能以 3.0 这样的浮点回来）。"""
    try:
        return str(int(float(value)))
    except (TypeError, ValueError):
        raise ValueError(f"的值 {value!r} 不是整数。") from None


def _to_bool_text(value):
    """cTrader CLI 的 bool 参数认 True / False。"""
    return "True" if value else "False"


def _parse_api_date(value):
    text = "" if value is None else str(value).strip()
    try:
        return datetime.strptime(text, API_DATE_FORMAT)
    except ValueError:
        raise ValueError(f"的日期 {text!r} 不是合法的 YYYY-MM-DD。") from None


# 按字段 type 选格式化函数；没登记的 type（string / enum 等）原样转成文本。
_CLI_VALUE_FORMATTERS = {
    "double": to_number_text,
    "int": to_whole_number_text,
    "bool": _to_bool_text,
    "date": to_cli_date,
}


class ParameterField:
    """接口返回的一个参数字段：name 就是 cBot 的属性名，type 决定 value 怎么格式化。"""

    def __init__(self, field):
        self.name = str(field.get("name") or "").strip()
        if not self.name:
            raise ValueError("参数字段缺少 name。")
        self.type = str(field.get("type") or "").strip()
        self.value = field.get("value")

    @property
    def is_blank(self):
        """值为空说明后端没填这一项：跳过不传，让 cBot 用自己的参数默认值。

        （空值直接拼成 --Name= 时 CLI 的行为没有保证，不如不传。）
        """
        return self.value is None or (isinstance(self.value, str) and not self.value.strip())

    def cli_arg(self):
        """这个字段对应的命令行片段，例如 --TakeProfitR=2。"""
        formatter = _CLI_VALUE_FORMATTERS.get(self.type, str)
        try:
            return f"--{self.name}={formatter(self.value)}"
        except ValueError as error:
            raise ValueError(f"参数「{self.name}」{error}") from error
