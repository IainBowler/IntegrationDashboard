namespace Api.Contracts;

/// <summary>
/// Lifetime statistics for one endpoint. Nullable aggregates are null when the
/// endpoint has never been called; LastStatusCode is also null when the most
/// recent call was a transport failure that produced no HTTP status.
/// </summary>
public sealed record EndpointStatisticsResponse(
    string EndpointName,
    string Direction,
    int TotalCalls,
    int SuccessCount,
    double? AvgDurationMs,
    int? MaxDurationMs,
    DateTime? LastCalledAtUtc,
    int? LastStatusCode);

public sealed record IntegrationStatisticsResponse(
    string Name,
    string DisplayName,
    IReadOnlyList<EndpointStatisticsResponse> Endpoints);
