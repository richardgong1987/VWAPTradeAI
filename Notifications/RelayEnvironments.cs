using System;

namespace cAlgo.Robots;

// The relays the cBot can talk to, in one place. The app (rustapp/src/main.ts) lists the same two.
//
// Pure, no cAlgo dependency, unit tested.
public static class RelayEnvironments {
    public static readonly Uri Local = new("ws://localhost:8080/ws");
    public static readonly Uri Production = new("wss://webhook.raku-den.net/ws");

    public static RelayEndpointModel Endpoint(RelayEnvironmentModel environment, string accessKey) {
        Uri url = environment switch {
            RelayEnvironmentModel.Local => Local,
            RelayEnvironmentModel.Production => Production,
            _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, "no relay to connect to")
        };
        return new RelayEndpointModel(url, accessKey);
    }
}
