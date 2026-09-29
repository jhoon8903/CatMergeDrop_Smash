using System;

namespace Projects.Scripts.GamePlay
{
    /// <summary>Cat level utilities independent of stage balance tuning.</summary>
    public static class CatSummonRules
    {
        public static int HighestBoardLevel(int[] levels)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            int highest = 1;
            for (int i = 0; i < levels.Length; i++)
                highest = Math.Max(highest, levels[i]);
            return highest;
        }

    }
}
