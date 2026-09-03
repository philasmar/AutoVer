# AutoVer

**AutoVer** is a .NET CLI tool that automates versioning, tagging and changelog
generation for a git repository.

```bash
dotnet tool install -g AutoVer
```

Requires the .NET 10 SDK or later. The installed command is `autover`.

## What it does

* Versions one or many projects in a single git repository, with independent or
  shared version numbers
* Versions `.csproj`, `.nuspec` and `Dockerfile` projects - the latter through the
  `org.opencontainers.image.version` label
* Applies Patch, Minor or Major increments, chosen per release or per change
* Commits the version bump and tags the release
* Generates a `CHANGELOG.md` from conventional commit messages or from
  hand-written change files
* Reports the release name and tag for a CI job to create a GitHub or GitLab
  release from
* Lets you configure the shape of the tag and release name a release produces

## Getting started

Describe the projects to version in `.autover/autover.json` at the repository root:

```json
{
  "Projects": [
    { "Name": "MyLibrary", "Path": "src/MyLibrary/MyLibrary.csproj" }
  ],
  "UseCommitsForChangelog": false,
  "ChangeFilesDetermineIncrementType": true
}
```

Record a change as part of the work that made it:

```bash
autover change --project-name MyLibrary --increment-type Minor -m "Add a widget API"
```

That writes a change file under `.autover/changes/`. Commit it alongside your
code. When you're ready to release:

```bash
autover version     # bumps the version, commits, and tags the release
autover changelog   # writes CHANGELOG.md, consumes the change files, commits
```

Then push, tags included:

```bash
git push && git push origin --tags
```

## Commands

### `autover version`

Performs the versioning: bumps each configured project, commits the change and
tags the release.

| Option | Purpose |
|---|---|
| `--project-path <path>` | Repository or project directory to act on. Defaults to the current directory. |
| `--increment-type <type>` | `Major`, `Minor`, `Patch` or `None`. Overrides the configured default. |
| `--use-version <version>` | Release a specific version instead of an increment. |
| `--current` | Print the current version without changing anything. |
| `--skip-version-tag-check` | Don't require each project to already carry a version. |
| `--no-commit` | Bump the version without committing (implies no tag). |
| `--no-tag` | Commit the bump without tagging. |
| `--verbose` | Include full exception detail on failure. |

`--current` prints a bare value for a single project, so it can be captured
directly: `VERSION=$(autover version --current)`.

### `autover changelog`

Generates `CHANGELOG.md` for the current release and commits it.

| Option | Purpose |
|---|---|
| `--project-path <path>` | Repository or project directory to act on. |
| `--output-to-console` | Print the changelog instead of writing the file. |
| `--release-name` | Print the current release's name. |
| `--tag-name` | Print the current release's tag. |
| `--verbose` | Include full exception detail on failure. |

`--release-name`, `--tag-name` and `--output-to-console` print to stdout for
shell capture; warnings and errors go to stderr.

### `autover change`

Creates a change file describing work in progress, to be committed with it and
consumed by the next `autover changelog`.

| Option | Purpose |
|---|---|
| `--project-name <name>` | The configured project the change belongs to. |
| `--increment-type <type>` | `Major`, `Minor`, `Patch` or `None`. |
| `--message, -m <text>` | The changelog message. |
| `--project-path <path>` | Repository or project directory to act on. |

## Configuration

`.autover/autover.json`:

| Setting | Default | Purpose |
|---|---|---|
| `Projects` | `[]` | The projects to version. Omit to auto-discover. |
| `UseCommitsForChangelog` | `true` | Build the changelog from conventional commit messages. Set `false` to use change files. |
| `ChangeFilesDetermineIncrementType` | `false` | Let each change file's increment type decide the bump. |
| `UseSameVersionForAllProjects` | `false` | Move every project to one shared version. |
| `DefaultIncrementType` | `Patch` | The increment used when nothing else specifies one. |
| `ChangelogCategories` | none | Map a commit type (e.g. `feat`) to a changelog heading. |
| `TagFormat` | `release_{date}[_{iteration}]` | The git tag a release is tagged with. |
| `ReleaseNameFormat` | follows `TagFormat` | The human-readable release name. |
| `VersionFromTag` | `false` | Take the version from the repository's release tags instead of from a project file. |
| `InitialVersion` | `0.1.0` | The version to start from when nothing carries one yet - a tag-sourced repository with no release tag, or a project file with no version field. |

Each entry in `Projects` takes:

| Field | Purpose |
|---|---|
| `Name` | The name used by `autover change --project-name` and in the changelog. |
| `Path` | The project file to version. |
| `Paths` | Several project files that share one version, instead of `Path`. |
| `IncrementType` | An increment specific to this project. |
| `PrereleaseLabel` | A prerelease label to apply, e.g. `beta.1`. |

## Tag and release name formats

