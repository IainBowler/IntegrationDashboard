namespace Api.Services.IntegrationCalls;

public sealed class IntegrationCallRecorder(
    IIntegrationCallService service,
    ILogger<IntegrationCallRecorder> logger) : IIntegrationCallRecorder
{
    public async Task RecordAsync(IntegrationCallRecord call)
    {
        var redacted = call with
        {
            RequestBody = IntegrationCallRedactor.Redact(call.RequestBody),
            ResponseBody = IntegrationCallRedactor.Redact(call.ResponseBody),
            Error = IntegrationCallRedactor.Redact(call.Error),
        };

        try
        {
            await service.SaveAsync(redacted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to record {Direction} integration call to {Url}",
                redacted.Direction, redacted.Url);
        }
    }
}
