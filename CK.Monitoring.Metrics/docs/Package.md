Bridges CK.Metrics and CK.Monitoring: the `MetricsLogHandler` GrandOutput handler (configured by
`MetricsLogHandlerConfiguration`) writes the metrics log entries to a high-performance FasterLog.

The handler does not create its FasterLog: it must be injected at runtime with `SetFasterLog`. The
CK.AppIdentity.Monitoring.Metrics package owns that lifecycle and performs the injection.