By default AutoVer tags a release by date - `release_2026-09-02`, or
`release_2026-09-02_2` for a second release the same day - and names it
`Release 2026-09-02`. Both are configurable:

```json
{
  "TagFormat": "v{major}.{minor}.{patch}[-{prerelease}]",
  "ReleaseNameFormat": "Release {major}.{minor}.{patch}[-{prerelease}]"
}
```

A version-based tag is what lets a consumer pin an immutable, meaningful ref - a
shared CI templates repository included at `ref: v1.4.0`, for instance, where a
date tells you when a release happened but not whether adopting it will break you.

`ReleaseNameFormat` is optional: left unset, it follows `TagFormat`'s family, so
setting only `TagFormat` is enough.

Text outside braces is literal. `{{`, `}}`, `[[` and `]]` are escapes for the
corresponding literal character.

| Placeholder | Family | Renders |
|---|---|---|
| `{major}` `{minor}` `{patch}` | version | The project's version components |
| `{prerelease}` | version | The prerelease label (e.g. `beta.1`), empty when unset |
| `{date}` | date | The release date, `yyyy-MM-dd` by default |
| `{year}` `{month}` `{day}` | date | Individual date components |
| `{iteration}` | either | 2, 3, ... for a repeat release of the same version/date; 1 for the first |

A format draws from one family only (see the rules below); `{iteration}` belongs
to neither and can be used with both.

Date placeholders accept a .NET date format: `{date:yyyyMMdd}`, `{year:yy}`,
`{month:MMM}`. Supported specifiers are `yyyy`, `yy`, `MMMM`, `MMM`, `MM`, `M`,
`dd` and `d`, plus the separators `-` `_` `.` `/` and space - a specifier that
renders but couldn't be read back would silently drop tags from release history.

### Optional groups

A `[...]` group renders only when the placeholders inside it have a value, which
is what lets one format cover a release both with and without a prerelease, or
both the first and a repeat release:

| Format | Renders |
|---|---|
| `v{major}.{minor}.{patch}[-{prerelease}]` | `v1.4.0`, or `v1.4.0-beta.1` |
| `release_{date}[_{iteration}]` | `release_2026-09-02`, or `release_2026-09-02_2` |

Only `{prerelease}` and `{iteration}` can be absent, so only those are meaningful
inside a group.

### Rules

A tag isn't only written - it's read back to work out which release was most
recent, and from that the commit range a changelog covers. So a format has to
yield an unambiguous sort order, which these rules enforce at load time rather
than part-way through a release:

* **One family per format.** Use version placeholders or date placeholders, never
  both. The two orderings disagree the moment a release isn't strictly linear (a
  backport, or an explicit `--use-version`), leaving no single correct answer for
  which release came last. A date is also recoverable from the tagged commit
  regardless, so a date inside a version tag adds nothing git doesn't already know.
* **A complete ordering key.** All of `{major}`, `{minor}` and `{patch}`, or a full
  date. A partial key collides on every release that only changes an omitted part.
* **An unambiguous layout.** A format has to read back what it renders, so
  adjacent variable-width placeholders are rejected: `{major}{minor}{patch}`
  renders `1.23.456` as `123456`, which reads back as `1234.5.6`.
* **`TagFormat` and `ReleaseNameFormat` must use the same family**, since a release
  name is rendered from the components of the tag it describes.
* **A version-based format needs one version to represent.** If a repository has
  several projects that can hold different versions, either set
  `UseSameVersionForAllProjects` to `true` or use a date-based format.
* **Repeat releases need somewhere to go.** Releasing the same version twice with
  no `{iteration}` in the format is an error rather than a duplicate tag.
