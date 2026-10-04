# Phase 1 clean KSP validation — 2026-10-03

This report records only actions actually run in KSP. Phase 1 is **not accepted**. Section 18 and section 19.2 of `AI Master command prompt` remain the gate; no Phase 2 work was started.

## Installation and isolation

- Game: KSP `1.12.5.3190 (WindowsPlayer x64) en-us`; `buildID64.txt`: Steam build `03190`, 2022-12-12, master branch.
- Separate test directory: `work/KSP-1.12.5-clean`. Original Steam installation at `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program` and its saves were left intact.
- Top-level `GameData` contents: `Squad`, `Lodeworks` only. `Lodeworks` contained `Lodeworks.dll`, `Lodeworks.Sim.dll`, **opt-in** `Lodeworks.Phase1Harness.dll`, three diagnostic PART configs (`lwPhase1LockProbe.cfg`, `lwPhase1FuelDepotProbe.cfg`, `lwPhase1DockFuelProbe.cfg`), and generated probe thumbnails. No other mod directory was present.
- Test saves are inside the separate installation: `Phase1CleanSandbox`, `phase1cleancareera`, and `Phase1CleanScienceA`. Normal mod staging remains two DLLs; the harness is staged under `artifacts/Phase1Harness/GameData`, outside the normal package.
- Evidence logs in the task workspace `work/evidence`: `KSP-clean-run1.log`, `KSP-clean-run2.log`, `KSP-clean-run3-contract-deadline.log`, `KSP-clean-career-contract-success.log`, `KSP-clean-science-isolation.log`, `KSP-clean-science-complete.log`, `KSP-clean-fuel-bidirectional.log`, `KSP-clean-orbit-mass.log`, and `KSP-clean-docking-orbit-transfer.log`. Saved-state snapshots include `KSP-clean-docked-orbit-quicksave.sfs` and `KSP-clean-docked-100km-quicksave.sfs`. These are full copies, not edited excerpts. They are not committed because of size and machine-specific noise.

Final automated checks with `KSP_ROOT` set to the clean directory: `dotnet build .\Lodeworks.sln -c Release` passed with zero warnings/errors; `dotnet test .\Lodeworks.sln -c Release --no-build` passed 5/5; `tools\Verify-Phase1BlueprintApi.ps1` passed 11/11 public API/control-flow assertions; `tools\Verify-Phase1RuntimePackage.ps1` passed both the normal two-DLL and separate harness package checks.

## Blueprint lock in the clean game

1. In sandbox, the lock probe appeared in the VAB and launched. The panel reported ready. Sandbox has no R&D singleton; stock sandbox availability was used.
2. In career, with reward absent, the probe was locked. Granting the reward while `basicScience` was unresearched left Science, Experimental, Tech, and Purchased flags false. After researching Engineering 101, Survivability, then Basic Science, all four became true. Revoking reward left Science true but the other three false.
3. Imported the *same* probe craft from the clean sandbox save. Without reward, VAB reported locked/invalid parts. With reward, the craft loaded. Revoked reward after loading: stock launch preflight displayed `Unavailable Experimental Parts`, named the probe, and had no proceed option. Regranted reward without changing the craft: it launched to the pad.
4. Saved, revoked reward, visited Tracking Station, and loaded the already launched vessel. It remained accessible. Save/reload preserved the career reward state when granted.
5. In new science save, initial reward and stock availability were false. Granting reward before `basicScience` was researched kept all four stock flags false. Save/reload retained the grant. Revoked it, saved, then switched to career: career still had its grant and all four stock flags true.
6. Returned to the clean science save. Used KSP's built-in cheat control to add **100 diagnostic science points**, then researched Engineering 101 (5), Survivability (15), and Basic Science (45) in R&D, leaving 35. The point grant is a test fixture; the tech research and part checks occurred in KSP. With reward granted, Science/Experimental/Tech/Purchased were all true; revoking reward left only Science true (`KSP-clean-science-complete.log:5090-5091`).
7. Copied the same imported probe craft file into the clean science save. With reward revoked, the VAB loader marked it `Contains locked or invalid parts` and warned that the probe was missing. Granting reward made that warning disappear and the craft loaded. Revoking reward after loading, then clicking Launch, produced stock `Unavailable Experimental Parts`, `1x Lodeworks Phase 1 Lock Probe`, and `Unable to Launch`. Granting reward again without changing the craft launched it to the pad. After quicksave and reload, the panel again showed reward true and all four stock flags true (`:5161-5171`, `:5469`).

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

The same ground vessel showed mass modifier changes: cargo `+1 t` made connected mass `0.4600000083 -> 1.460000038 t`; after save and flight reload `KSP-clean-run1.log:3935,3992` logged the same `1.460000038 t` while packed then unpacked. Tracking Station logged the vessel unloaded/packed at `1.46000004 t` (`:3843`). Removing `1 t` cargo from the reverse fixture made mass `2.504999876 -> 1.504999995 t` (`:3999-4000`). The vessel GUID and persistent ID stayed stable across this ground scene transition.

