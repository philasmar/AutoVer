using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services;
using AutoVer.Services.IO;
using AutoVer.Services.ProjectFiles;

namespace AutoVer.UnitTests.ProjectFiles;

public class DockerfileFileHandlerTest
{
    private const string DockerfileWithQuotedLabel =
"""
FROM alpine:3.20
LABEL maintainer="team-datalake" org.opencontainers.image.version="1.2.3"
COPY . /app
CMD ["/app/run.sh"]
""";

    private const string DockerfileWithUnquotedLabel =
"""
FROM alpine:3.20
LABEL org.opencontainers.image.version=1.2.3
CMD ["/app/run.sh"]
""";

    private const string DockerfileWithoutLabel =
"""
FROM alpine:3.20
CMD ["/app/run.sh"]
""";

    private const string DockerfileWithCommentMentioningLabel =
"""
FROM alpine:3.20
# see org.opencontainers.image.version="0.0.0" below for the real value
LABEL org.opencontainers.image.version="1.2.3"
CMD ["/app/run.sh"]
""";

    private const string DockerfileWithLabelContinuation =
"""
FROM alpine:3.20
LABEL maintainer="team-datalake" \
      org.opencontainers.image.version="1.2.3"
CMD ["/app/run.sh"]
""";

    private const string MultiStageDockerfile =
"""
FROM alpine:3.20 AS build
LABEL org.opencontainers.image.version="1.2.3"
RUN echo building

FROM alpine:3.20
LABEL org.opencontainers.image.version="1.2.3"
CMD ["/app/run.sh"]
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

    private static DockerfileFileHandler CreateHandler() => new(new ThreePartVersionIncrementer(), new FileManager(new CurrentDirectoryContext()));

    [Test]
    [Arguments("Dockerfile", true)]
    [Arguments("dockerfile", true)]
    [Arguments("Dockerfile.prod", true)]
    [Arguments("Dockerfile.dev", true)]
    [Arguments("api.Dockerfile", true)]
    [Arguments("web.dockerfile", true)]
    [Arguments("docker-compose.yml", false)]
    [Arguments("Project1.csproj", false)]
    [Arguments("readme.md", false)]
    [Arguments("Dockerfile.orig", false)]
    [Arguments("Dockerfile.bak", false)]
    [Arguments("Dockerfile.swp", false)]
    [Arguments("Dockerfile.swo", false)]
    [Arguments("Dockerfile.tmp", false)]
    [Arguments("Dockerfile~", false)]
    public async Task IsMatch_MatchesDockerfileNamingConventions(string fileName, bool expected)
    {
        var handler = CreateHandler();
        await Assert.That(handler.IsMatch(fileName)).IsEqualTo(expected);
    }

    [Test]
    public async Task Load_ParsesQuotedVersionLabel()
    {
        var handler = CreateHandler();
        var definition = handler.Load("Dockerfile", DockerfileWithQuotedLabel);

        await Assert.That(definition.Version).IsEqualTo("1.2.3");
        await Assert.That(definition.Contents).IsTypeOf<List<string>>();
    }

    [Test]
    public async Task Load_ParsesUnquotedVersionLabel()
    {
        var handler = CreateHandler();
        var definition = handler.Load("Dockerfile", DockerfileWithUnquotedLabel);

        await Assert.That(definition.Version).IsEqualTo("1.2.3");
    }

    [Test]
    public async Task Load_NoLabel_VersionIsNull()
    {
        var handler = CreateHandler();
        var definition = handler.Load("Dockerfile", DockerfileWithoutLabel);

        await Assert.That(definition.Version).IsNull();
    }

    [Test]
    public async Task UpdateVersion_DefaultPatchIncrement_PersistsToDisk()
    {
        var handler = CreateHandler();
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, DockerfileWithQuotedLabel);

        var definition = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));
        handler.UpdateVersion(definition, IncrementType.Patch);

        var reloaded = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));
        await Assert.That(reloaded.Version).IsEqualTo("1.2.4");
    }

    [Test]
    public async Task UpdateVersion_MinorIncrement_ResetsPatch()
    {
        var handler = CreateHandler();
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, DockerfileWithQuotedLabel);

        var definition = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));
        handler.UpdateVersion(definition, IncrementType.Minor);

        var reloaded = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));
        await Assert.That(reloaded.Version).IsEqualTo("1.3.0");
    }

    [Test]
    public async Task UpdateVersion_PreservesOtherLinesAndOtherLabelsOnSameLine()
    {
        var handler = CreateHandler();
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, DockerfileWithQuotedLabel);

        var definition = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));
        handler.UpdateVersion(definition, IncrementType.Patch);

        var content = await File.ReadAllTextAsync(dockerfilePath);
        await Assert.That(content).Contains("FROM alpine:3.20");
        await Assert.That(content).Contains("maintainer=\"team-datalake\"");
        await Assert.That(content).Contains("COPY . /app");
        await Assert.That(content).Contains("CMD [\"/app/run.sh\"]");
        await Assert.That(content).Contains("org.opencontainers.image.version=\"1.2.4\"");
    }

    [Test]
    public async Task UpdateVersion_OverrideVersion_SetsExactVersion()
    {
        var handler = CreateHandler();
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, DockerfileWithQuotedLabel);

        var definition = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));
        handler.UpdateVersion(definition, IncrementType.Patch, overrideVersion: "9.9.9");

        var reloaded = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));
        await Assert.That(reloaded.Version).IsEqualTo("9.9.9");
    }

    [Test]
    public async Task UpdateVersion_InvalidOverrideVersion_ThrowsInvalidArgumentException()
    {
        var handler = CreateHandler();
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, DockerfileWithQuotedLabel);
        var definition = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));

        await Assert.That(() => handler.UpdateVersion(definition, IncrementType.Patch, overrideVersion: "not-a-version"))
            .Throws<InvalidArgumentException>();
    }

    [Test]
    // A Dockerfile with no version LABEL is seeded rather than rejected: the label is appended at
    // the end of the file, which puts it in the final build stage - the stage whose labels the built
    // image carries - and it takes the given version as-is rather than incrementing from nothing.
    public async Task UpdateVersion_NoLabel_AppendsTheLabelWithTheGivenVersion()
    {
        var handler = CreateHandler();
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, DockerfileWithoutLabel);
        var definition = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));

        handler.UpdateVersion(definition, IncrementType.Patch, overrideVersion: "1.0.0");

        await Assert.That(definition.Version).IsEqualTo("1.0.0");

        var written = await File.ReadAllTextAsync(dockerfilePath);
        await Assert.That(written).EndsWith($"LABEL {ProjectConstants.DockerImageVersionLabel}=\"1.0.0\"");
        // Everything that was already there is left untouched.
        await Assert.That(written).StartsWith("FROM alpine:3.20");
        await Assert.That(written).Contains("CMD [\"/app/run.sh\"]");

        // And the appended label is what a reload reads back as the version.
        var reloaded = handler.Load(dockerfilePath, written);
        await Assert.That(reloaded.Version).IsEqualTo("1.0.0");
    }

    // A file ending in a newline must keep it, and must not gain a blank line before the label.
    [Test]
    public async Task UpdateVersion_NoLabel_PreservesATrailingNewline()
    {
        var handler = CreateHandler();
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, DockerfileWithoutLabel + "\n");
        var definition = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));

        handler.UpdateVersion(definition, IncrementType.Patch, overrideVersion: "1.0.0");

        var written = await File.ReadAllTextAsync(dockerfilePath);
        await Assert.That(written).EndsWith($"LABEL {ProjectConstants.DockerImageVersionLabel}=\"1.0.0\"\n");
        await Assert.That(written).DoesNotContain("\n\nLABEL");
    }

    // A comment mentioning the version label text must not be treated as the version
    // source — only an actual LABEL instruction line counts.
    [Test]
    public async Task Load_IgnoresCommentMentioningVersionLabel()
    {
        var handler = CreateHandler();
        var definition = handler.Load("Dockerfile", DockerfileWithCommentMentioningLabel);

        await Assert.That(definition.Version).IsEqualTo("1.2.3");
    }

    [Test]
    public async Task UpdateVersion_IgnoresCommentMentioningVersionLabel_OnlyUpdatesRealLabel()
    {
        var handler = CreateHandler();
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, DockerfileWithCommentMentioningLabel);
        var definition = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));

        handler.UpdateVersion(definition, IncrementType.Patch);

        var content = await File.ReadAllTextAsync(dockerfilePath);
        await Assert.That(content).Contains("# see org.opencontainers.image.version=\"0.0.0\" below for the real value");
        await Assert.That(content).Contains("LABEL org.opencontainers.image.version=\"1.2.4\"");
    }

    // Multi-stage Dockerfiles can repeat the version LABEL per stage; every occurrence
    // must stay in sync after an update.
    [Test]
    public async Task UpdateVersion_MultiStageDockerfile_UpdatesEveryVersionLabel()
    {
        var handler = CreateHandler();
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, MultiStageDockerfile);
        var definition = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));

        handler.UpdateVersion(definition, IncrementType.Patch);

        var content = await File.ReadAllTextAsync(dockerfilePath);
        var occurrences = content.Split("org.opencontainers.image.version=\"1.2.4\"").Length - 1;
        await Assert.That(occurrences).IsEqualTo(2);
        await Assert.That(content).DoesNotContain("1.2.3");
    }

    // The version label can be declared on a backslash-continuation line of a multi-line
    // LABEL instruction, not just on the LABEL line itself.
    [Test]
    public async Task Load_FindsVersionLabelOnContinuationLine()
    {
        var handler = CreateHandler();
        var definition = handler.Load("Dockerfile", DockerfileWithLabelContinuation);

        await Assert.That(definition.Version).IsEqualTo("1.2.3");
    }

    [Test]
    public async Task UpdateVersion_LabelContinuationLine_UpdatesInPlace()
    {
        var handler = CreateHandler();
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, DockerfileWithLabelContinuation);
        var definition = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));

        handler.UpdateVersion(definition, IncrementType.Patch);

        var content = await File.ReadAllTextAsync(dockerfilePath);
        await Assert.That(content).Contains("maintainer=\"team-datalake\" \\");
        await Assert.That(content).Contains("org.opencontainers.image.version=\"1.2.4\"");
    }

    // UpdateVersion must only rewrite the version label itself, preserving the file's
    // original CRLF line endings rather than normalizing everything to LF.
    [Test]
    public async Task UpdateVersion_PreservesCrlfLineEndings()
    {
        var handler = CreateHandler();
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        var crlfContent = DockerfileWithQuotedLabel.Replace("\n", "\r\n");
        await File.WriteAllTextAsync(dockerfilePath, crlfContent);
        var definition = handler.Load(dockerfilePath, await File.ReadAllTextAsync(dockerfilePath));

        handler.UpdateVersion(definition, IncrementType.Patch);

        var content = await File.ReadAllTextAsync(dockerfilePath);
        await Assert.That(content).Contains("FROM alpine:3.20\r\n");
        await Assert.That(content).Contains("org.opencontainers.image.version=\"1.2.4\"\r\n");
        await Assert.That(content).Contains("CMD [\"/app/run.sh\"]");
    }
}
