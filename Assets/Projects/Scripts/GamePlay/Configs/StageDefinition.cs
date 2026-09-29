using System;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using UnityEngine;

namespace Projects.Scripts.GamePlay.Configs
{
    [Serializable]
    public struct StageBrickCell
    {
        public bool Present;
        public BrickKind Kind;
        public int Level;
    }

    [CreateAssetMenu(fileName = "StageDefinition", menuName = "Cat Merge Drop/Stage Definition")]
    public sealed class StageDefinition : ScriptableObject
    {
        [SerializeField, Min(1)] private int stageIndex = 1;
        [SerializeField, Min(1)] private int boardColumns = 4;
        [SerializeField, Min(1)] private int brickRows = 10;
        [SerializeField, Range(0, 100), Tooltip("0 uses the shared stage Drop budget.")]
        private int dropBudget;
        public int DropBudgetOverride => dropBudget;
        [Serializable]
        private sealed class RevisionSnapshot
        {
            public int Revision, Columns, Rows;
            public StageBrickCell[] Cells;
        }
        [SerializeField] private int catalogRevision;
        [SerializeField] private RevisionSnapshot[] previousRevisions = Array.Empty<RevisionSnapshot>();
        public int CatalogRevision => catalogRevision;
        [SerializeField] private StageBrickCell[] cells = Array.Empty<StageBrickCell>();

        public int StageIndex => stageIndex;
        public int BoardColumns => boardColumns;
        public int BrickRows => brickRows;

        private RevisionSnapshot PreviousRevision(int revision)
        {
            if (previousRevisions != null)
                foreach (var snapshot in previousRevisions)
                    if (snapshot.Revision == revision) return snapshot;
            throw new InvalidOperationException($"Stage {stageIndex} has no catalog revision {revision}.");
        }

        public int ColumnsForRevision(int revision) => revision == catalogRevision
            ? boardColumns : PreviousRevision(revision).Columns;
        public int RowsForRevision(int revision) => revision == catalogRevision
            ? brickRows : PreviousRevision(revision).Rows;
        public StageBrickCell GetCell(int row, int column, int revision)
        {
            if (revision == catalogRevision) return GetCell(row, column);
            var snapshot = PreviousRevision(revision);
            if (row < 0 || column < 0 || row >= snapshot.Rows || column >= snapshot.Columns) return default;
            int index = row * snapshot.Columns + column;
            return index < snapshot.Cells.Length ? snapshot.Cells[index] : default;
        }

        public StageBrickCell GetCell(int row, int column)
        {
            if (row < 0 || column < 0 || row >= brickRows || column >= boardColumns || cells == null)
                return default;
            int index = row * boardColumns + column;
            return index >= 0 && index < cells.Length ? cells[index] : default;
        }

        public void Configure(int configuredStageIndex, int configuredBoardColumns, int configuredBrickRows,
            StageBrickCell[] configuredCells)
        {
            // Keep exact brick identities for checkpoints saved before a catalog update.
            var incoming = configuredCells ?? Array.Empty<StageBrickCell>();
            bool changed = boardColumns != configuredBoardColumns || brickRows != configuredBrickRows ||
                           cells == null || cells.Length != incoming.Length;
            if (!changed)
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i].Present != incoming[i].Present || cells[i].Kind != incoming[i].Kind ||
                        cells[i].Level != incoming[i].Level) { changed = true; break; }
            if (changed && cells != null && cells.Length > 0)
            {
                int count = previousRevisions?.Length ?? 0;
                Array.Resize(ref previousRevisions, count + 1);
                previousRevisions[count] = new RevisionSnapshot { Revision = catalogRevision,
                    Columns = boardColumns, Rows = brickRows, Cells = (StageBrickCell[])cells.Clone() };
                catalogRevision++;
            }
            stageIndex = Mathf.Max(1, configuredStageIndex);
            boardColumns = Mathf.Max(1, configuredBoardColumns);
            brickRows = Mathf.Max(1, configuredBrickRows);
            cells = configuredCells == null ? Array.Empty<StageBrickCell>() : (StageBrickCell[])configuredCells.Clone();
        }
    }
}
