# CK-Metrics

Supports [System.Diagnostics.Metrics](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics) with
a different approach than other metrics collectors and handlers like the ones in [Microsoft.Extensions.Diagnostics.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions).

The approach is simpler as there are no DI involved. Just like the [DotNetEventEventSource](https://github.com/signature-opensource/CK-ActivityMonitor/tree/develop/CK.ActivityMonitor/DotNetEventSource)
support, metrics are global to an application (technically the AppDomain but we don't play with multiple AppDomains anyway).

This library is on the collector side of the Telemetry world. It can handle Meters and Instruments defined in external assemblies
like the [.Net standard ones](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-diagnostics) but we
impose some constraints that are stricter than the OpenTelemetry framework suggests.

## Restrictions

### Meter
The meter's name must be a namespace-like simple identifier that cannot be longer than the
static `int DotNetMetrics.MeterNameLengthLimit` that defaults to 255 charaters.
This can be programmatically changed if needed.
It is checked by `^[a-zA-Z][_a-zA-Z0-9]*(\.[_a-zA-Z0-9]*)*$` regular expression.

The meter's description can be any string (of any length). 

### Instrument

The instrument's name follows https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-name-syntax.
It cannot be longer than 255 charaters.
It is checked by `^[a-zA-Z][-_\./a-zA-Z0-9]*$` regular expression.

The instrument's unit (when defined) must be at most 63 characters and can contain any Unicode characters.
See https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-unit

### Tags (Attributes)

By defaut, a maximum of 128 tags per meter or instrument is allowed.
This can be programmatically changed by the static `int DotNetMetrics.AttributeCountLimit` property.

For tag's key ([attribute names](https://opentelemetry.io/docs/specs/semconv/general/naming/) for OpenTelemetry) we
enforce the [OpenTelemetry Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/general/naming/#recommendations-for-opentelemetry-authors)
and restricts keys to not be longer than the static `int DotNetMetrics.AttributeNameLengthLimit` that defaults to 255 charaters.
This can be programmatically changed if needed.
It is checked by `^[a-z][a-z0-9]((\.|_)([a-z][a-z0-9])*)*$` regular expression.

Only long, double, boolean and string (or array of them) are allowed in Tag values (see [OpenTelemetry's attributes]()).
However, strings cannot be longer than the static `int DotNetMetrics.AttributeValueLengthLimit` that defaults to 1023 charaters.
This can be programmatically changed if needed.

Note that a `null` string is allowed as a tag's value and in an array of strings.
Only a warning is emitted when a null value is found.




