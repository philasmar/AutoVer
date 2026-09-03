## Release 2026-09-03

### AutoVer (1.3.0)
* Add VersionFromTag, which takes the version from the repository's release tags instead of a project file, for a repository that has no artifact to carry one. Projects are listed by name only, no file is written when a release is cut, and the largest increment any of them asks for wins
* Seed a project that carries no version yet from the new InitialVersion setting instead of failing. The version field is created - a Version element for a project file, a version element in a nuspec's metadata, or a LABEL appended to a Dockerfile - and takes InitialVersion as-is, so releases after that increment normally
* Add InitialVersion, the version to start from when nothing carries one yet, defaulting to 0.1.0
* --skip-version-tag-check is no longer needed, since a project without a version is seeded rather than rejected. It is still accepted, and ignored

## Release 2026-09-02

### AutoVer (1.2.0)
* Add configurable TagFormat and ReleaseNameFormat, so a release can be tagged by version (e.g. v1.4.0) instead of only by date. Formats are built from placeholders with optional groups, and are validated for invertibility and against git's ref grammar when the configuration is read
* Order prerelease versions by SemVer 2.0.0 precedence, so beta.2 sorts below beta.10 and 1.4.0 sorts above 1.4.0-beta.1
* Keep a hyphenated prerelease label such as 1.0.0-alpha-1 intact when parsing a version, instead of dropping it
* Identify the current release as the nearest release tag reachable from HEAD, so a backport's changelog no longer describes the highest version instead of the release just cut
* Fix the release name falling back to a tag-shaped string on the first release of a new date

## Release 2026-08-24

### AutoVer (1.1.0)
* Add a --current flag to 'version' to print each project's current version without incrementing, committing, or tagging anything

## Release 2026-08-20

### AutoVer (1.0.0)
* BREAKING: Upgrading to this version requires the .NET 10 SDK/runtime (previously .NET 8)
* BREAKING: 'version', 'change', and 'changelog' now fail with an error if --increment-type is invalid (values are matched case-insensitively against None/Patch/Minor/Major), instead of silently defaulting to Patch
* Add native support for versioning Dockerfiles (via a 'LABEL org.opencontainers.image.version' tag), alongside existing .csproj/.nuspec support
* Dockerfile versioning supports multi-stage builds and multi-line LABEL declarations (keeping every version LABEL in sync), and preserves the file's original line endings
* Add a --verbose flag to 'version', 'change', and 'changelog' to show full error details, including the underlying exception, when something goes wrong
* Fix a crash when --project-path is a relative path (e.g. '.' or a subdirectory) instead of an absolute one, and fix --project-path failing when it points at a subdirectory that has no project files of its own but the repo already has an autover.json
* Fix a false 'not a valid git repository' error when an existing autover.json has no (or an empty) Projects list
* Fix 'changelog' crashing when autover.json (or a change file) was added to the repo after the last release tag, and fix it silently ignoring the current autover.json settings (e.g. UseCommitsForChangelog) in that same situation

## Release 2026-05-21

### AutoVer (0.0.26)
* Detect linked git worktrees so change files are written to the correct repository root.

## Release 2025-04-01

### AutoVer (0.0.25)
* Fixed an issue when --use-version has the same version as the csproj and no version update is made. Previously, this would error out with a non-zero exit code.

## Release 2024-10-24

### AutoVer (0.0.24)
* Add support for project containers, which allow multiple projects to be versioned as one
* Add a unit test project that tests a combination of project configurations

## Release 2024-10-22

### AutoVer (0.0.23)
* Add support for nuspec files

## Release 2024-10-04

### AutoVer (0.0.22)
* Using the new setting UseSameVersionForAllProjects, users will now be able to force all the configured projects to use the same version. This version will be determined by the project with the currently highest version.

## Release 2024-04-13

### AutoVer (0.0.21)
* Fix paths issue on Windows due to invalid Path separator

## Release 2024-03-26

### AutoVer (0.0.20)
* Add ability to override the version of all projects

## Release 2024-03-23

### AutoVer (0.0.19)
* Add caching to commonly called methods to improve speed and efficiency
* Change the version tag prefix from 'version' to 'release'

## Release 2024-03-21

### AutoVer (0.0.18)
* Fix issue causing changelog creation to fail when changes folder doesn't exist

## Release 2024-03-21

### AutoVer (0.0.17)
* Fixed an issue while parsing project name

## Release 2024-03-21

### AutoVer (0.0.16)
* Change files can now specify the increment type

## Release 2024-03-20

### AutoVer (0.0.15)
* Add 'DefaultIncrementType' to AutoVer config
* Add a 'None' IncrementType which does not perform any increment operation
* Add icon to the AutoVer NuGet package

## Release 2024-03-20

### AutoVer (0.0.14)
* Change command now supports repositories with multiple projects

## Release 2024-03-18

### AutoVer (0.0.13)
* ChangeFileHandler now uses last git tag instead of HEAD

## Release 2024-03-18

### AutoVer (0.0.12)
* Fix issue causing change files to not be deleted and staged after running 'autover changelog'
* Fix issue causing 'autover changlog --release-name' to fail

## Release 2024-03-18

### AutoVer (0.0.11)
* Add change files to control generated changelog file
* Changelog command now uses the contents of last git tag instead of HEAD

## Release 2024-03-15

### AutoVer (0.0.10)
* Changelog command now uses the contents of last git tag instead of HEAD

## Release 2024-03-15

### AutoVer (0.0.9)
* Project version is now displayed in the generated changelog

## Release 2024-03-02

### Chores
* fix release workflow
* add support for GitHub release in release process
* remove CLI intro header if arguments are passed
### Features
* add option to output git tag name
* add option to output release name
* add ability to skip git commit and tag after versioning

## Release 2024-01-26

### Bug Fixes
* pull latest ref in release action
