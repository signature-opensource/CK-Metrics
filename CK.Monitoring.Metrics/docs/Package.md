Bridges CK.Metrics and CK.Monitoring with a GrandOutput handler that writes measurements to a
high-performance FasterLog.

The handler is a producer only, and it does not work on its own: it needs a FasterLog instance
injected at runtime. Another package owns that lifecycle and performs the injection.
