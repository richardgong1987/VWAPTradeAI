"""从后端参数接口拉取回测参数记录（HTTP 细节都关在这里）。

GET <CTRADER_PARAMETER_RECORDS_URL> 返回：

    {"code": 200, "success": true, "data": [
        {"recordId": 1, "symbol": "XAUUSD", "period": "m5",
         "parameterFields": [{"name": "TakeProfitR", "type": "double", "value": 2}, ...]},
        ...
    ]}

本模块只负责把 data 原样取出来，不解释里面的字段——那是 plan.py 的事。
只依赖标准库，与 upload_reports.py 保持一致。
"""

import json
import urllib.request

REQUEST_TIMEOUT_SECONDS = 30


def fetch_parameter_records(records_url):
    """拉取参数记录列表；接口报错或返回结构不对时抛 ValueError。"""
    payload = _get_json(records_url)
    if payload.get("success") is False:
        raise ValueError(f"参数接口返回失败：{payload.get('msg') or payload}")

    records = payload.get("data")
    if not isinstance(records, list):
        raise ValueError(f"参数接口返回的 data 不是列表：{records!r}")
    return records


def _get_json(records_url):
    request = urllib.request.Request(records_url, headers={"Accept": "application/json"})
    with urllib.request.urlopen(request, timeout=REQUEST_TIMEOUT_SECONDS) as response:
        return json.loads(response.read().decode("utf-8"))
