namespace Api.Contracts;

public sealed record SalesforceAccountDto(
    string Id,
    string Name,
    string? Industry,
    string? Type,
    string? Website,
    DateTimeOffset LastModifiedDate);
