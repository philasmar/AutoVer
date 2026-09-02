using AutoVer.Exceptions;
using AutoVer.Models;

namespace AutoVer.UnitTests;

public class VersionTagFormatTest
{
    private static readonly DateTime ReleaseDate = new(2026, 9, 2, 13, 45, 0, DateTimeKind.Utc);

    private static ThreePartVersion Version(int major, int minor, int patch, string? prereleaseLabel = null) =>
        new() { Major = major, Minor = minor, Patch = patch, PrereleaseLabel = prereleaseLabel };

    private static VersionTagFormat Parse(string format) => VersionTagFormat.Parse(format, "TagFormat");

    // The defaults have to keep producing byte-for-byte what AutoVer produced before the format
    // was configurable, otherwise every repo already using it would silently start tagging
    // differently on its next release.
    [Test]
    [Arguments(1, "release_2026-09-02")]
    [Arguments(5, "release_2026-09-02_5")]
    public async Task DefaultTagFormat_MatchesPreConfigurableBehavior(int iteration, string expected)
    {
        var format = Parse(UserConfiguration.DefaultTagFormat);

        await Assert.That(format.Render(null, ReleaseDate, iteration)).IsEqualTo(expected);
        await Assert.That(format.Family).IsEqualTo(TagFormatFamily.Date);
    }

    [Test]
    [Arguments(1, "Release 2026-09-02")]
    [Arguments(5, "Release 2026-09-02 #5")]
    public async Task DefaultReleaseNameFormat_MatchesPreConfigurableBehavior(int iteration, string expected)
    {
        var format = VersionTagFormat.Parse(UserConfiguration.DefaultReleaseNameFormat, "ReleaseNameFormat");

        await Assert.That(format.Render(null, ReleaseDate, iteration)).IsEqualTo(expected);
    }

    [Test]
    public async Task SemverFormat_OmitsOptionalGroupsWithoutValues()
    {
        var format = Parse("v{major}.{minor}.{patch}[-{prerelease}][-{iteration}]");

        await Assert.That(format.Family).IsEqualTo(TagFormatFamily.Semver);
        await Assert.That(format.Render(Version(1, 4, 0), ReleaseDate, 1)).IsEqualTo("v1.4.0");
        await Assert.That(format.Render(Version(1, 4, 0, "beta.1"), ReleaseDate, 1)).IsEqualTo("v1.4.0-beta.1");
        await Assert.That(format.Render(Version(1, 4, 0), ReleaseDate, 2)).IsEqualTo("v1.4.0-2");
        await Assert.That(format.Render(Version(1, 4, 0, "beta.1"), ReleaseDate, 3)).IsEqualTo("v1.4.0-beta.1-3");
    }

    [Test]
    public async Task DateFormat_HonorsNetFormatSpecifiers()
    {
        await Assert.That(Parse("release_{date:yyyyMMdd}").Render(null, ReleaseDate, 1)).IsEqualTo("release_20260902");
        await Assert.That(Parse("{year}.{month}.{day}").Render(null, ReleaseDate, 1)).IsEqualTo("2026.09.02");
        await Assert.That(Parse("{year:yy}{month:MMM}{day:d}").Render(null, ReleaseDate, 1)).IsEqualTo("26Sep2");
    }

    [Test]
    public async Task EscapedStructuralCharacters_RenderLiterally()
    {
        await Assert.That(Parse("v{{{major}.{minor}.{patch}}}").Render(Version(1, 4, 0), ReleaseDate, 1))
            .IsEqualTo("v{1.4.0}");
        await Assert.That(Parse("[[{major}.{minor}.{patch}]]").Render(Version(1, 4, 0), ReleaseDate, 1))
            .IsEqualTo("[1.4.0]");
    }

