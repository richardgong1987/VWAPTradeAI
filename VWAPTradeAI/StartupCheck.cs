using System;

namespace cAlgo.Robots;

// 启动前的参数校验。有问题就返回一句给用户看的话，没问题返回 null。
//
// 抽出来是为了能单测：这些规则一旦失效，cBot 会带着一套算错的参数安静地跑完整个回测，
// 那比直接停下来糟糕得多。纯判断，没有 cAlgo 依赖。
public static class StartupCheck {
    public static string FindError(bool isFiveMinuteChart, string timeFrameName, string orderLabel) {
        // 标签为空的话，这个品种上每一个 "_L"/"_S" 结尾的单子都会被当成本 cBot 的单。
        if (string.IsNullOrWhiteSpace(orderLabel))
            return "订单标签不能为空。";

        // The strategy is specified on M5 only (docs/VWAP_Strong_V1.1.docx).
        if (!isFiveMinuteChart)
            return $"VWAP Strong 只支持 M5：当前周期是 {timeFrameName}。";

        return null;
    }

    // The production relay refuses clients without the key, so starting without one is pointless.
    public static string FindRelayError(RelayEnvironmentModel environment, string accessKey) {
        if (environment != RelayEnvironmentModel.Production)
            return null;

        if (string.IsNullOrWhiteSpace(accessKey))
            return "生产环境（Production）需要访问密钥：请填写「访问密钥」。";

        return null;
    }
}
