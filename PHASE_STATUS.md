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

## Phase 1 — blocked at the stock part-lock/launch gate

Scope: API feasibility for contracts, part rewards, funds, stock fuel bridge, depot identity/orbit, and mass modifier. Dependency: Phase 0. Normative sections: master document 4.3, 9.5-9.6, 11.1, 11.3, 15, 18, and 19.2. Interfaces supplied for later phases: pure per-save blueprint decision, partial fuel accounting, physical mass sum, and compiled KSP API probes. No Phase 2 inventory, shipment, campaign, or deployed depot behavior was implemented.

Automated evidence on 2026-10-01 with `KSP_ROOT` set to `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program`:

```powershell
dotnet build .\Lodeworks.sln -c Release
dotnet test .\Lodeworks.sln -c Release --no-build
```

Build: succeeded, zero warnings/errors. Tests: 5 passed, 0 failed (Phase 0 boundary plus Phase 1 per-save gate, partial/reverse/zero fuel transfer, invalid result, and mass fixtures). Staging contains `Lodeworks.dll` and `Lodeworks.Sim.dll` only. Compile success proves the signatures listed in DECISIONS.md exist in this KSP installation; pure tests do not prove gameplay behavior.

**Blocker:** Public metadata exposes editor part filters and stock purchase state, but no confirmed public interception that makes an unrewarded Lodeworks part unpurchasable in R&D and prevents an imported craft containing it from launching. The stock editor's `GetStockPreFlightCheck` is private; `PreFlightCheck.AddTest` alone does not attach a mandatory test to the stock launch path. The exact prerequisite is a demonstrated public-API enforcement route satisfying section 11.3, or an explicit design amendment. No Harmony workaround or cosmetic badge was substituted.

Remaining in-game checklist (none claimed complete):

- Load staged DLLs in clean KSP 1.12.5 and inspect `KSP.log` for assembly/type errors.
- Confirm real contract offer/accept/save/reload/reward ordering and career/science/sandbox behavior.
- Prove part lock, R&D purchase prevention, imported craft launch rejection, reward unlock, editor refresh, save switching, sandbox availability, and pre-existing craft loading.
- Measure `RequestResource` positive/negative sign and partial two-component transfer on a physically docked vessel; verify chosen tank flow, stock/ledger quantities, and connected-vessel mass before/after both directions.
- Register a core on a real vessel; observe dock/undock identity remap, orbit change, clearance, and packed/unloaded `IPartMassModifier` updates without duplicate or missing mass.
- Smoke-test KSP 1.12.3 and 1.12.4 if those installations become available.

Do not advance to Phase 2 until the blocking essential lock/launch behavior is resolved and the Phase 1 game evidence is recorded. The current probe is intentionally not wired into player controls.

