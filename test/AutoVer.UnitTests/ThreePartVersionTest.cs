using AutoVer.Models;

namespace AutoVer.UnitTests;

public class ThreePartVersionTest
{
    private static ThreePartVersion Parse(string version) => ThreePartVersion.Parse(version);

    [Test]
    [Arguments("1.0.0", "2.0.0")]
    [Arguments("2.0.0", "2.1.0")]
    [Arguments("2.1.0", "2.1.1")]
    public async Task CompareTo_OrdersByMajorThenMinorThenPatch(string lower, string higher)
    {
        await Assert.That(Parse(lower).CompareTo(Parse(higher))).IsLessThan(0);
        await Assert.That(Parse(higher).CompareTo(Parse(lower))).IsGreaterThan(0);
    }

    // SemVer 2.0.0 §11. A plain ordinal string comparison gets the first two of these backwards,
    // which previously made GetNextMaxVersion pick the wrong "highest" version.
    [Test]
    [Arguments("1.4.0-beta.2", "1.4.0-beta.10")]
    [Arguments("1.4.0-beta.1", "1.4.0")]
    [Arguments("1.0.0-alpha", "1.0.0-alpha.1")]
    [Arguments("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [Arguments("1.0.0-alpha.beta", "1.0.0-beta")]
    [Arguments("1.0.0-beta", "1.0.0-beta.2")]
    [Arguments("1.0.0-beta.2", "1.0.0-beta.11")]
    [Arguments("1.0.0-beta.11", "1.0.0-rc.1")]
    [Arguments("1.0.0-rc.1", "1.0.0")]
    public async Task CompareTo_FollowsSemVerPrereleasePrecedence(string lower, string higher)
    {
        await Assert.That(Parse(lower).CompareTo(Parse(higher))).IsLessThan(0);
        await Assert.That(Parse(higher).CompareTo(Parse(lower))).IsGreaterThan(0);
        await Assert.That(Parse(lower) < Parse(higher)).IsTrue();
        await Assert.That(Parse(higher) > Parse(lower)).IsTrue();
    }

    [Test]
    [Arguments("1.4.0")]
    [Arguments("1.4.0-beta.1")]
    public async Task CompareTo_IsZeroForEqualVersions(string version)
    {
        await Assert.That(Parse(version).CompareTo(Parse(version))).IsEqualTo(0);
    }

    // A prerelease label may itself contain hyphens (SemVer: everything after the first '-'), and a
    // version-based tag is rendered from this parse - so dropping part of the label would put the
    // wrong name on an immutable tag.
    [Test]
    [Arguments("1.0.0", null)]
    [Arguments("1.0.0-rc.1", "rc.1")]
    [Arguments("1.0.0-alpha-1", "alpha-1")]
    [Arguments("1.0.0-alpha.beta-2", "alpha.beta-2")]
    public async Task Parse_KeepsTheWholePrereleaseLabel(string version, string? expectedLabel)
    {
        var parsed = Parse(version);

        await Assert.That(parsed.PrereleaseLabel).IsEqualTo(expectedLabel);
        await Assert.That(parsed.ToString()).IsEqualTo(version);
    }

    [Test]
    public async Task CompareTo_TreatsNumericPrereleaseIdentifiersNumerically()
    {
        // Large enough that a digit-by-digit string comparison would disagree with the numeric one.
        await Assert.That(Parse("1.0.0-9").CompareTo(Parse("1.0.0-100"))).IsLessThan(0);
    }
}
