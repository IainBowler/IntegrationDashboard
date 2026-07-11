namespace Api.Services.IntegrationCalls;

public interface IIntegrationCallService
{
    Task SaveAsync(IntegrationCallRecord call);
}
