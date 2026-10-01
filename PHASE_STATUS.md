# Phase status

## Phase 0 — implemented, awaiting game check

Scope: solution and three projects, existing MIT license, configurable KSP references, C# 9 target frameworks, and two-assembly staging. Dependencies: installed KSP 1, .NET SDK, .NET 8 runtime, .NET Framework 4.8 reference assemblies, and pinned NuGet packages. Interfaces for later phases: `Lodeworks.Sim` is game independent; `Lodeworks` is the KSP adapter assembly. The compile probe is deliberately not gameplay logic.

Verified on 2026-10-01 with `KSP_ROOT` set to the installed Steam KSP directory:

```powershell
dotnet build .\Lodeworks.sln -c Release
dotnet test .\Lodeworks.sln -c Release --no-build
```

Result: build succeeded with zero warnings/errors; 1 boundary test passed. `artifacts\GameData\Lodeworks\Plugins` contained exactly `Lodeworks.dll` and `Lodeworks.Sim.dll`. It contained no KSP, Unity, or test DLLs.

Remaining manual check: load the staged assemblies in KSP and inspect the game log for assembly load errors. No gameplay behavior or Phase 1 API feasibility check is claimed yet. Phase 1 should begin by compiling small probes against this installation and recording actual signatures and in-game observations before building features.

