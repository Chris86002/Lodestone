# Decisions

## Phase 0 — 2026-10-01

- Keep the existing MIT license. The repository's master document is the sole design authority.
- Use SDK-style projects targeting `netstandard2.0` for `Lodeworks.Sim`, `net48` for `Lodeworks`, and `net8.0` for tests, all with C# 9. A local .NET 10.0.401 SDK built the targets successfully; the .NET 8 runtime and .NET Framework 4.8 reference assemblies were installed.
- Pin KSPBuildTools to 1.0.0, Microsoft.NET.Test.Sdk to 17.14.1, xunit to 2.9.3, and xunit.runner.visualstudio to 3.1.4. The `NETStandard.Library` dependency is supplied implicitly by the SDK. The build uses the installed KSP files through `KSP_ROOT` and stages only the two Lodeworks DLLs.
- Compile probe references `PartModule` from the installed `Assembly-CSharp.dll` and `UnityEngine.MonoBehaviour` from the installed Unity assemblies. Their availability is compiler verified. No Phase 1 contract, part lock, funds, fuel bridge, depot, or mass modifier API has yet been validated.
- The installed game was found at `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program`. Its `buildID64.txt` reports Steam build 03190. This is an observation from this PC, not a repository default.

