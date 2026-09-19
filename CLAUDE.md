# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is the **Windows Community Toolkit Labs** repository - a prototyping space for experimental components that may eventually graduate to the main [Windows Community Toolkit](https://github.com/CommunityToolkit/Windows). Components here target WinUI 2 (UWP), WinUI 3 (Windows App SDK), and Uno Platform.

The `tooling` directory is a git submodule from [CommunityToolkit/Tooling-Windows-Submodule](https://github.com/CommunityToolkit/Tooling-Windows-Submodule).

## Build Requirements

- Visual Studio 2022 (UWP & Desktop Workloads for .NET)
- .NET 9 SDK
- Windows 10 SDK, version 2004 (10.0.19041.0)
- Windows 10 21H1 (Build 19043) or greater
- Long paths must be enabled (`git config --system core.longpaths true` + registry key `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled = 1`)

## Common Commands

### Initial Setup
```bash
# Clone with submodules
git clone --recurse-submodules https://github.com/CommunityToolkit/Labs-Windows.git

# Restore dotnet tools (includes slngen)
dotnet tool restore
```

### Working on a Single Component
```bash
# Navigate to component and generate its solution
cd components/ComponentName
OpenSolution.bat
```

This runs `GenerateSingleSampleHeads.ps1` which creates platform heads (UWP, WinAppSDK, WASM) and opens the solution.

### Building All Components
```bash
# From repo root - generates CommunityToolkit.AllComponents.sln
GenerateAllSolution.bat

# Or with specific targets
powershell .\tooling\GenerateAllSolution.ps1 -MultiTargets wasdk
powershell .\tooling\GenerateAllSolution.ps1 -MultiTargets uwp,wasm,wasdk
```

### Multi-Target Configuration
```bash
# Enable additional platforms (ios, android, macos, etc.)
powershell .\tooling\MultiTarget\UseTargetFrameworks.ps1 -targets all

# Switch between WinUI 2 (Uno+UWP) and WinUI 3 (Uno+WinAppSDK)
powershell .\tooling\MultiTarget\UseUnoWinUI.ps1 -targets 3
```

### Running Tests
Tests run via VSTest against UWP or WinAppSDK heads:
```bash
vstest.console.exe ./tooling/**/CommunityToolkit.Tests.wasdk.build.appxrecipe /Framework:FrameworkUap10
```

### Creating a New Component
```bash
dotnet new --install .\tooling\ProjectTemplate\
cd components
dotnet new ctk-component -n MyComponentName
```

### XAML Style Check
```bash
powershell -version 5.1 -command "./ApplyXamlStyling.ps1 -Passive"
```

## Architecture

### Component Structure
Each component in `components/` follows this structure:
```
ComponentName/
├── src/                    # Main component source (CommunityToolkit.WinUI.Controls.ComponentName.csproj)
├── samples/                # Sample pages and documentation (*.md files become sample app pages)
├── tests/                  # Shared test project (.shproj)
├── heads/                  # Generated platform heads (created by OpenSolution.bat)
├── MultiTarget.props       # Optional: restricts which platforms this component supports
└── OpenSolution.bat        # Generates heads and opens solution
```

### Multi-Target System
The `<MultiTarget>` MSBuild property controls which platforms a component builds for. Valid values: `uwp`, `wasdk`, `wasm`, `wpf`, `linuxgtk`, `macos`, `ios`, `android`, `netstandard`.

Each component can define its supported targets in `src/MultiTarget.props`:
```xml
<Project>
  <PropertyGroup>
    <MultiTarget>uwp;wasdk;wasm</MultiTarget>
  </PropertyGroup>
</Project>
```

### Tooling Components
- **CommunityToolkit.Tooling.SampleGen**: Source generator for sample page metadata
- **CommunityToolkit.Tooling.TestGen**: Source generator for test infrastructure
- **CommunityToolkit.Tooling.XamlNamedPropertyRelay**: Helper for XAML named property binding

### Platform Heads
Platform-specific project heads live in `tooling/ProjectHeads/`. The solution generator (slngen) creates platform-specific builds:
- `*.Uwp.csproj` - UWP/WinUI 2
- `*.Wasdk.csproj` - Windows App SDK/WinUI 3
- `*.Wasm.csproj` - WebAssembly via Uno

### Compilation Symbols
Key conditional compilation symbols used in cross-platform code:
- `WINAPPSDK` - Windows App SDK / WinUI 3 builds
- `__UNO__` - Uno Platform specific code paths
- `__WASM__` - WebAssembly-specific optimizations (AOT-friendly paths)

### WinUI 2 vs WinUI 3 Namespaces
- System controls (TextBlock, etc.) auto-resolve to correct namespace
- WinUI library controls (NavigationView, ItemsRepeater) require `MUXC.` prefix in C# code
- Use `WINAPPSDK` compilation symbol to detect WinUI 3 builds

### Testing
- Uses MSTest framework with Community Toolkit's `VisualUITestBase`
- Tests are in shared projects (`.shproj`) with platform-specific heads
- Only UWP and WinAppSDK platforms run tests in CI
- Test pattern example:
```csharp
[TestClass]
public partial class ExampleTestClass : VisualUITestBase
{
    [TestMethod]
    public async Task MyTest() { }
}
```

## Key Files
- `Directory.Build.props` - Global MSBuild properties, version info
- `tooling/ToolkitComponent.SourceProject.props` - Imported by all component source projects
- `tooling/MultiTarget/Library.props` - Multi-target framework configuration

## Package Publishing
Packages are automatically versioned by date (YYMMDD format) and published to the CommunityToolkit-Labs Azure DevOps feed on main branch commits.
