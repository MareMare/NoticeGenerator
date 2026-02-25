# NoticeGenerator

A command-line tool that generates NOTICE.md files containing license information for NuGet package dependencies in .NET projects.

## Overview

NoticeGenerator automatically scans your .NET project or solution, retrieves license information for all NuGet package dependencies, and generates a comprehensive NOTICE.md file. This is particularly useful for compliance with open-source licenses that require attribution.

## Features

- 🔍 **Automatic Package Discovery**: Uses `dotnet list package` to find all NuGet dependencies
- 📄 **License Text Retrieval**: Fetches full license texts from NuGet packages and external sources
- ⚡ **Parallel Processing**: Concurrent NuGet API requests for faster execution
- 🎯 **Flexible Scope**: Support for both top-level and transitive dependencies
- 📊 **Progress Tracking**: Real-time progress display with detailed status information
- 🎨 **Rich Console Output**: Beautiful console interface powered by Spectre.Console
- 🔧 **Configurable Output**: Customizable output file path and formatting options

## Installation

### Prerequisites

- .NET 10.0 or later

### Build from Source

```bash
git clone https://github.com/MareMare/NoticeGenerator.git
cd NoticeGenerator
dotnet build -c Release
```

## Usage

### Basic Usage

Generate a NOTICE.md file for the current directory:

```bash
dotnet run --project src/NoticeGenerator
```

### Command Line Options

```bash
notice-generator [OPTIONS]
```

#### Options

| Option | Description | Default |
|--------|-------------|---------|
| `-p, --project <PATH>` | Path to the project or solution to analyze | `.` (current directory) |
| `-s, --scope <SCOPE>` | Package scope: `all` (includes transitive) or `top` (top-level only) | `all` |
| `--no-version` | Omit version from package identifiers (useful for grouping) | `false` |
| `-o, --output <FILE>` | Output file path for the generated NOTICE.md | `NOTICE.md` |
| `--concurrency <N>` | Number of concurrent NuGet API requests (1-16) | `4` |

### Examples

#### Analyze a specific project directory
```bash
dotnet run --project src/NoticeGenerator -- --project ./src
```

#### Generate NOTICE for top-level packages only
```bash
dotnet run --project src/NoticeGenerator -- --project ./src --scope top
```

#### Omit version information from package names
```bash
dotnet run --project src/NoticeGenerator -- --project ./src --no-version
```

#### Custom output file and scope
```bash
dotnet run --project src/NoticeGenerator -- --project ./src --scope top --output THIRD_PARTY_NOTICES.md
```

#### High concurrency for faster processing
```bash
dotnet run --project src/NoticeGenerator -- --project ./src --concurrency 8
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
   - External URLs specified in package metadata
   - SPDX standard license texts for common licenses
4. **Report Generation**: Compiles all information into a structured NOTICE.md file

## Dependencies

- **Microsoft.Extensions.Http**: HTTP client factory and extensions
- **NuGet.Protocol**: NuGet API client library
- **Spectre.Console.Cli**: Rich console application framework

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Support

If you encounter any issues or have questions, please [open an issue](https://github.com/MareMare/NoticeGenerator/issues) on GitHub.