using Api.Options;
using Api.Services.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Api.Tests.Unit;

public class AuthFlowServiceTests
{
    private readonly IExternalAuthProvider _provider = Substitute.For<IExternalAuthProvider>();
    private readonly IOneTimeCodeStore _codeStore = Substitute.For<IOneTimeCodeStore>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IRefreshTokenService _refreshTokenService = Substitute.For<IRefreshTokenService>();
    private readonly AuthFlowService _sut;

    private static readonly UserRecord User = new(42, "okta", "sub-1", "user@example.com", "Test User");

    public AuthFlowServiceTests()
    {
        _provider.Name.Returns("okta");
        _sut = new AuthFlowService(
            [_provider],
            _codeStore,
            _tokenService,
            _userService,
            _refreshTokenService,
            Microsoft.Extensions.Options.Options.Create(new AuthOptions
            {
                FrontendBaseUrl = "http://localhost:5173",
                ApiBaseUrl = "https://api.example",
            }));
    }

    [Fact]
    public void BeginLogin_UnknownProvider_ReturnsNull()
    {
        _sut.BeginLogin("google").Should().BeNull();
    }

    [Fact]
    public void BeginLogin_BuildsAuthorizationUrlWithIssuedState()
    {
        _codeStore.Issue("state", Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns("state-code");
        _provider.BuildAuthorizationUrl("state-code", Arg.Any<string>(), "https://api.example/auth/callback/okta")
            .Returns("https://idp.example/authorize");

        var url = _sut.BeginLogin("okta");

        url.Should().Be("https://idp.example/authorize");
        _codeStore.Received(1).Issue(
            "state",
            Arg.Is<string>(payload => payload.StartsWith("okta|")),
            Arg.Any<TimeSpan>());
    }

    [Fact]
    public void BeginLogin_IsCaseInsensitiveOnProviderName()
    {
        _codeStore.Issue("state", Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns("state-code");
        _provider.BuildAuthorizationUrl(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("https://idp.example/authorize");

        _sut.BeginLogin("OKTA").Should().NotBeNull();
    }

    [Fact]
    public async Task HandleCallback_UnknownProvider_ReturnsNull()
    {
        var result = await _sut.HandleCallbackAsync("google", "code", "state");

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleCallback_InvalidState_ReturnsNullWithoutCallingProvider()
    {
        _codeStore.Redeem("state", "bad-state").Returns((string?)null);

        var result = await _sut.HandleCallbackAsync("okta", "code", "bad-state");

        result.Should().BeNull();
        await _provider.DidNotReceiveWithAnyArgs()
            .ExchangeCodeAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task HandleCallback_StateForDifferentProvider_ReturnsNull()
    {
        _codeStore.Redeem("state", "state-code").Returns("google|verifier");

        var result = await _sut.HandleCallbackAsync("okta", "code", "state-code");

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleCallback_ExchangeFails_ReturnsNull()
    {
        _codeStore.Redeem("state", "state-code").Returns("okta|verifier");
        _provider.ExchangeCodeAsync("code", "verifier", Arg.Any<string>())
            .Returns((ExternalUserInfo?)null);

        var result = await _sut.HandleCallbackAsync("okta", "code", "state-code");

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleCallback_Success_UpsertsUserAndIssuesHandoffCode()
    {
        var externalUser = new ExternalUserInfo("okta", "sub-1", "user@example.com", "Test User");
        _codeStore.Redeem("state", "state-code").Returns("okta|verifier");
        _provider.ExchangeCodeAsync("code", "verifier", "https://api.example/auth/callback/okta")
            .Returns(externalUser);
        _userService.UpsertExternalUserAsync(externalUser).Returns(User);
        _codeStore.Issue("handoff", "42", Arg.Any<TimeSpan>()).Returns("handoff-code");

        var result = await _sut.HandleCallbackAsync("okta", "code", "state-code");

        result.Should().Be("handoff-code");
        _codeStore.Received(1).Issue(
            "handoff", "42", Arg.Is<TimeSpan>(ttl => ttl <= TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task ExchangeHandoffCode_InvalidCode_ReturnsNull()
    {
        _codeStore.Redeem("handoff", "bad").Returns((string?)null);

        var result = await _sut.ExchangeHandoffCodeAsync("bad");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExchangeHandoffCode_UnknownUser_ReturnsNull()
    {
        _codeStore.Redeem("handoff", "code").Returns("42");
        _userService.GetByIdAsync(42).Returns((UserRecord?)null);

        var result = await _sut.ExchangeHandoffCodeAsync("code");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExchangeHandoffCode_Success_MintsAccessAndRefreshTokens()
    {
        _codeStore.Redeem("handoff", "code").Returns("42");
        _userService.GetByIdAsync(42).Returns(User);
        _refreshTokenService.IssueAsync(42).Returns("refresh-raw");
        _tokenService.CreateAccessToken(User).Returns(("access-jwt", 900));

        var result = await _sut.ExchangeHandoffCodeAsync("code");

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("access-jwt");
        result.ExpiresInSeconds.Should().Be(900);
        result.RefreshToken.Should().Be("refresh-raw");
        result.User.UserId.Should().Be(42);
        result.User.Provider.Should().Be("okta");
        result.User.Email.Should().Be("user@example.com");
        result.User.DisplayName.Should().Be("Test User");
    }

    [Fact]
    public async Task Refresh_InvalidToken_ReturnsNull()
    {
        _refreshTokenService.ValidateAndRotateAsync("bad")
            .Returns(((UserRecord, string)?)null);

        var result = await _sut.RefreshAsync("bad");

        result.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_ValidToken_ReturnsRotatedPair()
    {
        _refreshTokenService.ValidateAndRotateAsync("old-raw").Returns((User, "new-raw"));
        _tokenService.CreateAccessToken(User).Returns(("access-jwt", 900));

        var result = await _sut.RefreshAsync("old-raw");

        result.Should().NotBeNull();
        result!.RefreshToken.Should().Be("new-raw");
        result.AccessToken.Should().Be("access-jwt");
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        await _sut.LogoutAsync("refresh-raw");

        await _refreshTokenService.Received(1).RevokeAsync("refresh-raw");
    }
}
