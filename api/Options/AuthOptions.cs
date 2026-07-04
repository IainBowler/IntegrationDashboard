namespace Api.Options;

public class AuthOptions
{
    public string FrontendBaseUrl { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "";
    public int RefreshTokenDays { get; set; } = 30;
}
