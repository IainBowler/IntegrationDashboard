using Api.Services.Auth;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace Api.Tests.Unit;

public class MemoryOneTimeCodeStoreTests
{
    private readonly MemoryOneTimeCodeStore _store = new(new MemoryCache(new MemoryCacheOptions()));

    [Fact(DisplayName = "a code redeems exactly once")]
    public void Redeem_ReturnsPayloadOnce()
    {
        var code = _store.Issue("handoff", "42", TimeSpan.FromMinutes(1));

        _store.Redeem("handoff", code).Should().Be("42");
        _store.Redeem("handoff", code).Should().BeNull();
    }

    [Fact(DisplayName = "a code cannot be redeemed for a different purpose")]
    public void Redeem_WithWrongPurpose_ReturnsNull()
    {
        var code = _store.Issue("handoff", "42", TimeSpan.FromMinutes(1));

        _store.Redeem("state", code).Should().BeNull();
        // and the code is not consumed by the failed attempt
        _store.Redeem("handoff", code).Should().Be("42");
    }

    [Fact(DisplayName = "an unknown code redeems to nothing")]
    public void Redeem_UnknownCode_ReturnsNull()
    {
        _store.Redeem("handoff", "no-such-code").Should().BeNull();
    }

    [Fact(DisplayName = "an expired code redeems to nothing")]
    public async Task Redeem_AfterExpiry_ReturnsNull()
    {
        var code = _store.Issue("handoff", "42", TimeSpan.FromMilliseconds(50));

        await Task.Delay(200);

        _store.Redeem("handoff", code).Should().BeNull();
    }

    [Fact(DisplayName = "issued codes are unique and URL-safe")]
    public void Issue_GeneratesUniqueUrlSafeCodes()
    {
        var first = _store.Issue("state", "a", TimeSpan.FromMinutes(1));
        var second = _store.Issue("state", "b", TimeSpan.FromMinutes(1));

        first.Should().NotBe(second);
        first.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }
}
