Supports System.Diagnostics.Metrics with a different approach than the usual collectors and handlers.

DI-free: configuration is static and thread-safe, so a library can be instrumented without any
container. A global `MeterListener` captures every instrument automatically, and instruments are
matched and aggregated by configuration rather than by registration.

This is the producer side only: it emits measurements, it does not transport or store them.
