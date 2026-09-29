using System;

namespace Projects.Scripts.GamePlay
{
    /// <summary>Fixed sale payout by Cat level. Defaults always stay below the cheapest summon.</summary>
    public static class CatSaleRules
    {
        private static readonly int[] DefaultPrices = { 2, 3, 4, 5, 6, 7, 8, 9 };

        public static int PriceForLevel(int level, int[] configuredPrices = null)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            int[] prices = configuredPrices is { Length: > 0 } ? configuredPrices : DefaultPrices;
            return Math.Max(0, prices[Math.Min(level - 1, prices.Length - 1)]);
        }
    }
}
