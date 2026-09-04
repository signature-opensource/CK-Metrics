Makes the metrics pipeline self-managing under CK.AppIdentity.

Owns the FasterLog lifecycle - creation, rotation, disposal - injects it into the monitoring handler,
and registers the measurement consumers declared in the application configuration.

This is the package to reference to get a working pipeline; the export format itself comes from a
consumer package.
