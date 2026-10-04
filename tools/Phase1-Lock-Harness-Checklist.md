# Phase 1 blueprint lock harness — KSP 1.12.5 runbook

This is an **opt-in diagnostic**, not campaign gameplay. It tests the current
`Phase1BlueprintAdapter` with one original probe part. The in-game reward button
stands in for a future contract receipt. Results from this runbook address the
part-lock portion of Phase 1 only; contracts, fuel bridge, and depot/mass checks
have their own remaining gate in `PHASE_STATUS.md`.

## Build and install

1. Set `KSP_ROOT` to a clean KSP 1.12.5 installation with Squad content.
2. Run `dotnet build .\Lodeworks.Phase1Harness\Lodeworks.Phase1Harness.csproj -c Release`.
3. Copy `artifacts\Phase1Harness\GameData\Lodeworks` into that installation's
   `GameData`. The diagnostic package contains `Lodeworks.dll`,
   `Lodeworks.Sim.dll`, `Lodeworks.Phase1Harness.dll`, and
   `Parts\lwPhase1LockProbe.cfg`. Do not install it alongside another copy of
   Lodeworks. Use disposable saves named `Phase1Sandbox`, `Phase1A`, and `Phase1B`;
   the harness adds its own scenario record to saves opened with it.
4. On the space center or in the VAB, look for **Lodeworks Phase 1 lock probe**.
   Its panel reports the save name, reward bit, reconciliation status, stock
   science state, experimental grant, tech availability, and purchase state.
   Press **Log current stock state** when recording each observation. Log lines
   begin `[Lodeworks Phase1 Harness]` in `KSP.log`.

If the panel says `waiting / failed`, the diagnostic part is missing, R&D is not
ready, or a tech-tree mod has created the reserved `lodeworksBlueprintLocked`
node. Record the log and stop that run; do not count it as a passing lock test.

## Repeatable checks

Record **pass/fail, a screenshot or short note, and the matching `KSP.log` lines**
for every row. Use a fresh KSP process for each save-switch sequence if an
unexpected state persists.

| ID | Steps | Expected result |
| --- | --- | --- |
| S1 | Create `Phase1Sandbox`; open VAB without researching anything. | Probe appears in Pods and can be selected and launched. Panel says mode `SANDBOX`, reconcile `ready`, and `Sandbox: part present; stock R&D not used`. KSP does not create an R&D instance in sandbox, so no experimental grant is expected there. |
| S2 | In `Phase1Sandbox`, make a craft containing the probe, save it as `Phase1Probe`, then exit the VAB. | Craft file exists under `saves\Phase1Sandbox\Ships\VAB`. Keep this file for the import checks. |
| A1 | Create career `Phase1A`; before researching `basicScience`, leave reward revoked. Open R&D and VAB. | Probe is absent from the editor and cannot be purchased in R&D. Panel reward `False`, experimental `False`. |
| A2 | In `Phase1A`, grant the test reward while `basicScience` is still unresearched. Reopen or refresh VAB. | Reward `True`, but the probe remains unavailable because its separate stock science prerequisite is unmet. |
| A3 | Research `basicScience` in `Phase1A`; visit space center/VAB after the harness reconciles. | Probe appears without an additional entry purchase. Panel shows science `True`, experimental `True`, tech `True`, purchased `True`. |
| A4 | Save the game, exit to main menu, reload `Phase1A`. | Reward and availability persist once; no duplicate or missing grant. |
| A5 | Put the unlocked probe on a craft, launch it, save, then revoke the test reward at the space center and reload. | Existing vessel still loads. A new copy of the probe is unavailable in the editor. |
| A6 | With reward revoked, copy `Phase1Probe.craft` from the sandbox save into `saves\Phase1A\Ships\VAB`, load it in VAB, and try to launch. | The stock preflight refuses launch with no proceed option. Record its exact on-screen message. |
| A7 | Grant reward again in `Phase1A` with `basicScience` researched; reopen the imported craft and try to launch. | Stock preflight permits launch. Do not rely only on the part appearing in the editor. |
| B1 | Create a second career `Phase1B`; research `basicScience` but do not grant the reward. Open VAB and try the copied craft. | Probe remains unavailable and imported craft launch is rejected. Panel reward `False`, experimental `False`. |
| B2 | Switch from `Phase1B` back to `Phase1A`, then back to `Phase1B`, checking the panel and VAB each time. | A retains its grant; B retains its lock. No cross-save grant leaks through process memory. |
| C1 | Repeat A1–A4 in a science-mode save if that mode is in scope for the release. | The career-style science prerequisite and reward rule apply; no sandbox bypass. |

For A6, a failed launch caused by missing crew, a facility limit, or another
preflight test is **not** proof of the blueprint lock. The observed failed test
must specifically identify the unavailable experimental probe. For A7, use a
launchable craft so a different preflight rule cannot hide a failure.

## Evidence record

Fill this in for the run and attach the relevant log excerpt and screenshots to
the Phase 1 status update or PR:

```text
KSP version/build:
Installed GameData folders:
Harness commit and DLL build time:
S1/S2:
A1/A2/A3/A4/A5/A6/A7:
B1/B2:
C1 (or not run):
KSP.log exceptions/type-load errors:
Tester and date:
```

After testing, remove the diagnostic `GameData\Lodeworks` package and keep the
disposable saves separate from normal gameplay. Do not mark Phase 1 accepted
until its other required game checks are also observed and recorded.

