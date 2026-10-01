using System;

namespace Lodeworks.Ksp
{
    // Phase 0 compile probe: these types must come from the installed game.
    internal static class KspAssemblyProbe
    {
        internal static readonly Type PartModuleType = typeof(PartModule);
        internal static readonly Type UnityComponentType = typeof(UnityEngine.MonoBehaviour);
        internal static readonly string SimulationAssembly = Sim.SimulationMarker.AssemblyIdentity;
    }
}

