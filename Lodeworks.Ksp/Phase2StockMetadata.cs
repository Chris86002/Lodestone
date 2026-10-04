using System;
using Lodeworks.Sim;

namespace Lodeworks.Ksp
{
    // Main-thread read only. This adapter is intentionally not a player command.
    public static class Phase2StockMetadata
    {
        public static bool TryRead(out StockFuelMetadata metadata, out string failure)
        {
            metadata = default;
            failure = string.Empty;
            var library = PartResourceLibrary.Instance;
            var lf = library?.GetDefinition("LiquidFuel");
            var ox = library?.GetDefinition("Oxidizer");
            if (lf == null || ox == null)
            {
                failure = "Stock LiquidFuel/Oxidizer definitions are unavailable.";
                return false;
            }
            try
            {
                metadata = new StockFuelMetadata(lf.density, ox.density, lf.unitCost, ox.unitCost);
                return true;
            }
            catch (ArgumentException)
            {
                failure = "Stock fuel density or unitCost is invalid.";
                return false;
            }
        }
    }
}

