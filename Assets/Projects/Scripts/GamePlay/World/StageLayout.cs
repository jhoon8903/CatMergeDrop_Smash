using System;

namespace Projects.Scripts.GamePlay
{
    /// <summary>Playable board and brick dimensions for a stage. View pools stay at their authored maximum.</summary>
    public readonly struct StageLayout
    {
        public const int MinimumBoardSlots = 3 * 3;
        public int BoardColumns { get; }
        public int BoardRows { get; }
        public int BrickColumns { get; }
        public int BrickRows { get; }
        public int BoardSlotCount => BoardColumns * BoardRows;
        public int BrickSlotCount => BrickColumns * BrickRows;
        public float WallInnerX => BoardColumns * WorldPlayfieldLayout.BoardColumnPitch * .5f;

        public StageLayout(int boardColumns, int boardRows, int brickColumns, int brickRows)
        {
            if (boardColumns < 1 || boardRows < 1 || brickColumns != boardColumns || brickRows < 1 ||
                boardColumns * boardRows > MergeBoardModel.SlotCount ||
                brickColumns * brickRows > WorldPlayfieldLayout.BrickCount)
                throw new ArgumentOutOfRangeException(nameof(boardColumns),
                    "Brick columns must match board columns and fit the authored view pools.");
            BoardColumns = boardColumns;
            BoardRows = boardRows;
            BrickColumns = brickColumns;
            BrickRows = brickRows;
        }

    }
}
