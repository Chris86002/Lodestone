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

## Phase 1 — API route identified, awaiting game check

Scope: API feasibility for contracts, part rewards, funds, stock fuel bridge, depot identity/orbit, and mass modifier. Dependency: Phase 0. Normative sections: master document 4.3, 9.5-9.6, 11.1, 11.3, 15, 18, and 19.2. Interfaces supplied for later phases: pure per-save blueprint decision, partial fuel accounting, physical mass sum, and compiled KSP API probes. No Phase 2 inventory, shipment, campaign, or deployed depot behavior was implemented.

Automated evidence on 2026-10-01 with `KSP_ROOT` set to `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program`:

```powershell
dotnet build .\Lodeworks.sln -c Release
dotnet test .\Lodeworks.sln -c Release --no-build
```

Build: succeeded, zero warnings/errors. Tests: 5 passed, 0 failed (Phase 0 boundary plus Phase 1 per-save gate, partial/reverse/zero fuel transfer, invalid result, and mass fixtures). Staging contains `Lodeworks.dll` and `Lodeworks.Sim.dll` only. Compile success proves the signatures listed in DECISIONS.md exist in this KSP installation; pure tests do not prove gameplay behavior.

The initial custom-preflight blocker is superseded by a stock experimental-part route found in the installed assembly. `Phase1BlueprintAdapter` compiles against public R&D APIs and uses an absent tech node plus per-save experimental grants. Both stock launch paths include `ExperimentalPartsAvailable`, whose failed test has no proceed option. `tools/Verify-Phase1BlueprintApi.ps1` checks these signatures and control-flow references. This is an API feasibility finding, not a claim that locked parts have been tested in KSP. The adapter still needs scenario reward records and runtime integration, and the Phase 1 acceptance gate has not passed.

Follow-up automated checks on 2026-10-01: `tools/Verify-Phase1BlueprintApi.ps1 -KspRoot $env:KSP_ROOT` passed 11 public-API/control-flow assertions against the installed assembly; `dotnet build .\Lodeworks.sln -c Release` passed with zero warnings/errors; `dotnet test .\Lodeworks.sln -c Release --no-build` passed all 5 tests; staging still contains exactly the two Lodeworks DLLs. `KSP_ROOT` was set locally to the installed Steam KSP path for these commands.

Remaining in-game checklist (none claimed complete):

- Load staged DLLs in clean KSP 1.12.5 and inspect `KSP.log` for assembly/type errors.
- Confirm real contract offer/accept/save/reload/reward ordering and career/science/sandbox behavior.
- Prove part lock, R&D purchase prevention, imported craft launch rejection, reward unlock, editor refresh, save switching, sandbox availability, and pre-existing craft loading.
- Measure `RequestResource` positive/negative sign and partial two-component transfer on a physically docked vessel; verify chosen tank flow, stock/ledger quantities, and connected-vessel mass before/after both directions.
- Register a core on a real vessel; observe dock/undock identity remap, orbit change, clearance, and packed/unloaded `IPartMassModifier` updates without duplicate or missing mass.
- Smoke-test KSP 1.12.3 and 1.12.4 if those installations become available.

Do not advance to Phase 2 until the lock/launch behavior and other Phase 1 game evidence are recorded. The current adapter is intentionally not wired into player controls.

Follow-up on 2026-10-03: corrected the pure blueprint decision so sandbox bypasses both campaign rewards and career science prerequisites, as required by section 11.3. `dotnet test .\Lodeworks.sln -c Release` passed all 5 tests against the local KSP 1.12.5 build dependencies. This fixes a sandbox rule error but does not satisfy the Phase 1 acceptance gate: the adapter still has no reward-part configurations or per-save scenario caller, and none of the in-game checklist above has been observed.

Phase 1 diagnostic harness on 2026-10-03: added an opt-in test assembly, a stock-model probe PART, and a scenario-owned reward bit with grant/revoke controls. The harness calls the actual `Phase1BlueprintAdapter`; it is staged under `artifacts\Phase1Harness\GameData` and does not change the normal two-DLL package. `tools\Phase1-Lock-Harness-Checklist.md` gives repeatable sandbox, career, imported-craft, reload, and cross-save observations. Its build succeeded with zero warnings/errors against installed KSP 1.12.5. The assembly verifier passed 11 checks and the regression suite passed 5 tests. **No interactive KSP harness run is claimed.** The part-lock acceptance gate and other Phase 1 runtime checks remain open until the recorded observations pass.

