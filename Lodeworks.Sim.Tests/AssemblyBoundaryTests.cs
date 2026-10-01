using System;
using System.Linq;
using System.Runtime.Versioning;
using Xunit;

namespace Lodeworks.Sim.Tests
{
    public sealed class AssemblyBoundaryTests
    {
        [Fact]
        public void SimulationTargetsNetStandardAndHasNoGameReferences()
        {
            var assembly = typeof(SimulationMarker).Assembly;
            var target = (TargetFrameworkAttribute?)Attribute.GetCustomAttribute(
                assembly, typeof(TargetFrameworkAttribute));

            Assert.Equal(".NETStandard,Version=v2.0", target?.FrameworkName);
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name!.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                reference.Name == "Assembly-CSharp");
        }
    }
}

