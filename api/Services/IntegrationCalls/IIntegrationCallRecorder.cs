namespace Api.Services.IntegrationCalls;

/// <summary>
/// The only entry point the recording seams use: redacts then saves, and
/// never throws — a failure to audit must never fail the audited call.
/// </summary>
public interface IIntegrationCallRecorder
{
    Task RecordAsync(IntegrationCallRecord call);
}
