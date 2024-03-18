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
