namespace Api.Services.Auth;

public interface IUserService
{
    /// <summary>Creates or updates the user keyed on (Provider, ExternalSubjectId)
    /// and stamps LastLoginAtUtc.</summary>
    Task<UserRecord> UpsertExternalUserAsync(ExternalUserInfo info);

    Task<UserRecord?> GetByIdAsync(long userId);
}
