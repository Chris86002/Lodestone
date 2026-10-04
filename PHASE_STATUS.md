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

Phase 1 diagnostic harness on 2026-10-03: added an opt-in test assembly, a stock-model probe PART, and a scenario-owned reward bit with grant/revoke controls. The harness calls the actual `Phase1BlueprintAdapter`; it is staged under `artifacts\Phase1Harness\GameData` and does not change the normal two-DLL package. `tools\Phase1-Lock-Harness-Checklist.md` gives repeatable sandbox, career, imported-craft, reload, and cross-save observations. Its initial build succeeded with zero warnings/errors against installed KSP 1.12.5. The assembly verifier passed 11 checks and the regression suite passed 5 tests. Interactive results and remaining limits are recorded below.

First interactive report on 2026-10-03: the probe appeared in sandbox VAB and launched, but the panel stayed `waiting / failed`. `KSP.log` identified the cause: KSP_x64 could not load `Lodeworks.Sim` due to a missing `netstandard` 2.0 assembly. The sandbox R&D singleton was also unavailable. The harness and build were corrected: `Lodeworks.Sim` now targets `net48` as well as `netstandard2.0`, KSP packages the `net48` build, and the harness treats sandbox as stock availability without R&D. Rebuild: 0 warnings/errors; simulation tests: 5 passed; `tools\Verify-Phase1RuntimePackage.ps1` passed package contents and reference checks. The earlier sandbox launch did not count as a passing adapter check; the corrected run is recorded below.

Follow-up interactive lock run on 2026-10-03, reported by the tester using KSP 1.12.5 with other mods present: the corrected sandbox panel reported `ready`, the probe appeared and launched, and the saved test reward persisted after reload. In career `Phase1A`, the unrewarded probe was absent from VAB and R&D; with reward granted but `basicScience` unresearched, the log showed science/experimental/tech/purchase all false. After researching `basicScience`, the tester reported the probe present with all four flags true, surviving save/reload. Revoking the reward left an already launched vessel loadable while removing the probe from new builds. The saved probe craft then gave KSP's generic `contains locked or invalid parts` launch warning; granting the reward without editing that craft allowed launch, a same-craft differential check of the lock. In career `Phase1B`, the tester reported science researched and reward revoked with the probe locked; the imported craft was rejected, and switching A→B retained A's grant and B's lock. The current `persistent.sfs` files contain `probeRewarded = True` in A and `False` in B. The fresh `KSP.log` contains no Lodeworks type-load or reconciliation errors. These are user-reported gameplay observations supported by save/log inspection, not an agent-played clean-install run. A clean Squad-plus-Lodeworks run and the separate contract, stock fuel, and depot/mass checks remain open. Phase 1 is not yet accepted.

Science-mode follow-up, also reported by the tester in the modded installation: save `Phase1C` is `SCIENCE_SANDBOX`. The log shows a reward grant with `Science=False`, `Experimental=False`, `Tech=False`, `Purchased=False`; after `basicScience` was researched, a grant logged all four values true and the tester reported the probe appeared. This covers the science-mode availability sequence, but a post-grant science save/reload was not recorded. The clean Squad-plus-Lodeworks run and the other Phase 1 runtime areas remain open; do not advance to Phase 2 on these lock checks alone.

Clean-install follow-up on 2026-10-03: a separate KSP `1.12.5.3190` Steam build `03190` directory contained only `Squad` and `Lodeworks` in `GameData`; the original modded installation and saves were preserved. The opt-in harness was installed only in this test copy. In-game career lock tests covered purchase prevention, same imported craft rejection and launch after reward, reward persistence, and existing vessel loading. Sandbox launched the part. Science reward persistence and cross-save isolation were observed; clean science prerequisite research and imported-craft launch were not repeated. A real career contract was offered, accepted, saved/reloaded active, completed, and archived; the stock completion awards preceded the harness blueprint reward. Stock contracts were unavailable in science. Single-tank ground requests established partial reverse LF/OX amounts and mass conservation, and ground packed/unloaded mass was observed. See `tools/Phase1-Clean-KSP-Validation.md` for exact steps, logs, failures, and limits.

**Phase 1 remains open.** A later retained log captured both fuel directions and partial forward/reverse returns on the single-tank ground vessel. KSP's Set Orbit diagnostic produced two orbital states with stable core/vessel IDs; loaded, temporarily packed, and unloaded orbital mass matched after a 1 t cargo change. The clean science save then repeated Basic Science research, imported-craft lock, stock launch rejection, same-craft reward launch, and reward reload using diagnostic science points. Physically docked multi-tank flow, dock/undock depot identity and clearance, and orbital flight reload were not observed. Do not mark Phase 1 accepted or begin Phase 2.

