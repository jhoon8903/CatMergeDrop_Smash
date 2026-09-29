using System;

namespace Projects.Scripts.GamePlay
{
    public enum MergeBoardChange
    {
        None,
        Move,
        Merge,
        Swap
    }

    public readonly struct MergeBoardOperation
    {
        public readonly MergeBoardChange Change;
        public readonly int SourceCatId;
        public readonly int TargetCatId;

        public MergeBoardOperation(MergeBoardChange change, int sourceCatId, int targetCatId)
        {
            Change = change;
            SourceCatId = sourceCatId;
            TargetCatId = targetCatId;
        }
    }

    /// <summary>
    /// Mutable maximum six-by-three merge board with row-major slot indices.
    /// </summary>
    public sealed class MergeBoardModel
    {
        public const int Columns = 6;
        public const int Rows = 3;
        public const int SlotCount = Columns * Rows;

        private readonly int[] _levels;
        private readonly int[] _catIds;

        public MergeBoardModel(int[] levels)
            : this(levels, CreateDefaultCatIds(levels)) { }

        public MergeBoardModel(int[] levels, int[] catIds)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            if (levels.Length != SlotCount) throw new ArgumentException($"The board must contain exactly {SlotCount} levels.", nameof(levels));
            if (catIds == null) throw new ArgumentNullException(nameof(catIds));
            if (catIds.Length != SlotCount) throw new ArgumentException($"The board must contain exactly {SlotCount} cat ids.", nameof(catIds));

            _levels = new int[SlotCount];
            _catIds = new int[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                if (levels[i] < 0) throw new ArgumentException($"Level at slot {i} cannot be negative.", nameof(levels));
                if ((levels[i] == 0) != (catIds[i] == 0)) throw new ArgumentException("Empty slots must have no cat id.", nameof(catIds));
                _levels[i] = levels[i];
                _catIds[i] = catIds[i];
            }
        }

        public int GetLevel(int index)
        {
            ValidateIndex(index);
            return _levels[index];
        }

        public int GetCatId(int index)
        {
            ValidateIndex(index);
            return _catIds[index];
        }

        public int[] CopyLevels()
        {
            int[] copy = new int[SlotCount];
            Array.Copy(_levels, copy, SlotCount);
            return copy;
        }

        public int[] CopyCatIds()
        {
            int[] copy = new int[SlotCount];
            Array.Copy(_catIds, copy, SlotCount);
            return copy;
        }

        public bool TryMoveOrMerge(int source, int target, out MergeBoardChange change)
        {
            bool changed = TryMoveOrMerge(source, target, out MergeBoardOperation operation);
            change = operation.Change;
            return changed;
        }

        public bool TryMoveOrMerge(int source, int target, out MergeBoardOperation operation)
        {
            operation = new MergeBoardOperation(MergeBoardChange.None, 0, 0);
            if (!IsValidIndex(source) || !IsValidIndex(target) || source == target) return false;
            int sourceLevel = _levels[source];
            if (sourceLevel == 0) return false;
            int targetLevel = _levels[target];
            if (targetLevel == 0)
            {
                _levels[target] = sourceLevel;
                _levels[source] = 0;
                _catIds[target] = _catIds[source];
                _catIds[source] = 0;
                operation = new MergeBoardOperation(MergeBoardChange.Move, _catIds[target], 0);
                return true;
            }

            if (sourceLevel == targetLevel)
            {
                if (targetLevel == int.MaxValue) return false;
                _levels[target] = targetLevel + 1;
                _levels[source] = 0;
                int sourceId = _catIds[source];
                _catIds[source] = 0;
                operation = new MergeBoardOperation(MergeBoardChange.Merge, sourceId, _catIds[target]);
                return true;
            }

            _levels[source] = targetLevel;
            _levels[target] = sourceLevel;
            int sourceIdForSwap = _catIds[source];
            _catIds[source] = _catIds[target];
            _catIds[target] = sourceIdForSwap;
            operation = new MergeBoardOperation(MergeBoardChange.Swap, sourceIdForSwap, _catIds[source]);
            return true;
        }

        public bool TryRemove(int slot, out int catId, out int level)
        {
            catId = 0;
            level = 0;
            if (!IsValidIndex(slot) || _levels[slot] == 0) return false;
            catId = _catIds[slot];
            level = _levels[slot];
            _levels[slot] = 0;
            _catIds[slot] = 0;
            return true;
        }

        private static int[] CreateDefaultCatIds(int[] levels)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            int[] ids = new int[levels.Length];
            for (int i = 0; i < levels.Length; i++) if (levels[i] > 0) ids[i] = i + 1;
            return ids;
        }

        private static bool IsValidIndex(int index) => index >= 0 && index < SlotCount;

        private static void ValidateIndex(int index)
        {
            if (!IsValidIndex(index)) throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
