# Phase 1 clean KSP validation — 2026-10-03

This report records only actions actually run in KSP. Phase 1 is **not accepted**. Section 18 and section 19.2 of `AI Master command prompt` remain the gate; no Phase 2 work was started.

## Installation and isolation

- Game: KSP `1.12.5.3190 (WindowsPlayer x64) en-us`; `buildID64.txt`: Steam build `03190`, 2022-12-12, master branch.
- Separate test directory: `work/KSP-1.12.5-clean`. Original Steam installation at `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program` and its saves were left intact.
- Top-level `GameData` contents: `Squad`, `Lodeworks` only. `Lodeworks` contained `Lodeworks.dll`, `Lodeworks.Sim.dll`, **opt-in** `Lodeworks.Phase1Harness.dll`, two diagnostic PART configs (`lwPhase1LockProbe.cfg`, `lwPhase1FuelDepotProbe.cfg`), and the generated fuel-part thumbnail. No other mod directory was present.
- Test saves are inside the separate installation: `Phase1CleanSandbox`, `phase1cleancareera`, and `Phase1CleanScienceA`. Normal mod staging remains two DLLs; the harness is staged under `artifacts/Phase1Harness/GameData`, outside the normal package.
- Evidence logs in the task workspace `work/evidence`: `KSP-clean-run1.log`, `KSP-clean-run2.log`, `KSP-clean-run3-contract-deadline.log`, `KSP-clean-career-contract-success.log`, `KSP-clean-science-isolation.log`, `KSP-clean-fuel-bidirectional.log`. These are full copies of `KSP.log`, not edited excerpts. They are not committed because of size and machine-specific noise.

Final automated checks with `KSP_ROOT` set to the clean directory: `dotnet build .\Lodeworks.sln -c Release` passed with zero warnings/errors; `dotnet test .\Lodeworks.sln -c Release --no-build` passed 5/5; `tools\Verify-Phase1BlueprintApi.ps1` passed 11/11 public API/control-flow assertions; `tools\Verify-Phase1RuntimePackage.ps1` passed both the normal two-DLL and separate harness package checks.

## Blueprint lock in the clean game

1. In sandbox, the lock probe appeared in the VAB and launched. The panel reported ready. Sandbox has no R&D singleton; stock sandbox availability was used.
2. In career, with reward absent, the probe was locked. Granting the reward while `basicScience` was unresearched left Science, Experimental, Tech, and Purchased flags false. After researching Engineering 101, Survivability, then Basic Science, all four became true. Revoking reward left Science true but the other three false.
3. Imported the *same* probe craft from the clean sandbox save. Without reward, VAB reported locked/invalid parts. With reward, the craft loaded. Revoked reward after loading: stock launch preflight displayed `Unavailable Experimental Parts`, named the probe, and had no proceed option. Regranted reward without changing the craft: it launched to the pad.
4. Saved, revoked reward, visited Tracking Station, and loaded the already launched vessel. It remained accessible. Save/reload preserved the career reward state when granted.
5. In new science save, initial reward and stock availability were false. Granting reward before `basicScience` was researched kept all four stock flags false. Save/reload retained the grant. Revoked it, saved, then switched to career: career still had its grant and all four stock flags true. The clean science test did **not** repeat research-to-unlock and imported-craft launch; the prior modded science test did report research-to-unlock.

## Contract lifecycle

KSP's new-save screen explicitly says contracts are unavailable in Science mode. The harness attempted an offer there and logged `Offer unavailable in mode=SCIENCE_SANDBOX; ContractSystem=False` (`KSP-clean-science-isolation.log:3930`). Sandbox likewise has no contract system. Career is the supported stock contract mode.

In career, used the opt-in panel to generate an actual `Contract` subclass. Mission Control showed an offered contract with a 10-day duration. Accepted through Mission Control, saved through KSP, loaded the quicksave, then completed the active diagnostic parameter from the panel. Mission Control moved it to archive. The log shows stock advance and completion awards before the blueprint grant:

