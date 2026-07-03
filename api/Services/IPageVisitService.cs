namespace Api.Services;

public interface IPageVisitService
{
    Task RecordVisitAsync(string pagePath);
    Task<long> GetVisitCountAsync(string pagePath);
}
