namespace Api.Services.Auth;

public record UserRecord(
    long UserId,
    string Provider,
    string ExternalSubjectId,
    string? Email,
    string? DisplayName);