For an actual orbital state change, used KSP's built-in **Set Orbit** test control on the sandbox vessel. `KSP-clean-orbit-mass.log:4711` shows the same vessel GUID `5b54ae8b-606f-4c2c-9e81-321b923dc31d`, persistent ID `4111271180`, and probe part ID `126435408` at Kerbin periapsis ~`86,700 m`, eccentricity `0`, inclination `0`, loaded/unpacked mass `1.504999995 t`. Adding 1 t cargo yielded `2.504999876 t` (`:4713-4714`). A second Set Orbit operation changed periapsis to ~`93,000 m`, eccentricity `0.01`, inclination `10`, with the same IDs and `2.504999876 t` while temporarily packed (`:4727`). After saving and leaving flight, Tracking Station logged `loaded=False packed=True totalMass+t=2.50499988` (`:4815`, repeated at `:4842-4866`). These are actual KSP orbital states made with a diagnostic control, not a propulsion maneuver. The orbiting craft lacked a command module, so Tracking Station did not offer Fly after unloading; an orbital flight reload was not verified.

## Docked fuel, identity, orbit, and mass follow-up

Built an additional opt-in one-part docking-port fuel probe. In clean sandbox, launched two separate probes, set the first to a circular ~86.75 km Kerbin orbit with KSP's Set Orbit control, and used KSP's Rendezvous control to place the second 100 m away. Invoked the probe's `DockToVessel` action; a KSP quicksave contained one vessel with both probe parts. Reloaded that craft into orbital flight from Tracking Station. Both parts reported the same vessel GUID and persistent ID while packed, LF 10/OX 12 in each tank, and connected mass `0.3199999928 t` (`KSP-clean-docking-orbit-transfer.log:3841-3844`).

On part `1426767126`, used the explicitly labeled diagnostic fixture to set its tank to LF 95/OX 114 and ledger to LF 10/OX 12. The other tank stayed LF 10/OX 12. In the docked, unpacked vessel, positive requests returned LF 50/OX 60, then the **partial** LF 45/OX 54; the selected tank went `95/114 -> 45/54 -> 0/0`, its ledger `10/12 -> 60/72 -> 105/126`, and the other tank remained `10/12` throughout (`:3904-3911`). A negative request then returned LF `+100`, OX `+120` to the selected tank; its ledger fell to `5/6`, and the other tank again remained `10/12` (`:3912-3914`). Connected mass was `1.3650000095 t` before and after each bridge step, within float rounding. These are actual `Part.RequestResource` returns. The fixture itself granted quantities and is excluded from conservation claims. **The test did not establish cross-part draw or flow priority:** despite the docked two-tank vessel, the request stopped when its own tank emptied. That behavior needs a deliberate reachable-tank flow check before claiming the broader gate.

Added 1 t diagnostic cargo to part `1426767126`: connected mass rose `1.3650000095 -> 2.3650000095 t` (`:3915-3917`). The diagnostic undock logged KSP's warning `Event Undock not assigned to state Disengage` (`:3918-3919`), but KSP subsequently reported two distinct one-part vessels: part `1233492552` kept GUID `bfe40c8d-6324-4121-9d4e-ccc16053b152`, persistent ID `2147007185`, mass `0.1599999964 t`; part `1426767126` moved to GUID `bc7372db-daf4-4307-afc6-ed4182707e33`, persistent ID `1086199055`, mass `2.2049999237 t` (`:3920-3922`). Their sum is `2.3649999201 t`, agreeing with pre-undock mass within float rounding. The KSP warning is retained as a diagnostic failure even though the observed split succeeded.

Invoked diagnostic `DockToVessel` again at `68.179 m`; KSP logged `Docking to vessel Phase1 Dock B` (`:3923-3927`). Both part IDs then reported the second vessel's GUID/persistent ID and combined mass `2.3650000095 t`, with each part's ledger and cargo amount unchanged (`:3928-3931`). KSP's Set Orbit changed the merged vessel from periapsis ~86.3 km to circular 100 km. Both parts retained their IDs and combined mass `2.3650000095 t` (`:3954-3957`). After quicksave, Tracking Station logged the **unloaded, packed two-core vessel** with the same GUID/persistent ID and `2.36499977 t` mass (`:4015`, repeated at `:4047`, `:4061`, `:4073`). On **orbital flight reload**, both `OnStart Orbital` entries reported `packed=True`, the same IDs, orbit, tank and ledger quantities, `1 t` cargo on only the intended part, and `2.3650000095 t` connected mass (`:4121-4124`). The final quicksave holds two parts in one vessel. The unloaded difference is ~`0.00000024 t`, consistent with float rounding; no duplicated or missing mass was observed in these loaded, packed, docked, undocked, unloaded, and reloaded states.

## Open validation limits

- The bridge selected a tank on the docked vessel and measured its actual stock transfer. The second tank remained unchanged, including when the selected tank exhausted. Choosing and verifying a *different* connected tank through stock resource-flow rules remains untested; this run does not claim it.
- The diagnostic logs identity and orbit but does not calculate depot registration clearance. No clearance result is claimed here.
- KSP 1.12.3 and 1.12.4 were not installed, so only 1.12.5 was run. The smoke test of available supported version bounds is still a release check.
- KSP logged `Event Undock not assigned to state Disengage`; the subsequent in-game split, ID remap, separate masses, and redock were observed. This warning remains visible for review.

Phase 1 remains open pending the unverified behavior above. Section 19.2 is **not** accepted as a release gate, and Phase 2 has not begun.