    [Test]
    [Arguments("v{major}.{minor}.{patch}[-{prerelease}][-{iteration}]", "v1.4.0")]
    [Arguments("v{major}.{minor}.{patch}[-{prerelease}][-{iteration}]", "v1.4.0-beta.1")]
    [Arguments("v{major}.{minor}.{patch}[-{prerelease}][-{iteration}]", "v1.4.0-beta.1-2")]
    [Arguments("v{major}.{minor}.{patch}[-{prerelease}][-{iteration}]", "v1.4.0-3")]
    [Arguments("release_{date}[_{iteration}]", "release_2026-09-02")]
    [Arguments("release_{date}[_{iteration}]", "release_2026-09-02_7")]
    [Arguments("release_{date:yyyyMMdd}", "release_20260902")]
    [Arguments("{year}.{month}.{day}", "2026.09.02")]
    [Arguments("{year:yy}{month:MMM}{day:dd}", "26Sep02")]
    [Arguments("{month:MMMM}-{day}-{year}", "September-02-2026")]
    [Arguments("release_{date:yyyy/MM/dd}", "release_2026/09/02")]
    public async Task RenderedTag_RoundTripsBackThroughTheSameFormat(string formatString, string tag)
    {
        var format = Parse(formatString);

        await Assert.That(format.TryParseTag(tag, out var parsed)).IsTrue();
        await Assert.That(parsed!.Raw).IsEqualTo(tag);
        await Assert.That(format.Render(parsed.Version, parsed.Date ?? ReleaseDate, parsed.Iteration)).IsEqualTo(tag);
    }

    // A format is free to place {prerelease} outside an optional group (a repo that always sets a
    // prerelease label, say). Whatever that renders still has to be readable back, otherwise the
    // tag AutoVer just wrote could never be found again and every release would look like the first.
    [Test]
    [Arguments("beta.1", "v1.4.0-beta.1")]
    [Arguments(null, "v1.4.0-")]
    public async Task NonOptionalPrerelease_RendersAndReadsBackEitherWay(string? prereleaseLabel, string expected)
    {
        var format = Parse("v{major}.{minor}.{patch}-{prerelease}");
        var rendered = format.Render(Version(1, 4, 0, prereleaseLabel), ReleaseDate, 1);

        await Assert.That(rendered).IsEqualTo(expected);
        await Assert.That(format.TryParseTag(rendered, out var parsed)).IsTrue();
        await Assert.That(parsed!.Version!.PrereleaseLabel).IsEqualTo(prereleaseLabel);
    }

    // A greedy prerelease group would swallow the iteration suffix and report iteration 1.
    [Test]
    public async Task TrailingIteration_IsNotAbsorbedByThePrereleaseLabel()
    {
        var format = Parse("v{major}.{minor}.{patch}[-{prerelease}][-{iteration}]");

        await Assert.That(format.TryParseTag("v1.4.0-beta.1-2", out var parsed)).IsTrue();
        await Assert.That(parsed!.Version!.PrereleaseLabel).IsEqualTo("beta.1");
        await Assert.That(parsed.Iteration).IsEqualTo(2);
    }

    [Test]
    [Arguments("release_{date}[_{iteration}]", "v1.4.0")]
    [Arguments("release_{date}[_{iteration}]", "release_2026-13-45")]
    [Arguments("release_{date}[_{iteration}]", "release_2026-09-02-extra")]
    [Arguments("v{major}.{minor}.{patch}", "v1.4")]
    [Arguments("v{major}.{minor}.{patch}", "1.4.0")]
    public async Task NonMatchingTag_IsRejected(string formatString, string tag)
    {
        await Assert.That(Parse(formatString).TryParseTag(tag, out _)).IsFalse();
    }

    [Test]
    public async Task SupportsIteration_ReflectsWhetherTheFormatCanRepresentARepeatRelease()
    {
        await Assert.That(Parse("v{major}.{minor}.{patch}").SupportsIteration).IsFalse();
        await Assert.That(Parse("v{major}.{minor}.{patch}[-{iteration}]").SupportsIteration).IsTrue();
        await Assert.That(Parse(UserConfiguration.DefaultTagFormat).SupportsIteration).IsTrue();
    }

