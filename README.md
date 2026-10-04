# Lodestone

Lodeworks is an original logistics and industry mod for Kerbal Space Program 1. Merged work includes the Phase 0 scaffold, Phase 1 API feasibility accepted on recorded clean KSP 1.12.5 evidence, and Phase 2/2A pure inventory, transactions and reservations. There are no playable colony systems yet. Phase 3 PR #9 remains unmerged as checked on October 4, 2026.

[`AI Master command prompt`](<AI Master command prompt>) is the sole design authority, revision 5.1. **Read the mandatory reconciliation gate and next-session instruction in `PHASE_STATUS.md` before any new phase.** Completed-phase acceptance does not automatically prove conformance to amended rules. Pending Phase 3 reconciliation must be accepted before Phase 3A/4; do not import its open implementation as cleanup.

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

## Opt-in Phase 1 lock harness

To test the blueprint lock in KSP, build `Lodeworks.Phase1Harness\Lodeworks.Phase1Harness.csproj` separately. Its diagnostic package is staged under `artifacts\Phase1Harness\GameData`, away from the normal two-DLL output. It includes a test-only probe part and a per-save grant/revoke panel. Follow `tools\Phase1-Lock-Harness-Checklist.md` for installation, expected observations, and an evidence record. The harness does not award campaign rewards and is not part of the normal mod package.

The simulation project builds `netstandard2.0` for game-independent consumers and `net48` for KSP. The KSP package stages the `net48` assembly because KSP 1.12.5's game process does not resolve `netstandard.dll`. After building the normal and diagnostic packages, run `tools\Verify-Phase1RuntimePackage.ps1 -KspRoot $env:KSP_ROOT` to check their contents and runtime references.
