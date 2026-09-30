namespace cAlgo.Robots;

// Which relay the cBot talks to (see RelayEnvironments). Local by default, so testing never
// reaches the production relay — and the boss's app — by accident.
public enum RelayEnvironmentModel {
    Off, // no relay: signals are only logged, nothing can be approved
    Local, // a relay on this machine, for testing
    Production // the deployed relay the boss's app listens to; needs the access key
}
