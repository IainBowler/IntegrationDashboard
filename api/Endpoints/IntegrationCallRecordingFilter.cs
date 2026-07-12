using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Api.Services.IntegrationCalls;

namespace Api.Endpoints;

/// <summary>
/// Inbound recording seam: applied to an integration's route group so every
/// authorised request to /api/integrations/{name}/* is audited. Runs after
/// authorization, so anonymous 401s never reach it (by design — they never
/// touch the integration). The response body is a re-serialisation of the
/// TypedResults value, not a wire capture — equivalent for our handlers and
/// far simpler than response-stream duplication.
/// </summary>
public sealed class IntegrationCallRecordingFilter(string integrationName) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var recorder = http.RequestServices.GetRequiredService<IIntegrationCallRecorder>();
        var correlationId = Activity.Current?.TraceId.ToString();
        var userId = long.TryParse(http.User.FindFirstValue("sub"), out var id) ? id : (long?)null;
        var method = http.Request.Method;
        var url = http.Request.Path + http.Request.QueryString;
        // last path segment matches the seeded dbo.IntegrationEndpoint names
        var endpointName = http.Request.Path.Value?.TrimEnd('/') is { Length: > 0 } path
            ? path[(path.LastIndexOf('/') + 1)..]
            : null;
        // Like the response body below, the request body is a re-serialisation
        // of the bound Contract argument rather than a wire capture — the body
        // stream is already consumed by binding when this filter runs.
        var requestBody = context.Arguments
            .FirstOrDefault(a => a?.GetType().Namespace == "Api.Contracts") is { } dto
            ? JsonSerializer.Serialize(dto, dto.GetType(), JsonSerializerOptions.Web)
            : null;
        var stopwatch = Stopwatch.StartNew();

        object? result;
        try
        {
            result = await next(context);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await recorder.RecordAsync(new IntegrationCallRecord(
                IntegrationCallDirection.Inbound,
                integrationName,
                endpointName,
                correlationId,
                userId,
                method,
                url,
                StatusCode: null,
                (int)stopwatch.ElapsedMilliseconds,
                requestBody,
                ResponseBody: null,
                Error: $"{ex.GetType().Name}: {ex.Message}"));
            throw;
        }

        stopwatch.Stop();
        // Results<T1, T2> union types wrap the actual result — inspect the inner one
        var inner = result is INestedHttpResult nested ? nested.Result : result;
        var statusCode = (inner as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
        var responseBody = (inner as IValueHttpResult)?.Value is { } value
            ? JsonSerializer.Serialize(value, value.GetType(), JsonSerializerOptions.Web)
            : null;

        await recorder.RecordAsync(new IntegrationCallRecord(
            IntegrationCallDirection.Inbound,
            integrationName,
            endpointName,
            correlationId,
            userId,
            method,
            url,
            statusCode,
            (int)stopwatch.ElapsedMilliseconds,
            requestBody,
            responseBody,
            Error: null));
        return result;
    }
}
