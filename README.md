# AutoVer

**Notice:** _This project is currently in development and is not ready for use beyond experimentation._

**AutoVer** is a .NET CLI tool that is used for automating versioning of .NET projects. 

The tool is meant to support the following capabilities:
* Auto version multiple project with different version numbers in a single git repository
* Specify Patch, Minor or Major version types
* Create git tags and GitHub releases
* Generate changelogs based on git commit messages as well as hand-written ones.

If you have any feature requests, feel free to submit then to this repo for me to take into consideration as I build this tool out.

This project takes inspiration from the following projects:
 * [Versionize](https://github.com/versionize/versionize)
 * [AWS .NET deployment tool](https://github.com/aws/aws-dotnet-deploy)