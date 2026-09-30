"""把参数接口返回的每条记录变成一个回测任务 ConditionRow。

一条 record = 一次回测：symbol / period 决定跑哪个品种周期，parameterFields 里的每一项直接
翻译成一个 cTrader CLI 参数（见 parameters.ParameterField）。参数的增删由后端那张记录表决定，
脚本这边不再维护列名或白名单。

报告文件名的拼法写在 ConditionRow.report_file_name：文件名只放需要区分回测结果的字段，
summary/naming.py 按这个格式反解析出汇总表的各列，改格式要两边一起改。
"""

from .parameters import ParameterField, to_cli_date, to_compact_date, to_number_text
from .records import fetch_parameter_records

FIELD_TAKE_PROFIT = "TakeProfitR"
FIELD_START_DATE = "start"
FIELD_END_DATE = "end"


class ConditionRow:
    """一条回测任务，来自参数接口的一条 record。"""

    def __init__(self, record):
        self.record_id = record.get("recordId")
        self.symbol = _required_text(record.get("symbol"), "缺少 symbol。")
        self.period = _required_text(record.get("period"), "缺少 period。")
        self._fields = _read_fields(record)
        # 下面三项在构造时就算出来：接口数据有问题要在批量开跑之前报错，而不是跑到一半才炸。
        self.start_date = self._formatted(FIELD_START_DATE, to_cli_date)
        self.end_date = self._formatted(FIELD_END_DATE, to_cli_date)
        self.report_file_name = self._build_report_file_name()

    def cli_args(self):
        """这条任务的 cBot 参数命令行片段，顺序与接口返回的字段顺序一致。"""
        return [field.cli_arg() for field in self._fields.values() if not field.is_blank]

    def _build_report_file_name(self):
        return (
            f"{self.symbol}-{self.period}-"
            f"{self._formatted(FIELD_TAKE_PROFIT, to_number_text)}-"
            f"{self._formatted(FIELD_START_DATE, to_compact_date)}-"
            f"{self._formatted(FIELD_END_DATE, to_compact_date)}.json"
        )

    def _formatted(self, field_name, to_text):
        """格式化一个字段的值，出错时补上字段名——否则「不是合法的 YYYY-MM-DD」看不出是哪个字段。"""
        value = self._value(field_name)
        try:
            return to_text(value)
        except ValueError as error:
            raise ValueError(f"参数「{field_name}」{error}") from error

    def _value(self, field_name):
        field = self._fields.get(field_name)
        if field is None:
            raise ValueError(f"缺少参数「{field_name}」，无法确定这条回测跑什么。")
        return field.value


def read_condition_rows(records_url):
    """拉取参数接口，返回回测任务列表（顺序与接口返回一致）。"""
    return [_to_condition_row(record) for record in fetch_parameter_records(records_url)]


def _to_condition_row(record):
    """接口数据有问题时补上 recordId 再抛出，让人一眼看出该改后端的哪条记录。"""
    try:
        return ConditionRow(record)
    except ValueError as error:
        raise ValueError(f"参数记录 recordId={record.get('recordId')}：{error}") from error


def _read_fields(record):
    fields = {}
    for raw_field in record.get("parameterFields") or []:
        field = ParameterField(raw_field)
        fields[field.name] = field
    return fields


def _required_text(value, message):
    text = "" if value is None else str(value).strip()
    if not text:
        raise ValueError(message)
    return text
