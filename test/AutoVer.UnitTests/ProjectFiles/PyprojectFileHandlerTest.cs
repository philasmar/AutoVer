using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services;
using AutoVer.Services.IO;
using AutoVer.Services.ProjectFiles;

namespace AutoVer.UnitTests.ProjectFiles;

public class PyprojectFileHandlerTest
{
    private const string Basic =
"""
[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"

[project]
name = "walleye-datalake"
version = "1.2.3"
description = "A package"
dependencies = ["requests>=2.28"]

[project.urls]
Homepage = "https://example.invalid"

[tool.pytest.ini_options]
testpaths = ["tests"]
""";

    private string _tempDir = string.Empty;

    [Before(Test)]
    public void Before()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [After(Test)]
    public void After()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static PyprojectFileHandler CreateHandler() => new(new ThreePartVersionIncrementer(), new FileManager(new CurrentDirectoryContext()));

    private async Task<string> Bump(string content, IncrementType increment = IncrementType.Patch, string? prereleaseLabel = null, string? overrideVersion = null)
    {
        var handler = CreateHandler();
        var path = Path.Combine(_tempDir, "pyproject.toml");
        await File.WriteAllTextAsync(path, content);
        var definition = handler.Load(path, await File.ReadAllTextAsync(path));
        handler.UpdateVersion(definition, increment, prereleaseLabel, overrideVersion);
        return await File.ReadAllTextAsync(path);
    }

    // ── matching ─────────────────────────────────────────────────────────────────────────

    [Test]
    [Arguments("pyproject.toml", true)]
    [Arguments("PyProject.toml", true)]
    [Arguments("src/pkg/pyproject.toml", true)]
    [Arguments("setup.cfg", false)]
    [Arguments("Cargo.toml", false)]
    [Arguments("pyproject.toml.bak", false)]
    [Arguments("my-pyproject.toml", false)]
    public async Task IsMatch_OnlyPyprojectToml(string fileName, bool expected)
    {
        await Assert.That(CreateHandler().IsMatch(fileName)).IsEqualTo(expected);
    }

    // A tool-config-only pyproject.toml is common in repositories that are not Python packages;
    // discovering it would start versioning it in every repository that relies on discovery.
    [Test]
    public async Task SearchPatterns_IsEmpty_SoPyprojectIsNeverAutoDiscovered()
    {
        await Assert.That(CreateHandler().SearchPatterns).IsEmpty();
    }

    // ── reading ──────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Load_ReadsProjectVersion()
    {
        var definition = CreateHandler().Load("pyproject.toml", Basic);
        await Assert.That(definition.Version).IsEqualTo("1.2.3");
        await Assert.That(definition.Contents).IsTypeOf<List<string>>();
    }

    [Test]
    [Arguments("version = '1.2.3'")]
    [Arguments("version=\"1.2.3\"")]
    [Arguments("\"version\" = \"1.2.3\"")]
    [Arguments("'version' = '1.2.3'")]
    [Arguments("  version   =   \"1.2.3\"   # managed by AutoVer")]
    public async Task Load_AcceptsTomlSpellings(string versionLine)
    {
        var definition = CreateHandler().Load("pyproject.toml", $"[project]\nname = \"x\"\n{versionLine}\n");
        await Assert.That(definition.Version).IsEqualTo("1.2.3");
    }

    [Test]
    [Arguments("[ project ]")]
    [Arguments("[\"project\"]")]
    [Arguments("[project]  # metadata")]
    public async Task Load_AcceptsHeaderSpellings(string header)
    {
        var definition = CreateHandler().Load("pyproject.toml", $"{header}\nname = \"x\"\nversion = \"1.2.3\"\n");
        await Assert.That(definition.Version).IsEqualTo("1.2.3");
    }

    [Test]
    public async Task Load_IgnoresVersionKeysOutsideTheProjectTable()
    {
        const string content =
"""
[tool.poetry]
version = "9.9.9"

[project]
name = "x"
version = "1.2.3"

[project.optional-dependencies]
version = ["9.9.9"]

[[tool.mytool.items]]
version = "9.9.9"
""";
        await Assert.That(CreateHandler().Load("pyproject.toml", content).Version).IsEqualTo("1.2.3");
    }

    [Test]
    public async Task Load_IgnoresVersionTextInsideMultilineValues()
    {
        const string content =
"""
[project]
name = "x"
description = '''
version = "9.9.9"
[project]
'''
classifiers = [
  "version = 9.9.9",
]
version = "1.2.3"
""";
        await Assert.That(CreateHandler().Load("pyproject.toml", content).Version).IsEqualTo("1.2.3");
    }

