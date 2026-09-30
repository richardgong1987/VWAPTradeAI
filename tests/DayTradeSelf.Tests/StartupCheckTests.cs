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

        [Theory]
        [InlineData(RelayEnvironmentModel.Off)]
        [InlineData(RelayEnvironmentModel.Local)]
        public void off_and_local_are_fine_with_or_without_a_key(RelayEnvironmentModel environment) {
            Assert.Null(StartupCheck.FindRelayError(environment, ""));
            Assert.Null(StartupCheck.FindRelayError(environment, "myaccesscode"));
        }

        [Fact]
        public void production_with_its_key_is_fine() {
            Assert.Null(StartupCheck.FindRelayError(RelayEnvironmentModel.Production, "myaccesscode"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void production_without_a_key_is_refused(string accessKey) {
            Assert.Contains("访问密钥", StartupCheck.FindRelayError(RelayEnvironmentModel.Production, accessKey));
        }
    }
}
