namespace Api.Contracts;

public record TokenResponse(
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    UserResponse User);
