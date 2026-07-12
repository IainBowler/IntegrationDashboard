using System.Diagnostics;

namespace Api.Services.IntegrationCalls;

/// <summary>
/// Outbound recording seam: chained onto the integration typed HttpClients so
/// every call to the external system is audited. Request and response content
/// are buffered before reading, so the actual send and downstream readers
/// still work. Transport failures are recorded (null status) and rethrown.
/// </summary>
public sealed class IntegrationCallRecordingHandler(
    IIntegrationCallRecorder recorder,
    string integrationName) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? requestBody = null;
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken);
            requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var endpointName = request.Options.TryGetValue(IntegrationCallOptions.EndpointName, out var name)
            ? name
            : null;
        var correlationId = Activity.Current?.TraceId.ToString();
        var url = request.RequestUri?.ToString() ?? "";
        var stopwatch = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await recorder.RecordAsync(new IntegrationCallRecord(
                IntegrationCallDirection.Outbound,
                integrationName,
                endpointName,
                correlationId,
                UserId: null,
                request.Method.Method,
                url,
                StatusCode: null,
                (int)stopwatch.ElapsedMilliseconds,
                requestBody,
                ResponseBody: null,
                Error: $"{ex.GetType().Name}: {ex.Message}"));
            throw;
        }

        stopwatch.Stop();
        await response.Content.LoadIntoBufferAsync(cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        await recorder.RecordAsync(new IntegrationCallRecord(
            IntegrationCallDirection.Outbound,
            integrationName,
            endpointName,
            correlationId,
            UserId: null,
            request.Method.Method,
            url,
            (int)response.StatusCode,
            (int)stopwatch.ElapsedMilliseconds,
            requestBody,
            responseBody,
            Error: null));
        return response;
    }
}
