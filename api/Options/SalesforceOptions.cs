namespace Api.Options;

public class SalesforceOptions
{
    public string LoginUrl { get; set; } = "https://login.salesforce.com";
    public string ApiVersion { get; set; } = "v66.0";
    public int TokenCacheMinutes { get; set; } = 30;

    // Secrets: User Secrets locally, Key Vault in Azure — never appsettings.json.
    public string ClientId { get; set; } = "";
    public string Username { get; set; } = "";
    public string PrivateKey { get; set; } = "";
}
