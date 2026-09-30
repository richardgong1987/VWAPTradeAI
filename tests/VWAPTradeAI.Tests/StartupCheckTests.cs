using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests {
    // 启动校验。这些规则一旦失效，cBot 会带着一套算错的参数安静跑完整个回测 —— 所以宁可停下来。
    public class StartupCheckTests {
        private static string Check(bool isM5 = true, string label = "VWAPTradeAI-label") =>
            StartupCheck.FindError(isM5, "m5", label);

        [Fact]
        public void a_correct_setup_reports_nothing() {
            Assert.Null(Check());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void an_empty_order_label_is_refused(string label) {
            // 标签为空时，这个品种上每一个单子都会被当成本 cBot 的。
            Assert.Contains("订单标签", Check(label: label));
        }

        [Fact]
        public void a_chart_other_than_m5_is_refused() {
            Assert.Contains("只支持 M5", Check(isM5: false));
        }

        [Fact]
        public void the_label_is_checked_before_the_timeframe() {
            // 两个都错时先报标签：那是更基础的一个。
            Assert.Contains("订单标签", Check(isM5: false, label: ""));
        }

        private static string CheckAi(bool isEnabled = true, bool isRealTime = true, string url = "http://127.0.0.1:8787",
            int timeoutSeconds = 30) =>
            StartupCheck.FindAiFilterError(isEnabled, isRealTime, url, timeoutSeconds);

        [Fact]
        public void the_ai_filter_on_a_live_or_demo_chart_with_the_defaults_reports_nothing() {
            Assert.Null(CheckAi());
        }

        [Fact]
        public void the_ai_filter_is_refused_in_a_backtest_or_optimization() {
            // A backtest's clock does not wait for the model, and its chart picture lags the cBot.
            Assert.Contains("只支持实盘和模拟盘", CheckAi(isRealTime: false));
        }

        [Fact]
        public void with_the_ai_filter_off_a_backtest_needs_nothing_from_the_ai_settings() {
            // Off, the cBot must behave exactly as before, whatever the AI parameters hold.
            Assert.Null(CheckAi(isEnabled: false, isRealTime: false, url: "", timeoutSeconds: 0));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("127.0.0.1:8787")]
        [InlineData("/v1/assessments")]
        [InlineData("ftp://127.0.0.1:8787")]
        public void an_ai_service_address_that_is_not_an_http_url_is_refused(string url) {
            Assert.Contains("AI服务地址无效", CheckAi(url: url));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(241)] // an answer must arrive well inside the 300 s bar after the signal
        public void an_ai_timeout_outside_the_allowed_range_is_refused(int timeoutSeconds) {
            Assert.Contains("AI超时秒数", CheckAi(timeoutSeconds: timeoutSeconds));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(240)]
        public void an_ai_timeout_at_either_end_of_the_range_is_accepted(int timeoutSeconds) {
            Assert.Null(CheckAi(timeoutSeconds: timeoutSeconds));
        }
    }
}
