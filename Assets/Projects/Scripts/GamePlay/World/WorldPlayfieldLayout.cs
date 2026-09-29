namespace Projects.Scripts.GamePlay
{
    /// <summary>Shared world-space coordinates for the board, drop rows, and goal.</summary>
    public static class WorldPlayfieldLayout
    {
        public const float BoardCenterY = 0f;
        public const float BoardColumnPitch = 1.2f;
        public const float BoardRowPitch = 1.2f;
        public const float BoardCellWidth = BoardColumnPitch;
        public const float BoardCellHeight = BoardRowPitch;
        public const float BoardHalfWidth = MergeBoardModel.Columns * BoardColumnPitch * .5f;
        public const float CatVisualSize = 1.08f;
        public const float CatCollisionRadius = CatVisualSize * .5f;
        public const float CatVisualOffsetY = .06f;
        public const int BrickRows = 10;
        public const int BrickCount = BrickRows * MergeBoardModel.Columns;
        public const float DropStartY = -4.2f;
        public const float DropTravel = 18f;
        public static float GoalProgress => GoalProgressForLastBrickRow(BrickRows - 1);
        public const float FirstTileProgress = .26f;
        public const float TileRowPitch = BoardRowPitch;
        // Level 15 has the widest transparent margin (about 6% vertically), so
        // sprites need this extent to meet the next row without a visible seam.
        public const float TileWidth = 1.28f;
        public const float TileHeight = 1.28f;

        public static float ColumnX(int column) => ColumnX(column, MergeBoardModel.Columns);
        public static float ColumnX(int column, int columnCount) =>
            (column - (columnCount - 1) * .5f) * BoardColumnPitch;
        public static int ColumnAtX(float x, int columnCount) =>
            UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(x / BoardColumnPitch +
                (columnCount - 1) * .5f), 0, columnCount - 1);
        public static float BoardRowY(int row) => BoardRowY(row, MergeBoardModel.Rows);
        public static float BoardRowY(int row, int rowCount) =>
            BoardCenterY + ((rowCount - 1) * .5f - row) * BoardRowPitch;
        public static float DropY(float progress) => DropStartY - progress * DropTravel;
        public static float TileProgress(int row) => FirstTileProgress + row * TileRowPitch / DropTravel;
        public static float TileContactProgress(int row) => TileProgress(row) -
            (TileHeight * .5f + CatVisualSize * .5f - CatVisualOffsetY) / DropTravel;
        public static float GoalProgressForLastBrickRow(int lastBrickRow) =>
            TileProgress(UnityEngine.Mathf.Max(0, lastBrickRow)) +
            (TileHeight * .5f + .25f) / DropTravel;
    }
}
