using System;

namespace cAlgo.Robots;

// 启动前的参数校验。有问题就返回一句给用户看的话，没问题返回 null。
//
// 抽出来是为了能单测：这些规则一旦失效，cBot 会带着一套算错的参数安静地跑完整个回测，
// 那比直接停下来糟糕得多。纯判断，没有 cAlgo 依赖。
public static class StartupCheck {
    // An answer must arrive while the bar after the signal is still open, and an M5 bar lasts 300 s.
    public const int MaxAiTimeoutSeconds = 240;

    public static string FindError(bool isFiveMinuteChart, string timeFrameName, string orderLabel) {
        // 标签为空的话，这个品种上每一个 "_L"/"_S" 结尾的单子都会被当成本 cBot 的单。
        if (string.IsNullOrWhiteSpace(orderLabel))
            return "订单标签不能为空。";

        // The strategy is specified on M5 only (docs/VWAP_Strong_V1.1.docx).
        if (!isFiveMinuteChart)
            return $"VWAP Strong 只支持 M5：当前周期是 {timeFrameName}。";

        return null;
    }

    // With the AI trend filter off, nothing is checked: the cBot then has no AI dependency at all.
    public static string FindAiFilterError(bool isEnabled, bool isRealTime, string serviceUrl, int timeoutSeconds) {
        if (!isEnabled)
            return null;

        // A backtest's clock does not wait for a model that needs seconds, and its chart picture lags
        // behind the cBot, so the filter would judge the wrong bar. Optimization has no chart at all.
        if (!isRealTime)
            return "AI趋势过滤只支持实盘和模拟盘：回测、优化请关闭「启用AI趋势过滤」。";

        bool isHttpUrl = Uri.TryCreate(serviceUrl?.Trim(), UriKind.Absolute, out Uri url) &&
                         (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);

        if (!isHttpUrl)
            return $"AI服务地址无效：「{serviceUrl}」。应为类似 http://127.0.0.1:8787 的地址。";

        if (timeoutSeconds < 1 || timeoutSeconds > MaxAiTimeoutSeconds)
            return $"AI超时秒数必须在 1 到 {MaxAiTimeoutSeconds} 之间：当前是 {timeoutSeconds}。";

        return null;
    }
}
