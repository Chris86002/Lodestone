# Lodestone

Lodeworks is an original logistics and industry mod for Kerbal Space Program 1. The repository contains the Phase 0 build scaffold and Phase 1 API feasibility probes. A stock part-lock route has been identified, but Phase 1 still awaits in-game checks; there are no playable colony systems yet.

## Local build

Install a .NET SDK that supports `net8.0` (the Phase 0 build was verified with SDK 10.0.401), the .NET 8 runtime, and the .NET Framework 4.8 Developer Pack. Point `KSP_ROOT` at your own KSP 1 installation. Its `KSP_x64_Data\Managed` folder must contain `Assembly-CSharp.dll` and UnityEngine DLLs.

```powershell
$env:KSP_ROOT = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program'
dotnet build .\Lodeworks.sln -c Release
dotnet test .\Lodeworks.sln -c Release --no-build
```

Change the path for another PC. [KSPBuildTools](https://kspbuildtools.readthedocs.io/en/1.0.0/msbuild/ksp-install.html) also supports a local `.csproj.user` setting. Do not commit that file or game DLLs.

The staged mod files are in `artifacts\GameData\Lodeworks\Plugins`: `Lodeworks.dll` and `Lodeworks.Sim.dll`. `artifacts` is build output and is ignored by Git. No KSP or Unity DLL is distributed. The compiled Phase 1 API probes are not wired to player commands and do not yet add playable features.

See `PHASE_STATUS.md` for the checks completed and the remaining in-game validation.
