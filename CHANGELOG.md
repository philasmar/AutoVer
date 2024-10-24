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