```text
KSP-clean-career-contract-success.log:3738  OnOffered state=Offered funds=24141.440002441406
:3743  Awarding 100 funds to player for contract advance
:3744  OnAccepted state=Active funds=24241.440002441406
:3761  OnSave state=Active funds=24241.440002441406
:3799  OnLoad state=Active funds=24241.440002441406
:3814  Before parameter completion state=Active funds=24241.440002441406
:3816-3818  Awarding 100 funds, 1 science, 1 reputation for contract completion
:3820  AwardCompletion after funds=24341.440002441406
:3831  Stock onCompleted; first blueprint reward=True state=Completed funds=24341.440002441406 Science=True Experimental=True Tech=True Purchased=True
:3838  OnSave state=Completed funds=24341.440002441406
```

Earlier diagnostic failures were retained: first offer generation failed while `MeetRequirements` returned false; after that fix, a contract without a deadline expired immediately after acceptance and penalized 100 funds (`KSP-clean-run3-contract-deadline.log`). The harness now sets a 10-day deadline. These were diagnostic harness defects; the successful run above used the corrected harness.

## Stock fuel and mass

The flight probe uses the real `Part.RequestResource` API and logs selected part tank, local ledger, and `Vessel.GetTotalMass()`. Ground vessel contained a lock probe and one diagnostic LF/OX tank. Initially the selected tank held LF `10/100`, OX `12/120`, ledger `0/0`, connected mass `0.4600000083 t`. A `+50/+60` stock-to-ledger request returned the available `10/12`: tank `0/0`, ledger `10/12`, mass unchanged. A reverse `-10/-12` request restored the tank to `10/12`, ledger `0/0`, mass unchanged. These initial observations were read from the in-game panel but were not captured in a retained log.

A later retained run gives stronger bidirectional evidence (`KSP-clean-fuel-bidirectional.log:4273-4279`): selected tank `100/100` LF and `120/120` OX, ledger `5/6`, mass `1.504999995 t`; stock-to-ledger returned stock deltas `-50/-60`, ledger `+50/+60`, selected tank `50/60`, mass unchanged. Reversing returned `+50/+60` to stock and `-50/-60` to ledger, restoring the original amounts and mass. For **partial forward** exhaustion, the diagnostic fixture set tank `95/114`, ledger `10/12` without changing total mass; the first request moved `50/60`, the second requested `50/60` but actually moved only `45/54` (`:4484-4490`). Final tank `0/0`, ledger `105/126`, and mass remained `1.504999995 t`. The fixture explicitly grants/sets quantities for testing; its creation is not a bridge transaction.

For a logged partial reverse, the diagnostic fixture explicitly set the selected tank to LF `95/100`, OX `114/120` and ledger to `10/12`. `KSP-clean-run1.log:3996-3998` records request/reply: stock delta LF `+5`, OX `+6`; ledger delta LF `-5`, OX `-6`; tank became `100/120`, ledger `5/6`, connected mass stayed `2.504999876 t`. The fixture *grants* quantities for testing; it is not a legitimate transfer or conservation evidence across fixture creation.

The same ground vessel showed mass modifier changes: cargo `+1 t` made connected mass `0.4600000083 -> 1.460000038 t`; after save and flight reload `KSP-clean-run1.log:3935,3992` logged the same `1.460000038 t` while packed then unpacked. Tracking Station logged the vessel unloaded/packed at `1.46000004 t` (`:3843`). Removing `1 t` cargo from the reverse fixture made mass `2.504999876 -> 1.504999995 t` (`:3999-4000`). The vessel GUID and persistent ID stayed stable across this ground scene transition; the reported orbit values changed from landed initialization to landed physics, **not an orbital maneuver**.

## Open Phase 1 gates

- Repeat forward and reverse partial LF/OX on a **physically docked** vessel with multiple reachable tanks; capture actual return amounts, which tank supplied/received, ledger, and connected mass in retained `KSP.log` before/after. The current single-tank ground check proves sign and partial return, but not flow priority or docking reachability.
- Observe a depot core through docking and undocking with vessel GUID/persistent ID remap; then change orbit and verify the bound core, body, orbit basics, and clearance response. Only an undocked ground vessel was observed.
- Verify persisted mass modifier in an orbiting vessel across loaded, packed, and unloaded transitions and docking/undocking, including duplicate/missing mass checks. Current ground loaded/packed/unloaded observations are narrower.
- Complete clean science-mode prerequisite research, purchase prevention, imported-craft launch rejection, and rewarded launch differential. The earlier modded science-mode report is supplemental, not a clean-environment substitute.
- KSP 1.12.3 and 1.12.4 were not installed for smoke tests.

No Phase 1 acceptance or Phase 2 implementation is justified by this record.

