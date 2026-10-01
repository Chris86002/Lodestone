# Decisions

## Phase 0 — 2026-10-01

- Keep the existing MIT license. The repository's master document is the sole design authority.
- Use SDK-style projects targeting `netstandard2.0` for `Lodeworks.Sim`, `net48` for `Lodeworks`, and `net8.0` for tests, all with C# 9. A local .NET 10.0.401 SDK built the targets successfully; the .NET 8 runtime and .NET Framework 4.8 reference assemblies were installed.
- Pin KSPBuildTools to 1.0.0, Microsoft.NET.Test.Sdk to 17.14.1, xunit to 2.9.3, and xunit.runner.visualstudio to 3.1.4. The `NETStandard.Library` dependency is supplied implicitly by the SDK. The build uses the installed KSP files through `KSP_ROOT` and stages only the two Lodeworks DLLs.
- Compile probe references `PartModule` from the installed `Assembly-CSharp.dll` and `UnityEngine.MonoBehaviour` from the installed Unity assemblies. Their availability is compiler verified. No Phase 1 contract, part lock, funds, fuel bridge, depot, or mass modifier API has yet been validated.
- The installed game was found at `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program`. Its `buildID64.txt` reports Steam build 03190. This is an observation from this PC, not a repository default.

## Phase 1 API feasibility — 2026-10-01

Design authority: `AI Master command prompt` revision 5, phase 1 in section 18, with sections 4.3, 9.5-9.6, 11.1, 11.3, 15, and 19.2. Phase 0 is the only dependency. The Phase 1 code supplies pure gate/bridge/mass decisions and compile-time KSP adapter probes; it does not register gameplay commands or silently create campaign state.

Verified by inspecting public metadata in this PC's `KSP_x64_Data/Managed/Assembly-CSharp.dll` using the bundled `Mono.Cecil.dll`, then compiling against the same installation (Steam build 03190):

| Area | Observed public signatures / members | Decision |
| --- | --- | --- |
| Contracts | `Contracts.ContractSystem.Instance`, `Contracts.ContractSystem.GenerateContract(int, Contract.ContractPrestige, Type)`, `Contracts.Contract.Generate(Type, Contract.ContractPrestige, int, Contract.State)`, `Contract.OnLoad(ConfigNode)`, `Contract.OnSave(ConfigNode)`, `ContractParameter.SetComplete()` (protected) | A stock contract subclass is plausible. Acceptance/reward/save ordering is unverified in-game. No campaign contract is registered in this phase. |
| Part state | `AvailablePart.name`, `AvailablePart.TechRequired`, `ResearchAndDevelopment.Instance`, `PartTechAvailable(AvailablePart)`, `PartModelPurchased(AvailablePart)`, `RDTech.PurchasePart(AvailablePart)`, `RDTech.partsPurchased` | Read stock tech and purchase state. The pure `BlueprintGate` keeps reward sets per save. Do not mutate shared part prefabs. |
| Editor and launch | `KSP.UI.Screens.EditorPartList.ExcludeFilters`, `EditorPartList.Refresh()`, `EditorPartListFilterList<T>.AddFilter(...)`, `EditorLogic.launchVessel()`; `PreFlightCheck.AddTest(IPreFlightTest)` is public but `EditorLogic.GetStockPreFlightCheck(string)` is private and `launchVessel` is not virtual; `GameEvents.onEditorShipModified` exists, but no cancellable prelaunch event was found in the public metadata | Editor filters can hide locked parts. They do not prove that an imported craft cannot launch or that R&D purchase is blocked. No cosmetic or UI-only lock is claimed. **Phase 1 gate blocked** pending a verified public enforcement route, or a design amendment that changes the requirement. |
| Funds | `Funding.Instance`, `Funding.Funds`, `Funding.AddFunds(double, TransactionReasons)`, `Funding.CanAfford(float)` | Compile-probed only. Durable receipts and reconciliation are needed before any actual charge. |
| Stock fuel | `Part.RequestResource(string, double)`, `PartResource.amount/maxAmount`, `PartResourceLibrary.Instance.GetDefinition(string)`, `PartResourceDefinition.density`, `Vessel.GetConnectedResourceTotals(int, out double, out double, bool)` | Pure accounting books actual returned units, including partial and zero transfer. This signature does not establish sign, selected-tank flow reachability, or atomic two-component behavior. In-game checks remain required. |
| Depot identity/orbit | `Vessel.id`, `Vessel.persistentId`, `Vessel.orbit`, `Orbit.referenceBody/eccentricity/inclination/epoch/PeA`, `ProtoVessel.vesselID`, `GameEvents.onVesselPersistentIdChanged` and `onVesselDocking` | Vessel GUID is a reference, not a stable depot identity. The probe reads bound-orbit basics only. Dock/undock remapping, safe clearance, and loaded/unloaded data remain unverified. |
| Mass | `IPartMassModifier.GetModuleMass(float, ModifierStagingSituation) : float`, `GetModuleMassChangeWhen() : ModifierChangeWhen`, `Part.UpdateMass()`, `Vessel.GetTotalMass()` | A compiled persisted probe returns a per-core cargo mass delta. Packed/unloaded propagation and conservation with stock tanks need game observation. No whole-vessel rewrite is performed. |

The net48 project now references the installed .NET Framework 4.8 `netstandard` facade with `Private=false`, because compiling calls to `Lodeworks.Sim`'s public value types otherwise fails with CS0012. The facade is not staged. No KSP/Unity assembly is committed or packaged. The installed game is 1.12.5; 1.12.3 and 1.12.4 were not available for checks. `KSP_ROOT` was absent from this process environment, so build/test commands set it locally to the installed Steam directory.

