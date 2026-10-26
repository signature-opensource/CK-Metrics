Supports System.Diagnostics.Metrics without dependency injection: a global `MeterListener` captures every
instrument and emits meters, instruments and measurements as log entries on `ActivityMonitor.StaticLogger`.

The static `DotNetMetrics` class is the entry point (`ApplyConfiguration`, `GetConfiguration`): a `MetricsConfiguration`
enables instruments with `InstrumentMatcher` rules, and the `DefaultConfigure` extension methods let an instrument
enable itself. This is the producer side only: it does not transport or store the measurements.
