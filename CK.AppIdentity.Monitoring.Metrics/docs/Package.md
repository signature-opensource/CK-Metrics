Makes the metrics pipeline self-managing under CK.AppIdentity.

The `MetricsFeatureDriver` owns the FasterLog lifecycle - creation, truncation, disposal -, injects it into the
`MetricsLogHandler` and registers the measurement consumers. Consumers implement `IMetricsConsumer`, usually by
specializing `MetricsConsumerBase`; the export format itself comes from a consumer package.
