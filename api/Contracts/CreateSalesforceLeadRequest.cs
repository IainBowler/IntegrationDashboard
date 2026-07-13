namespace Api.Contracts;

/// <summary>
/// Lead creation payload. LastName and Company are Salesforce's required
/// fields for a Lead; the rest are optional.
/// </summary>
public sealed record CreateSalesforceLeadRequest(
    string LastName,
    string Company,
    string? FirstName = null,
    string? Email = null);
