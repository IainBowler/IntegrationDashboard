using Api.Services.Auth;
using FluentAssertions;

namespace Api.Tests.Unit;

public class TestAuthProviderTests
{
    private readonly TestAuthProvider _sut = new();

    [Fact]
    public void BuildAuthorizationUrl_RedirectsStraightBackToCallbackWithState()
    {
        var url = _sut.BuildAuthorizationUrl(
            "state-1", "challenge-1", "https://api.example/auth/callback/test");

        url.Should().StartWith("https://api.example/auth/callback/test?");
        url.Should().Contain("code=test-code");
        url.Should().Contain("state=state-1");
    }

    [Fact]
    public async Task ExchangeCode_WithTestCode_ReturnsStableTestUser()
    {
        var result = await _sut.ExchangeCodeAsync("test-code", "verifier", "https://api.example/cb");

        result.Should().Be(new ExternalUserInfo("test", "e2e-subject", "e2e@example.com", "E2E Test User"));
    }

    [Fact]
    public async Task ExchangeCode_WithAnyOtherCode_ReturnsNull()
    {
        var result = await _sut.ExchangeCodeAsync("stolen-code", "verifier", "https://api.example/cb");

        result.Should().BeNull();
    }
}