* **`TagFormat` has to produce a name git accepts**: no space, `~`, `^`, `:`,
  `?`, `*`, `[`, `\`, `..` or `@{`, no trailing `.` or `.lock`, no empty path
  segment. Validated against git's own ref grammar when the configuration is
  read, before any project file is written, and again on the finished tag name.
  `ReleaseNameFormat` is free text and unrestricted.

**If any project sets `PrereleaseLabel`, include `{prerelease}` in a
version-based `TagFormat`.** Without it, `1.4.0-beta.1` and `1.4.0` both render
as `v1.4.0`, and releasing the second one is reported as a collision.

**A format with no distinctive literal text matches more than you might expect.**
`{major}.{minor}.{patch}` matches any tag shaped like `1.4.0`, including one
created by hand before AutoVer was adopted, which would then count as release
history. Prefixing the format (`v{major}.{minor}.{patch}`) narrows it.

**The release AutoVer describes is the nearest one reachable from HEAD**, not the
highest-ordering tag - the same notion as `git describe --tags --abbrev=0`. The
two differ after a backport - release `2.0.0`, then cut `1.9.1` from the older
line - where building a changelog for the highest tag would describe the wrong
release and cover the wrong commit range. If no release tag is reachable at all
(an unmerged branch, or a shallow clone), the highest-ordering tag is used.

### Starting from nothing

A project that doesn't carry a version yet is **seeded** rather than rejected.
AutoVer creates the version field - a `<Version>` element in an unconditioned
`PropertyGroup`, a `<version>` in a nuspec's `metadata`, or an
`org.opencontainers.image.version` LABEL appended to a Dockerfile - and writes
`InitialVersion` into it, taken as-is rather than incremented. Every release after
that reads the field back and increments it in the ordinary way, so seeding only
ever happens once.

`InitialVersion` defaults to `0.1.0`. Set it to start somewhere else:

```json
{ "InitialVersion": "1.0.0" }
```

This is decided per project entry: an entry whose files already carry a version
takes it from there, and only one carrying none is seeded.

### Versioning from tags, with no project file

Some repositories have nothing to carry a version. A repository of shared CI
templates, for instance, publishes no package and builds no image - its releases
are consumed by pinned ref. A version written into a file there would be a version
nothing ever reads.

This is the model GitHub Actions uses: `action.yml` has no version field at all,
and an action's version is purely the ref you check out. `VersionFromTag` does the
same, taking the current version from the repository's own release tags:

```json
{
  "Projects": [ { "Name": "ci" } ],
  "VersionFromTag": true,
  "InitialVersion": "1.0.0",
  "TagFormat": "v{major}.{minor}.{patch}",
  "ChangeFilesDetermineIncrementType": true
}
```

Projects are listed by **name only** - the name is what `autover change
--project-name` attaches to and what the changelog labels. There is no `Path`.

A release then reads the current version out of the latest release tag, increments
it, and tags the new one. Because nothing is written, `autover version` produces
**no commit**: the tag lands on `HEAD`, and the release's only content is whatever
`autover changelog` commits afterwards. On the very first release there is no tag
to read, so `InitialVersion` is used exactly as given rather than incremented.

Where several projects are listed, they share the single version the tag carries,
and the largest increment any of them asked for wins - a Major change anywhere
makes the release a Major one.

Four things are checked when the configuration is read:

* `TagFormat` must be version-based. A date-based tag carries no version, so there
  would be nothing to read the current one back from.
* At least one project must be listed, so a change file has something to attach to.
* No project may specify `Path` or `Paths`. One tag carries one version, so a
  file-backed project alongside a tag-sourced one has no single answer.
* `InitialVersion` only applies with `VersionFromTag`, and must be a valid three
  part version.

### Switching the format on an existing repository

The version lives in the project file, not in the tag, so releases carry on
normally - `0.5.0` still becomes `0.5.1`, now tagged `v0.5.1`. Tags written under
the old format simply stop matching: AutoVer ignores what it can't parse, and says
so once on stderr when that leaves no release history at all. Old tags and
releases are left untouched.

Two things to know before switching:

* **Commit the `autover.json` change before releasing.** `autover changelog` loads
  the configuration as of the release tag, so an uncommitted switch leaves it
  reading the old format back and titling the release with a date.
* **A commits-based changelog loses its range boundary on the first release after
  the switch.** With no version-based release before it, the range reaches back to
  the start of the repository. Tagging the previous release's commit with a
  version-based name first (`git tag v0.5.0 release_2026-09-01`) bridges the gap.
  Repositories using change files are unaffected - their changelog comes from the
  change files, not from a commit range.

## CI/CD

AutoVer is designed to run in a CI/CD pipeline. The following GitHub Workflow
versions a project and updates its `CHANGELOG.md`:

```yaml
name: Prepare Release

on:
  workflow_dispatch:

permissions:
  contents: write

jobs:
  version-and-changelog:
    name: Version and Changelog
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - name: Setup .NET 10.0
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - name: Install AutoVer
        run: dotnet tool install -g AutoVer
      - name: Setup Git User
        run: |
          git config --global user.email "github-user@email.com"
          git config --global user.name "GitHub User"
      - name: Increment Version
        run: autover version
      - name: Update Changelog
        run: autover changelog
      - name: Push Changes
        run: git push && git push origin --tags
```

**_Note_**: Replace `github-user@email.com` and `GitHub User` with the identity
the release commits should be attributed to. `fetch-depth: 0` matters - AutoVer
reads tag history to determine the current and previous release.

To create a release from the same pipeline, read the name and tag back:

```yaml
      - name: Create Release
        run: |
          tag=$(autover changelog --tag-name)
          name=$(autover changelog --release-name)
          notes=$(autover changelog --output-to-console)
          gh release create "$tag" --title "$name" --notes "$notes"
```

## Getting Help

Bug reports and feature requests are welcome as
[issues](https://github.com/philasmar/autover/issues) on this repository.

This project takes inspiration from the following projects:
 * [Versionize](https://github.com/versionize/versionize)
 * [AWS .NET deployment tool](https://github.com/aws/aws-dotnet-deploy)
