# AutoVer

**Notice:** _This project is currently in development and is not ready for use beyond experimentation._

**AutoVer** is a .NET CLI tool that is used for automating versioning of .NET projects. 

The tool is meant to support the following capabilities:
* Auto version multiple project with different version numbers in a single git repository
* Specify Patch, Minor or Major version types
* Create git tags and GitHub releases
* Generate changelogs based on git commit messages as well as hand-written ones.

## CI/CD

**AutoVer** could be used as part of your CI/CD workflow. The following is a sample GitHub Workflow to version a .NET project as well as create the CHANGELOG.md file.

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
      - name: Setup .NET 8.0
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Install AutoVer
        run: dotnet tool install -g AutoVer
      - name: Setup Git User
        run: |
          git config --global user.email "github-user@email.com"
          git config --global user.name "GitHub User"
      - name: Increment Version
        run: autover version
      - name: Update Chagelog
        run: autover changelog
      - name: Push Changes
        run: git push && git push origin --tags
```

**_Note_**: Make sure to replace `github-user@email.com` and `GitHub User`.

## Getting Help

If you have any feature requests, feel free to submit then to this repo for me to take into consideration as I build this tool out.

This project takes inspiration from the following projects:
 * [Versionize](https://github.com/versionize/versionize)
 * [AWS .NET deployment tool](https://github.com/aws/aws-dotnet-deploy)