    [Test]
    public async Task Load_NoVersionKey_VersionIsNull()
    {
        await Assert.That(CreateHandler().Load("pyproject.toml", "[project]\nname = \"x\"\n").Version).IsNull();
    }

    // ── refusals ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Load_NoProjectTable_Throws()
    {
        var ex = Assert.Throws<InvalidProjectException>(() => CreateHandler().Load("pyproject.toml", "[tool.ruff]\nline-length = 100\n"));
        await Assert.That(ex!.Message).Contains("no [project] table");
    }

    [Test]
    public async Task Load_PoetryOnly_ExplainsPoetryIsNotSupported()
    {
        var ex = Assert.Throws<InvalidProjectException>(() => CreateHandler().Load("pyproject.toml", "[tool.poetry]\nname = \"x\"\nversion = \"1.2.3\"\n"));
        await Assert.That(ex!.Message).Contains("Poetry");
    }

    [Test]
    [Arguments("dynamic = [\"version\"]")]
    [Arguments("dynamic = ['readme', 'version']")]
    [Arguments("dynamic = [\n  \"readme\",\n  \"version\",  # from git\n]")]
    public async Task Load_DynamicVersion_Throws(string dynamic)
    {
        var ex = Assert.Throws<InvalidProjectException>(() => CreateHandler().Load("pyproject.toml", $"[project]\nname = \"x\"\n{dynamic}\n"));
        await Assert.That(ex!.Message).Contains("dynamic");
    }

    [Test]
    public async Task Load_DynamicWithoutVersion_IsFine()
    {
        var definition = CreateHandler().Load("pyproject.toml", "[project]\nname = \"x\"\nversion = \"1.2.3\"\ndynamic = [\"readme\"]\n");
        await Assert.That(definition.Version).IsEqualTo("1.2.3");
    }

    [Test]
    [Arguments("version = 1.2")]
    [Arguments("version = { attr = \"pkg.__version__\" }")]
    [Arguments("version = \"\"\"1.0.0\"\"\"")]  // was read as "" and overwritten into invalid TOML
    [Arguments("version = '''1.0.0'''")]
    [Arguments("version = \"1.0.0\" junk")]
    public async Task Load_NonStringVersion_Throws(string versionLine)
    {
        var ex = Assert.Throws<InvalidProjectException>(() => CreateHandler().Load("pyproject.toml", $"[project]\nname = \"x\"\n{versionLine}\n"));
        await Assert.That(ex!.Message).Contains("not a plain single-line string");
    }

    [Test]
    public async Task Load_DuplicateVersionOrTable_Throws()
    {
        Assert.Throws<InvalidProjectException>(() => CreateHandler().Load("pyproject.toml", "[project]\nversion = \"1\"\nversion = \"2\"\n"));
        Assert.Throws<InvalidProjectException>(() => CreateHandler().Load("pyproject.toml", "[project]\nname = \"a\"\n[project]\nname = \"b\"\n"));
        await Task.CompletedTask;
    }

    // ── writing ──────────────────────────────────────────────────────────────────────────

    [Test]
    [Arguments(IncrementType.Patch, "1.2.4")]
    [Arguments(IncrementType.Minor, "1.3.0")]
    [Arguments(IncrementType.Major, "2.0.0")]
    public async Task UpdateVersion_ChangesOnlyTheVersionValue(IncrementType increment, string expected)
    {
        var written = await Bump(Basic, increment);
        await Assert.That(written).IsEqualTo(Basic.Replace("version = \"1.2.3\"", $"version = \"{expected}\""));
    }

    [Test]
    public async Task UpdateVersion_KeepsQuoteStyleSpacingAndComment()
    {
        var written = await Bump("[project]\nname = 'x'\n  'version'  =  '1.2.3'   # bumped by release\n");
        await Assert.That(written).IsEqualTo("[project]\nname = 'x'\n  'version'  =  '1.2.4'   # bumped by release\n");
    }

    [Test]
    public async Task UpdateVersion_KeepsCrlfLineEndings()
    {
        var written = await Bump("[project]\r\nname = \"x\"\r\nversion = \"1.2.3\"\r\n");
        await Assert.That(written).IsEqualTo("[project]\r\nname = \"x\"\r\nversion = \"1.2.4\"\r\n");
    }

    [Test]
    public async Task UpdateVersion_Override_WritesThatVersion()
    {
        var written = await Bump(Basic, overrideVersion: "3.0.0");
        await Assert.That(written).Contains("version = \"3.0.0\"");
    }

    [Test]
    [Arguments("beta.1", "1.2.4-beta.1")]
    [Arguments("rc.2", "1.2.4-rc.2")]
    [Arguments("dev.1", "1.2.4-dev.1")]
    public async Task UpdateVersion_Pep440PrereleaseLabels_AreWritten(string label, string expected)
    {
        var written = await Bump(Basic, prereleaseLabel: label);
        await Assert.That(written).Contains($"version = \"{expected}\"");
    }

