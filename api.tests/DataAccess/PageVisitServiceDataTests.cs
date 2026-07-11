using Api.Services;
using FluentAssertions;

namespace Api.Tests.DataAccess;

[Collection("SqlServerDatabase")]
[Trait("Category", "Database")]
public class PageVisitServiceDataTests
{
    private readonly PageVisitService _sut;

    public PageVisitServiceDataTests(SqlServerFixture fixture)
    {
        _sut = new PageVisitService(fixture.ConnectionString);
    }

    [DatabaseFact(DisplayName = "recorded visits round-trip through the real table")]
    public async Task RecordVisit_ThenCount_RoundTrips()
    {
        var path = $"/page-{Guid.NewGuid():N}";

        await _sut.RecordVisitAsync(path);
        await _sut.RecordVisitAsync(path);
        await _sut.RecordVisitAsync(path);

        (await _sut.GetVisitCountAsync(path)).Should().Be(3);
    }

    [DatabaseFact(DisplayName = "an unvisited path counts zero in the real table")]
    public async Task Count_UnvisitedPath_IsZero()
    {
        (await _sut.GetVisitCountAsync($"/never-{Guid.NewGuid():N}")).Should().Be(0);
    }

    [DatabaseFact(DisplayName = "the summary groups by path and orders by count descending")]
    public async Task Summary_GroupsByPathAndOrdersByCountDescending()
    {
        var busyPath = $"/busy-{Guid.NewGuid():N}";
        var quietPath = $"/quiet-{Guid.NewGuid():N}";
        await _sut.RecordVisitAsync(busyPath);
        await _sut.RecordVisitAsync(busyPath);
        await _sut.RecordVisitAsync(quietPath);

        var summary = await _sut.GetSummaryAsync();

        // other tests share the table, so assert on our rows only
        var ours = summary.Where(s => s.PagePath == busyPath || s.PagePath == quietPath).ToList();
        ours.Should().HaveCount(2);
        ours[0].Should().Be(new Api.Contracts.PageVisitSummaryItem(busyPath, 2));
        ours[1].Should().Be(new Api.Contracts.PageVisitSummaryItem(quietPath, 1));
    }
}
