using Api.Services.Auth;
using FluentAssertions;

namespace Api.Tests.Unit;

public class TokenHasherTests
{
    [Fact]
    public void Sha256Hex_ProducesKnownVector()
    {
        TokenHasher.Sha256Hex("abc").Should().Be(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void Sha256Hex_IsDeterministic()
    {
        TokenHasher.Sha256Hex("some-refresh-token").Should().Be(
            TokenHasher.Sha256Hex("some-refresh-token"));
    }

    [Fact]
    public void Sha256Hex_MatchesTokenHashColumnShape()
    {
        var hash = TokenHasher.Sha256Hex(Guid.NewGuid().ToString());

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
