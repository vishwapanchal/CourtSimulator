---
name: verify-core
description: Build, style-check, test and coverage-gate the CourtSim.Core assembly outside Unity. Use after changing anything under Assets/Scripts/Core or Assets/Tests/EditMode, before committing, or when asked to "run the tests" in this project.
---

# Verify CourtSim.Core

CourtSim's engine-independent logic (`Assets/Scripts/Core`) and its NUnit suite
(`Assets/Tests/EditMode`) compile under plain .NET, so they can be checked without a Unity editor.

## Run

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-core.ps1
# or, with a specific SDK:
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-core.ps1 -Dotnet <path-to-dotnet.exe>
```

The script runs, in order: `dotnet restore`, `dotnet format --verify-no-changes` (rules from
`.editorconfig`), `dotnet build` with warnings as errors against .NET Standard 2.1 (Unity 2022.3's API
surface), `dotnet test` with coverlet, and a **line-coverage gate of 80%** on `CourtSim.Core`.

If `dotnet` is not on PATH, look for a portable SDK (`dotnet-install.ps1 -Channel 8.0 -InstallDir <dir>`)
and pass it with `-Dotnet`. Ask the user before installing anything.

## Interpreting failures

- **Style check fails:** run `dotnet format tools/CoreVerification/Tests/CourtSim.Core.Tests.csproj`
  to fix, then review the diff.
- **CS1591/CS1573:** suppressed by design. Any *other* warning is a real error.
- **API missing in netstandard2.1:** the code used an API Unity 2022.3 lacks. Find an alternative;
  don't raise the target framework.
- **NUnit API errors inside Unity but not here:** Unity bundles NUnit 3.5. Avoid `Assert.Multiple`,
  `Has.Some.Contains(string)` and other post-3.5 constraint syntax.

## Unity-side code

This skill does not compile Runtime/Editor/PlayMode code. Use the `unity-batch` skill for that.