    [Test]
    [Arguments("hotfix")]
    [Arguments("feature-x")]
    [Arguments("beta.1.2")]
    [Arguments("post.1")]   // a post-release: pip ranks it ABOVE 1.2.4 and installs it by default
    [Arguments("rev.2")]
    [Arguments("1")]        // 1.2.4-1 is 1.2.4.post1 to pip
    [Arguments("rc.1\n")]  // would write a raw newline into the TOML string
    public async Task UpdateVersion_LabelPipWouldReject_ThrowsAndLeavesFileUntouched(string label)
    {
        var handler = CreateHandler();
        var path = Path.Combine(_tempDir, "pyproject.toml");
        await File.WriteAllTextAsync(path, Basic);
        var definition = handler.Load(path, Basic);

        var ex = Assert.Throws<InvalidProjectException>(() => handler.UpdateVersion(definition, IncrementType.Patch, label));
        await Assert.That(ex!.Message).Contains("PEP 440");
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(Basic);
        Assert.Throws<InvalidProjectException>(() => handler.ValidateVersion(definition, IncrementType.Patch, label));
    }

    // Refused at Load: every caller parses a loaded version before asking the handler to write,
    // so a check at write time would never be reached (review finding).
    [Test]
    public async Task Load_NormalizedPythonSpelling_ExplainsHowToWriteIt()
    {
        var ex = Assert.Throws<InvalidProjectException>(() => CreateHandler().Load("pyproject.toml", "[project]\nname = \"x\"\nversion = \"1.2.3b1\"\n"));
        await Assert.That(ex!.Message).Contains("1.2.3-b1");
    }

    [Test]
    public async Task UpdateVersion_UpdatesTheInMemoryDefinition()
    {
        var handler = CreateHandler();
        var path = Path.Combine(_tempDir, "pyproject.toml");
        await File.WriteAllTextAsync(path, Basic);
        var definition = handler.Load(path, Basic);
        handler.UpdateVersion(definition, IncrementType.Minor);
        await Assert.That(definition.Version).IsEqualTo("1.3.0");
    }

    // ── seeding ──────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task UpdateVersion_NoVersion_IsSeededRightAfterName()
    {
        var written = await Bump("[project]\nname = \"x\"\ndescription = \"d\"\n", overrideVersion: "0.1.0");
        await Assert.That(written).IsEqualTo("[project]\nname = \"x\"\nversion = \"0.1.0\"\ndescription = \"d\"\n");
    }

    // Without a name key the version goes after the table's last value - which must be where
    // that value ENDS, never inside a multi-line array or string.
    [Test]
    public async Task UpdateVersion_NoVersionNoName_IsSeededAfterTheLastValueNotInsideIt()
    {
        const string content = "[project]\ndependencies = [\n  \"requests\",\n]\n\n[tool.x]\na = 1\n";
        var written = await Bump(content, overrideVersion: "0.1.0");
        await Assert.That(written).IsEqualTo("[project]\ndependencies = [\n  \"requests\",\n]\nversion = \"0.1.0\"\n\n[tool.x]\na = 1\n");
        await Assert.That(CreateHandler().Load("pyproject.toml", written).Version).IsEqualTo("0.1.0");
    }

    [Test]
    public async Task UpdateVersion_SeedKeepsCrlf()
    {
        var written = await Bump("[project]\r\nname = \"x\"\r\n", overrideVersion: "0.1.0");
        await Assert.That(written).IsEqualTo("[project]\r\nname = \"x\"\r\nversion = \"0.1.0\"\r\n");
    }

    // ── review findings ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task UpdateVersion_EmptyVersion_IsSeeded()
    {
        var written = await Bump("[project]\nname = \"x\"\nversion = \"\"\n", overrideVersion: "0.1.0");
        await Assert.That(written).IsEqualTo("[project]\nname = \"x\"\nversion = \"0.1.0\"\n");
    }

    // A ']' inside a string (dependency extras) or a nested array must not end the value early.
    [Test]
    public async Task UpdateVersion_NoName_SeedsAfterArraysWithBracketsInStringsAndNesting()
    {
        const string content = "[project]\ndependencies = [\n  \"requests[socks]>=2\",\n  \"b\",\n]\nmatrix = [\n  [1, 2],\n  [3],\n]\n\n[tool.x]\na = 1\n";
        var written = await Bump(content, overrideVersion: "0.1.0");
        await Assert.That(written).IsEqualTo(content.Replace("  [3],\n]\n", "  [3],\n]\nversion = \"0.1.0\"\n"));
    }

