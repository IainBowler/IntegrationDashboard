namespace Api.Services.Auth;

public record ExternalUserInfo(
    string Provider,
    string SubjectId,
    string? Email,
    string? DisplayName);
