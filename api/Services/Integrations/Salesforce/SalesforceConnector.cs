using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Contracts;
using Api.Options;
using Api.Services.IntegrationCalls;
using Microsoft.Extensions.Options;

namespace Api.Services.Integrations.Salesforce;

/// <summary>
/// Salesforce connector: fetches Accounts live via SOQL and creates Leads via
/// the sobjects REST endpoint, mapping to our own DTOs — raw Salesforce JSON
/// (attributes blobs, success/errors envelopes) never leaves this class.
/// </summary>
public class SalesforceConnector(
    HttpClient httpClient,
    ISalesforceTokenProvider tokenProvider,
    IOptions<SalesforceOptions> options,
    ILogger<SalesforceConnector> logger) : IIntegrationConnector
{
    public string Name => "salesforce";

    private const string AccountsQuery =
        "SELECT Id, Name, Industry, Type, Website, LastModifiedDate FROM Account "
        + "ORDER BY LastModifiedDate DESC LIMIT 50";

    public async Task<IReadOnlyList<SalesforceAccountDto>> GetAccountsAsync(CancellationToken ct = default)
    {
        var response = await SendQueryAsync(ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The org can invalidate a session before our cache TTL lapses;
            // re-authenticate once and retry.
            response.Dispose();
            tokenProvider.Invalidate();
            response = await SendQueryAsync(ct);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Salesforce Account query failed with HTTP {StatusCode}", (int)response.StatusCode);
                var failure = response.StatusCode == HttpStatusCode.Unauthorized
                    ? SalesforceFailure.AuthFailed
                    : SalesforceFailure.UpstreamError;
                throw new SalesforceApiException(failure,
                    $"Salesforce Account query failed with HTTP {(int)response.StatusCode}.");
            }

            return MapAccounts(await response.Content.ReadAsStringAsync(ct));
        }
    }

    public async Task<SalesforceLeadCreatedDto> CreateLeadAsync(
        CreateSalesforceLeadRequest lead, CancellationToken ct = default)
    {
        var response = await SendCreateLeadAsync(lead, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The org can invalidate a session before our cache TTL lapses;
            // re-authenticate once and retry.
            response.Dispose();
            tokenProvider.Invalidate();
            response = await SendCreateLeadAsync(lead, ct);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Salesforce Lead create failed with HTTP {StatusCode}", (int)response.StatusCode);
                var failure = response.StatusCode == HttpStatusCode.Unauthorized
                    ? SalesforceFailure.AuthFailed
                    : SalesforceFailure.UpstreamError;
                throw new SalesforceApiException(failure,
                    $"Salesforce Lead create failed with HTTP {(int)response.StatusCode}.");
            }

            return MapCreatedLead(await response.Content.ReadAsStringAsync(ct));
        }
    }

    private async Task<HttpResponseMessage> SendQueryAsync(CancellationToken ct)
    {
        var session = await tokenProvider.GetSessionAsync(ct);
        var url = $"{session.InstanceUrl}/services/data/{options.Value.ApiVersion}/query"
            + $"?q={Uri.EscapeDataString(AccountsQuery)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", session.AccessToken);
        request.Options.Set(IntegrationCallOptions.EndpointName, "query");
        try
        {
            return await httpClient.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SalesforceApiException(SalesforceFailure.Timeout, "Timed out querying Salesforce.");
        }
        catch (HttpRequestException ex)
        {
            throw new SalesforceApiException(SalesforceFailure.UpstreamError, "Could not reach Salesforce.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendCreateLeadAsync(CreateSalesforceLeadRequest lead, CancellationToken ct)
    {
        var session = await tokenProvider.GetSessionAsync(ct);
        var url = $"{session.InstanceUrl}/services/data/{options.Value.ApiVersion}/sobjects/Lead";
        // A fresh request + content per attempt, so the 401 retry re-sends the body.
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new("Bearer", session.AccessToken);
        request.Options.Set(IntegrationCallOptions.EndpointName, "create-lead");
        request.Content = JsonContent.Create(
            new LeadPayload(lead.LastName, lead.Company, lead.FirstName, lead.Email),
            options: LeadSerializerOptions);
        try
        {
            return await httpClient.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SalesforceApiException(SalesforceFailure.Timeout, "Timed out creating a Salesforce Lead.");
        }
        catch (HttpRequestException ex)
        {
            throw new SalesforceApiException(SalesforceFailure.UpstreamError, "Could not reach Salesforce.", ex);
        }
    }

    private static SalesforceLeadCreatedDto MapCreatedLead(string body)
    {
        try
        {
            var created = JsonSerializer.Deserialize<CreateResponse>(body);
            if (string.IsNullOrEmpty(created?.Id))
                throw new JsonException("Create response contained no id.");
            return new SalesforceLeadCreatedDto(created.Id);
        }
        catch (JsonException ex)
        {
            throw new SalesforceApiException(SalesforceFailure.UpstreamError,
                "Salesforce returned an unexpected response shape.", ex);
        }
    }

    private static IReadOnlyList<SalesforceAccountDto> MapAccounts(string body)
    {
        try
        {
            var query = JsonSerializer.Deserialize<QueryResponse>(body);
            return (query?.Records ?? [])
                .Select(r => new SalesforceAccountDto(
                    r.Id,
                    r.Name,
                    r.Industry,
                    r.Type,
                    r.Website,
                    // Salesforce datetimes use a colon-less offset ("+0000"),
                    // which System.Text.Json's ISO 8601 parsing rejects.
                    DateTimeOffset.Parse(r.LastModifiedDate, CultureInfo.InvariantCulture)))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentNullException)
        {
            throw new SalesforceApiException(SalesforceFailure.UpstreamError,
                "Salesforce returned an unexpected response shape.", ex);
        }
    }

    // Salesforce treats an explicit null as a field write, so omit unset fields.
    private static readonly JsonSerializerOptions LeadSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record LeadPayload(
        [property: JsonPropertyName("LastName")] string LastName,
        [property: JsonPropertyName("Company")] string Company,
        [property: JsonPropertyName("FirstName")] string? FirstName,
        [property: JsonPropertyName("Email")] string? Email);

    private sealed record CreateResponse(
        [property: JsonPropertyName("id")] string? Id);

    private sealed record QueryResponse(
        [property: JsonPropertyName("records")] List<AccountRecord>? Records);

    private sealed record AccountRecord(
        [property: JsonPropertyName("Id")] string Id,
        [property: JsonPropertyName("Name")] string Name,
        [property: JsonPropertyName("Industry")] string? Industry,
        [property: JsonPropertyName("Type")] string? Type,
        [property: JsonPropertyName("Website")] string? Website,
        [property: JsonPropertyName("LastModifiedDate")] string LastModifiedDate);
}
