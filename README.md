# NoticeGenerator

A command-line tool that generates NOTICE.md files containing license information for NuGet package dependencies in .NET projects.

## Overview

NoticeGenerator automatically scans your .NET project or solution, retrieves license information for all NuGet package dependencies, and generates a comprehensive NOTICE.md file. This is particularly useful for compliance with open-source licenses that require attribution.

## Features

- 🔍 **Automatic Package Discovery**: Uses `dotnet list package` to find all NuGet dependencies
- 📄 **License Text Retrieval**: Fetches full license texts from NuGet packages, GitHub repositories, and external sources
- ⚡ **Parallel Processing**: Concurrent NuGet API requests for faster execution
- 🎯 **Flexible Scope**: Support for both top-level and transitive dependencies
- 📊 **Progress Tracking**: Real-time progress display with detailed status information
- 🎨 **Rich Console Output**: Beautiful console interface powered by Spectre.Console
- 🔧 **Configurable Output**: Customizable output file path and formatting options

## Installation

### Prerequisites

- .NET 10.0 or later

### Install as .NET Tool

#### From GitHub Packages

```bash
# Add GitHub Packages source (one-time setup)
dotnet nuget add source "https://nuget.pkg.github.com/MareMare/index.json" \
  --name "MareMare GitHub Packages" \
  --username "token" \
  --password "YOUR_GITHUB_TOKEN" \
  --store-password-in-clear-text

# Install the tool
dotnet tool install MareMare.NoticeGenerator --local --prerelease
```

#### Using PowerShell Script (Recommended for Development)

The `sandbox/run-tools.ps1` script provides an automated way to install and run the tool:

```powershell
# Navigate to the sandbox directory
cd sandbox

# Run with default settings
.\run-tools.ps1

# Run with custom arguments
.\run-tools.ps1 -- --project ../src --scope top --output THIRD_PARTY_NOTICES.md
```

### Build from Source

```bash
git clone https://github.com/MareMare/NoticeGenerator.git
cd NoticeGenerator
dotnet build -c Release
```

### Publishing Standalone Executable

To create a self-contained, single-file executable for Windows x64:

```bash
dotnet publish .\src\NoticeGenerator -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:PublishReadyToRun=false -o artifacts
```

This command will:
- Build the project in Release configuration
- Target Windows x64 runtime (`win-x64`)
- Create a single executable file (`PublishSingleFile=true`)
- Include all dependencies (`SelfContained=true`)
- Output the executable to the `artifacts` directory

The resulting executable can be distributed and run on Windows x64 systems without requiring .NET to be installed.

## Usage

### Basic Usage

#### Using the standalone executable

```powershell
.\NoticeGenerator.exe --help
```

#### Using as Installed .NET Tool

```bash
dotnet tool run NoticeGenerator -- --help
```

#### Using from Source

```bash
dotnet run --project src/NoticeGenerator -- --help
```

#### Using PowerShell Script

```powershell
cd sandbox
.\run-tools.ps1 -- --help
```

### Command Line Options

```powershell
.\NoticeGenerator.exe [OPTIONS]
```

#### Options

| Option | Description | Default |
|--------|-------------|---------|
| `-p, --project <PATH>` | Path to the project or solution to analyze | `.` (current directory) |
| `-s, --scope <SCOPE>` | Package scope: `all` (includes transitive) or `top` (top-level only) | `all` |
| `--no-version` | Removes version information from the generated NOTICE.md | `false` |
| `-o, --output <FILE>` | Output file path for the generated NOTICE.md | `NOTICE.md` |
| `--concurrency <N>` | Number of concurrent NuGet API requests (1-16) | `4` |

### Examples

#### Using the standalone executable

```powershell
# Analyze a specific project directory
.\NoticeGenerator.exe --project ./src

# Generate NOTICE for top-level packages only
.\NoticeGenerator.exe --project ./src --scope top

# Omit version information from package names
.\NoticeGenerator.exe --project ./src --no-version

# Custom output file and scope
.\NoticeGenerator.exe --project ./src --scope top --output THIRD_PARTY_NOTICES.md

# High concurrency for faster processing
.\NoticeGenerator.exe --project ./src --concurrency 8
```

#### Using PowerShell Script

```powershell
# Basic usage
.\run-tools.ps1

# With custom arguments
.\run-tools.ps1 -- --project ../src --scope top --output THIRD_PARTY_NOTICES.md
```

#### Using from Source

```bash
# Analyze a specific project directory
dotnet run --project src/NoticeGenerator -- --project ./src

# Generate NOTICE for top-level packages only
dotnet run --project src/NoticeGenerator -- --project ./src --scope top

# Custom output file and scope
dotnet run --project src/NoticeGenerator -- --project ./src --scope top --output THIRD_PARTY_NOTICES.md
```

## Output Format

The generated NOTICE.md file includes:

- **Package Information**: Name, version, authors, and description
- **License Details**: License expression, copyright information
- **Full License Text**: Complete license text when available
- **Source Links**: Links to package pages, project repositories, and SPDX license pages
- **Error Reporting**: Clear indication of packages where license information couldn't be retrieved

## How It Works

1. **Package Discovery**: Executes `dotnet list package` to enumerate all NuGet dependencies
2. **Metadata Retrieval**: Queries the NuGet API in parallel to fetch package metadata
3. **License Text Extraction**: Downloads license files from:
   - Package contents (.nupkg files)
   - GitHub repository LICENSE files (provides actual copyright notices)
   - SPDX standard license texts for common licenses
   - External URLs specified in package metadata
4. **Report Generation**: Compiles all information into a structured NOTICE.md file

## Dependencies

- **Microsoft.Extensions.Http**: HTTP client factory and extensions
- **NuGet.Protocol**: NuGet API client library
- **Spectre.Console.Cli**: Rich console application framework

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
