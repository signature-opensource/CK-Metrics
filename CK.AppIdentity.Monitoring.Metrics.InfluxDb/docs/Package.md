An InfluxDB export consumer for the CK.AppIdentity metrics pipeline.

The `InfluxDbMetricsConsumer` posts measurements to InfluxDB v2.x over HTTP(S) using the line protocol. It is enabled
and configured from the AppIdentity local configuration (the "InfluxDb" option of the "Metrics" feature) - bucket, organization and token included.