    // Unbalanced brackets and '#' inside strings: only string-aware scanning keeps the array open.
    [Test]
    public async Task UpdateVersion_NoName_UnbalancedBracketsInStringsDoNotCloseTheArray()
    {
        const string content = "[project]\nkeywords = [\n  \"weird]\",\n  \"odd[\", \"x#y\",\n  \"b\",\n]\n";
        var written = await Bump(content, overrideVersion: "0.1.0");
        await Assert.That(written).IsEqualTo(content + "version = \"0.1.0\"\n");
    }

    [Test]
    public async Task Load_DottedKeyMultilineString_IsNotMistakenForAHeader()
    {
        const string content = "[tool.x]\ntemplates.header = \"\"\"\n[project]\n\"\"\"\n[project]\nname = \"x\"\nversion = \"1.0.0\"\n";
        await Assert.That(CreateHandler().Load("pyproject.toml", content).Version).IsEqualTo("1.0.0");
    }

    [Test]
    public async Task Load_DottedVersionKey_IsNotTheVersion()
    {
        await Assert.That(CreateHandler().Load("pyproject.toml", "[project]\nname = \"x\"\nversion.extra = \"9\"\n").Version).IsNull();
    }

    [Test]
    public async Task Load_EscapedQuotesAndHashesInStrings_DoNotConfuseTheScanner()
    {
        const string content = "[project]\nname = \"x\"\ndescription = \"a \\\" [x] # not a comment\"\nversion = \"1.0.0\"\n";
        await Assert.That(CreateHandler().Load("pyproject.toml", content).Version).IsEqualTo("1.0.0");
    }

    [Test]
    public async Task UpdateVersion_SeedAtEndOfCrlfFileWithoutTrailingNewline_KeepsCrlf()
    {
        var written = await Bump("[project]\r\nname = \"x\"", overrideVersion: "0.1.0");
        await Assert.That(written).IsEqualTo("[project]\r\nname = \"x\"\r\nversion = \"0.1.0\"");
    }

    [Test]
    public async Task UpdateVersion_SeedAtEndOfLfFileWithoutTrailingNewline()
    {
        var written = await Bump("[project]\nname = \"x\"", overrideVersion: "0.1.0");
        await Assert.That(written).IsEqualTo("[project]\nname = \"x\"\nversion = \"0.1.0\"");
    }

    // ── second review ──────────────────────────────────────────────────────────────────────

    // \""" inside a basic multi-line string is an escaped quote plus two quotes, not its end.
    [Test]
    public async Task Load_EscapedQuoteInMultilineString_DoesNotEndIt()
    {
        const string content = "[project]\nname = \"x\"\ndescription = \"\"\"a \\\"\"\" [\nversion = \"9.9.9\"\n\"\"\"\nversion = \"1.0.0\"\n";
        await Assert.That(CreateHandler().Load("pyproject.toml", content).Version).IsEqualTo("1.0.0");
        var written = await Bump(content);
        await Assert.That(written).IsEqualTo(content.Replace("version = \"1.0.0\"", "version = \"1.0.1\""));
    }

    [Test]
    public async Task Load_ClosingRunOfFiveQuotes_ClosesTheString()
    {
        const string content = "[project]\nname = \"x\"\ndescription = \"\"\"ends with two quotes\"\"\"\"\"\nversion = \"1.0.0\"\n";
        await Assert.That(CreateHandler().Load("pyproject.toml", content).Version).IsEqualTo("1.0.0");
    }

    // A seeded version goes after the END of a multi-line name, never inside it.
    [Test]
    public async Task UpdateVersion_MultilineName_SeedsAfterItsEnd()
    {
        var written = await Bump("[project]\nname = \"\"\"\nx\"\"\"\ndescription = \"d\"\n", overrideVersion: "0.1.0");
        await Assert.That(written).IsEqualTo("[project]\nname = \"\"\"\nx\"\"\"\nversion = \"0.1.0\"\ndescription = \"d\"\n");
    }

    [Test]
    public async Task Load_UnclosedValue_IsReportedAsInvalidToml()
    {
        var ex = Assert.Throws<InvalidProjectException>(() => CreateHandler().Load("pyproject.toml", "[tool.x]\na = [1,\n[project]\nversion = \"1.0.0\"\n"));
        await Assert.That(ex!.Message).Contains("line 2 is never closed");
    }

    [Test]
    public async Task Load_StrayTripleQuoteLine_IsTrackedNotIgnored()
    {
        var ex = Assert.Throws<InvalidProjectException>(() => CreateHandler().Load("pyproject.toml", "[project]\nname = \"x\"\n\"\"\"\nversion = \"1.0.0\"\n"));
        await Assert.That(ex!.Message).Contains("never closed");
    }
}
