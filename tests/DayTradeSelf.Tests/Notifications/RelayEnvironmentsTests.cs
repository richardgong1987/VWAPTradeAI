using System;
using cAlgo.Robots;
using Xunit;

namespace DayTradeSelf.Tests.Notifications {
    // The two relays, defined once. The app (rustapp/src/main.ts) must list the same addresses.
    public class RelayEnvironmentsTests {
        [Fact]
        public void local_is_the_relay_on_this_machine() {
            RelayEndpointModel endpoint = RelayEnvironments.Endpoint(RelayEnvironmentModel.Local, "myaccesscode");

            Assert.Equal(new Uri("ws://localhost:8080/ws"), endpoint.Url);
            Assert.Equal("myaccesscode", endpoint.AccessKey);
        }

        [Fact]
        public void production_is_the_deployed_relay_over_tls() {
            RelayEndpointModel endpoint = RelayEnvironments.Endpoint(RelayEnvironmentModel.Production, " myaccesscode ");

            Assert.Equal(new Uri("wss://webhook.raku-den.net/ws"), endpoint.Url);
            Assert.Equal("myaccesscode", endpoint.AccessKey);
        }

        [Fact]
        public void off_has_no_relay_to_connect_to() {
            Assert.Throws<ArgumentOutOfRangeException>(() => RelayEnvironments.Endpoint(RelayEnvironmentModel.Off, ""));
        }
    }
}
