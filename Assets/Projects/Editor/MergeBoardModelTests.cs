using System;
using NUnit.Framework;
using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Configs;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class MergeBoardModelTests
    {
        [Test]
        public void TryMoveOrMerge_MovesIntoEmptyTarget()
        {
            var board = CreateBoard(2, 0);

            bool changed = board.TryMoveOrMerge(0, 1, out MergeBoardChange change);

            Assert.That(changed, Is.True);
            Assert.That(change, Is.EqualTo(MergeBoardChange.Move));
            Assert.That(board.CopyLevels(), Is.EqualTo(CreateLevels(0, 2)));
        }

        [Test]
        public void TryMoveOrMerge_MergesEqualOccupiedLevels()
        {
            var board = CreateBoard(3, 3);

            bool changed = board.TryMoveOrMerge(0, 1, out MergeBoardChange change);

            Assert.That(changed, Is.True);
            Assert.That(change, Is.EqualTo(MergeBoardChange.Merge));
            Assert.That(board.CopyLevels(), Is.EqualTo(CreateLevels(0, 4)));
        }

        [Test]
        public void TryMoveOrMerge_SwapsUnequalOccupiedLevels()
        {
            var board = CreateBoard(2, 5);

            bool changed = board.TryMoveOrMerge(0, 1, out MergeBoardChange change);

            Assert.That(changed, Is.True);
            Assert.That(change, Is.EqualTo(MergeBoardChange.Swap));
            Assert.That(board.CopyLevels(), Is.EqualTo(CreateLevels(5, 2)));
        }

        [TestCase(-1, 0)]
        [TestCase(0, MergeBoardModel.SlotCount)]
        [TestCase(0, 0)]
        public void TryMoveOrMerge_InvalidMoveLeavesBoardUnchanged(int source, int target)
        {
            var board = CreateBoard(2, 3);
            int[] before = board.CopyLevels();

            bool changed = board.TryMoveOrMerge(source, target, out MergeBoardChange change);

            Assert.That(changed, Is.False);
            Assert.That(change, Is.EqualTo(MergeBoardChange.None));
            Assert.That(board.CopyLevels(), Is.EqualTo(before));
        }

        [Test]
        public void TryMoveOrMerge_EmptySourceAndOverflowMergeLeaveBoardUnchanged()
        {
            var emptySourceBoard = CreateBoard(0, 2);
            var overflowBoard = CreateBoard(int.MaxValue, int.MaxValue);
            int[] emptyBefore = emptySourceBoard.CopyLevels();
            int[] overflowBefore = overflowBoard.CopyLevels();

            Assert.That(emptySourceBoard.TryMoveOrMerge(0, 1, out MergeBoardChange emptyChange), Is.False);
            Assert.That(emptyChange, Is.EqualTo(MergeBoardChange.None));
            Assert.That(emptySourceBoard.CopyLevels(), Is.EqualTo(emptyBefore));

            Assert.That(overflowBoard.TryMoveOrMerge(0, 1, out MergeBoardChange overflowChange), Is.False);
            Assert.That(overflowChange, Is.EqualTo(MergeBoardChange.None));
            Assert.That(overflowBoard.CopyLevels(), Is.EqualTo(overflowBefore));
        }

        [Test]
        public void ConstructorAndCopyLevels_CreateIndependentSnapshots()
        {
            int[] input = CreateLevels(1, 2);
            var board = new MergeBoardModel(input);
            input[0] = 9;
            int[] snapshot = board.CopyLevels();
            snapshot[1] = 8;

            Assert.That(board.GetLevel(0), Is.EqualTo(1));
            Assert.That(board.GetLevel(1), Is.EqualTo(2));
        }

        [Test]
        public void Constructor_RejectsInvalidBoardData()
        {
            Assert.Throws<ArgumentNullException>(() => new MergeBoardModel(null));
            Assert.Throws<ArgumentException>(() => new MergeBoardModel(new int[MergeBoardModel.SlotCount - 1]));
            Assert.Throws<ArgumentException>(() => new MergeBoardModel(CreateLevels(-1)));
        }

        [Test]
        public void TryMoveOrMerge_PreservesCatIdentityAndReportsConsumedCat()
        {
            int[] levels = CreateLevels(3, 3);
            var board = new MergeBoardModel(levels, CreateLevels(11, 22));

            Assert.That(board.TryMoveOrMerge(0, 1, out MergeBoardOperation operation), Is.True);

            Assert.That(operation.Change, Is.EqualTo(MergeBoardChange.Merge));
            Assert.That(operation.SourceCatId, Is.EqualTo(11));
            Assert.That(operation.TargetCatId, Is.EqualTo(22));
            Assert.That(board.GetCatId(0), Is.EqualTo(0));
            Assert.That(board.GetCatId(1), Is.EqualTo(22));
        }

        [TestCase(1, 2)]
        [TestCase(2, 3)]
        [TestCase(7, 8)]
        [TestCase(8, 9)]
        [TestCase(99, 9)]
        public void SalePrice_DefaultScheduleIsFixedByLevelAndBelowMinimumSummon(int level, int expected)
        {
            int price = CatSaleRules.PriceForLevel(level);

            Assert.That(price, Is.EqualTo(expected));
            StageBalanceConfig stageBalanceConfig = ScriptableObject.CreateInstance<StageBalanceConfig>();
            try { Assert.That(price, Is.LessThan(stageBalanceConfig.SummonCostFor(1))); }
            finally { UnityEngine.Object.DestroyImmediate(stageBalanceConfig); }
        }

        [Test]
        public void SalePrice_ConfiguredScheduleUsesLastPriceForHigherLevels()
        {
            Assert.That(CatSaleRules.PriceForLevel(3, new[] { 4, 6 }), Is.EqualTo(6));
        }

        private static MergeBoardModel CreateBoard(int first, int second) => new MergeBoardModel(CreateLevels(first, second));

        private static int[] CreateLevels(int first = 0, int second = 0)
        {
            var levels = new int[MergeBoardModel.SlotCount];
            levels[0] = first;
            levels[1] = second;
            return levels;
        }
    }
}
