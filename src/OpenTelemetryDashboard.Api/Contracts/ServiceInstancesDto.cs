namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Item of <c>/logs/services</c> and <c>/traces/services</c>: a
/// <c>service.name</c> with its sorted <c>service.instance.id</c> values
/// (empty when the service never reported one).
/// </summary>
public sealed record ServiceInstancesDto(string Service, IReadOnlyList<string> Instances);
