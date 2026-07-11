using Api.Services.Auth;
using FluentAssertions;

namespace Api.Tests.Unit;

public class TokenHasherTests
{
    [Fact(DisplayName = "hashing produces the known SHA-256 test vector")]
    public void Sha256Hex_ProducesKnownVector()
    {
        TokenHasher.Sha256Hex("abc").Should().Be(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact(DisplayName = "hashing the same token twice gives the same hash")]
    public void Sha256Hex_IsDeterministic()
    {
        TokenHasher.Sha256Hex("some-refresh-token").Should().Be(
            TokenHasher.Sha256Hex("some-refresh-token"));
    }

    [Fact(DisplayName = "hashes are 64 lowercase hex characters, matching the TokenHash column")]
    public void Sha256Hex_MatchesTokenHashColumnShape()
    {
        var hash = TokenHasher.Sha256Hex(Guid.NewGuid().ToString());

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