    [Test]
    // Mixed families - the whole reason ordering would be ambiguous.
    [Arguments("v{major}.{minor}.{patch}-{date}")]
    [Arguments("{year}.{month}.{day}-{prerelease}")]
    // Incomplete ordering keys.
    [Arguments("v{major}.{minor}")]
    [Arguments("{month}-{day}")]
    [Arguments("release_{date:yyyy}")]
    [Arguments("release_{date:yyyy-MM}")]
    // No ordering key at all.
    [Arguments("release")]
    [Arguments("release_{iteration}")]
    // Structural problems.
    [Arguments("v{major}.{minor}.{patch")]
    [Arguments("v{major}.{minor}.{patch}}")]
    [Arguments("v{major}.{minor}.{patch}[-{prerelease}")]
    [Arguments("v{major}.{minor}.{patch}-{prerelease}]")]
    [Arguments("v{major}.{minor}.{patch}[[-{prerelease}]")]
    [Arguments("v{major}.{minor}.{patch}[-{iteration}[-{prerelease}]]")]
    // Placeholders that can't be absent don't belong in an optional group.
    [Arguments("v{major}.{minor}[.{patch}]")]
    [Arguments("release_[{date}]")]
    // An optional group with nothing to key off.
    [Arguments("v{major}.{minor}.{patch}[-rc]")]
    // Duplicated placeholders can't be read back unambiguously.
    [Arguments("v{major}.{minor}.{patch}-{major}")]
    // Unknown placeholder, and a format on something that isn't a date.
    [Arguments("v{major}.{minor}.{patch}-{revision}")]
    [Arguments("v{major:00}.{minor}.{patch}")]
    // Date specifiers that render but can't be matched back.
    [Arguments("release_{date:yyyy-MM-dd HH:mm}")]
    [Arguments("release_{year}-{month}-{day:ddd}")]
    // An empty format suffix isn't "no suffix" - .NET reads "" as the general date/time pattern.
    [Arguments("release_{date:}")]
    [Arguments("release_{year:}-{month}-{day}")]
    // {date} already covers the whole date; combining it with components produces a composite
    // format with repeated specifiers that can't be parsed back.
    [Arguments("release_{date}-{year}")]
    [Arguments("release_{date}_{month}{day}")]
    // Ambiguous: adjacent variable-width placeholders can't be read back unchanged.
    [Arguments("{major}{minor}{patch}")]
    [Arguments("v{major}.{minor}.{patch}{iteration}")]
    [Arguments("{year}-{month:M}{day:d}")]
    [Arguments("v{major}.{minor}.{patch}-{prerelease}{iteration}")]
    public async Task InvalidFormat_IsRejectedAtParseTime(string formatString)
    {
        await Assert.That(() => Parse(formatString)).Throws<InvalidUserConfigurationException>();
    }

    // The invariant culture reads a two-digit year against a window ending in 2049, which would
    // read a 2050 release's "50" back as 1950 - sorting it below every existing release.
    [Test]
    [Arguments(2026)]
    [Arguments(2049)]
    [Arguments(2050)]
    [Arguments(2099)]
    public async Task TwoDigitYear_RoundTripsPastTheInvariantCulturePivot(int year)
    {
        var format = Parse("{year:yy}.{month}.{day}");
        var date = new DateTime(year, 1, 12);

        var rendered = format.Render(null, date, 1);

        await Assert.That(format.TryParseTag(rendered, out var parsed)).IsTrue();
        await Assert.That(parsed!.Date).IsEqualTo(date);
    }

    [Test]
    public async Task EmptyFormat_IsRejected()
    {
        await Assert.That(() => Parse("")).Throws<InvalidUserConfigurationException>();
        await Assert.That(() => Parse("   ")).Throws<InvalidUserConfigurationException>();
    }
}
