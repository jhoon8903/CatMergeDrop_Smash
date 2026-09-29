using Projects.Scripts.GamePlay.Drop.Ecs.Enums;

namespace Projects.Scripts.GamePlay.Drop
{
    /// <summary>Deterministic initial brick pattern for each stage.</summary>
    public static class StageBrickPattern
    {
        public static BrickKind GetKind(int row, int column)
        {
            if (row % 3 == 1 && column == 1) return BrickKind.Gold;
            return row % 3 == 2 && column == 2 ? BrickKind.Item : BrickKind.Normal;
        }

        public static int ChipSideFor(int stageIndex, int row, int column)
            => (row + column + stageIndex) % 2 == 0 ? -1 : 1;

    }
}
