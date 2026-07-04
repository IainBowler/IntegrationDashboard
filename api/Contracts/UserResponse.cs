namespace Api.Contracts;

public record UserResponse(
    long UserId,
    string Provider,
    string? Email,
    string? DisplayName);
