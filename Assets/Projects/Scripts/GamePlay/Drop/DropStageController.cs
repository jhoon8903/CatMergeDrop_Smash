using System;
using System.Collections.Generic;
using System.Threading;
using Framework.Data;
using Framework.Poolable.Components;
using Projects.Scripts.Data;
using Projects.Scripts.GamePlay.Board;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Projects.Scripts.GamePlay.Drop.Ecs.Systems;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace Projects.Scripts.GamePlay
{
    /// <summary>Connects ECS drop state to scene-authored world sprites and UI text.</summary>
    [DisallowMultipleComponent]
    public sealed class DropStageController : MonoBehaviour
    {
        [SerializeField] private DropBrickView[] bricks = new DropBrickView[WorldPlayfieldLayout.BrickCount];
        [SerializeField] private DropCatView[] cats = new DropCatView[MergeBoardModel.SlotCount];
        [SerializeField] private Text hud;
        [SerializeField] private DropCameraController cameraRig;
        [SerializeField] private CatConfig catConfig;
        [SerializeField] private BrickConfig brickConfig;
        [SerializeField] private StageBalanceConfig stageBalanceConfig;

        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private MergeBoardController _board;
        private SaveSystem _saveSystem;
        private World _world;
        private Entity _stage = Entity.Null;
        private EntityQuery _brickQuery;
        private EntityQuery _runtimeQuery;
        private EntityQuery _poolQuery;
        private EntityQuery _catQuery;
        private EntityQuery _impactQuery;
        private int _savedGold;
        private int _dropsRemaining;
        private int _dropBudget;
        private int _currentStageIndex;
        private StageLayout _layout;
        private int _lastCommittedRunId;
        private Phase _lastPhase = Phase.Ready;
        private bool _savingRewards;
        private bool _dropPending;
        private bool _dropRecoveryPending;
        private bool _invalidBakedLayout;
        private float _nextRewardRetryTime;
        private readonly bool[] _brickVisible = new bool[WorldPlayfieldLayout.BrickCount];
        private readonly bool[] _catVisible = new bool[MergeBoardModel.SlotCount];
        private readonly HashSet<Sprite> _prebakedBrickSprites = new HashSet<Sprite>();
        private Transform _goalLine;

        public bool IsAvailable => _world is { IsCreated: true } 
                                   && _stage != Entity.Null 
                                   && _world.EntityManager.Exists(_stage);

        public bool IsRunning => _dropPending 
                                 || (IsAvailable && _world.EntityManager.GetComponentData<Stage>(_stage).Phase == Phase.Falling);

        public int DropsRemaining => _dropsRemaining;

        private void ReadAttemptState(PlayerSaveData save)
        {
            _savedGold = save.Gold;
            _dropsRemaining = save.AttemptDropsRemaining;
            _dropBudget = save.AttemptDropBudget;
        }
        public bool CanOpenRewards => IsAvailable && !_savingRewards && !_dropPending &&
            _world.EntityManager.GetComponentData<Stage>(_stage).Phase != Phase.Falling &&
            _lastCommittedRunId == _world.EntityManager.GetComponentData<Stage>(_stage).RunId &&
            (DropsRemaining > 0 || stageBalanceConfig.IsBeyondFinalStage(_currentStageIndex));
        public bool CanRestartAttempt => IsAvailable && !_savingRewards && !_dropPending &&
            _world.EntityManager.GetComponentData<Stage>(_stage).Phase != Phase.Falling &&
            _lastCommittedRunId == _world.EntityManager.GetComponentData<Stage>(_stage).RunId &&
            DropRewardPersistence.CanRestartAttempt(_saveSystem.GetSnapshot<PlayerSaveData>(), stageBalanceConfig);

        public bool CanRequestDrop
        {
            get
            {
                if (!IsAvailable || _savingRewards || _dropPending || DropsRemaining <= 0) return false;
                Stage stage = _world.EntityManager.GetComponentData<Stage>(_stage);
                return stage.Phase != Phase.Falling && _lastCommittedRunId == stage.RunId;
            }
        }

        public void RefreshSavedGold()
        {
            if (_saveSystem == null) return;
            ReadAttemptState(_saveSystem.GetSnapshot<PlayerSaveData>());
            Render();
        }

        public void Initialize(MergeBoardController board, SaveSystem saveSystem)
        {
            _board = board;
            _saveSystem = saveSystem;
            PlayerSaveData save = saveSystem.GetSnapshot<PlayerSaveData>();
            ReadAttemptState(save);
            _currentStageIndex = save.StageIndex;
            _goalLine = transform.Find("GoalLine");
            if (!ValidateReferences())
            {
                Debug.LogError("Drop scene references are incomplete. Rebuild the GameScene assets.", this);
                return;
            }
            _layout = stageBalanceConfig.LayoutForStage(_currentStageIndex);
            for (int i = 0; i < cats.Length; i++) cats[i].Configure(catConfig);
            cameraRig.SetStageIndex(_currentStageIndex);
            cameraRig.SetStageLayout(_layout);

            _world = World.DefaultGameObjectInjectionWorld;
            if (_world == null || !_world.IsCreated)
            {
                Debug.LogError("Drop requires the default ECS world.", this);
                return;
            }

            EntityManager manager = _world.EntityManager;
            _runtimeQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<DropRuntimeBaked>());
            _poolQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<EntityPool>());
            _brickQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>());
            _catQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<Cat>());
            _impactQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<BrickImpactEvent>());
            _board.DropRequested += HandleDropRequested;
            TryCreateStage();
        }

        private void TryCreateStage()
        {
            if (IsAvailable || _invalidBakedLayout || _world is not { IsCreated: true }
                || _runtimeQuery.IsEmptyIgnoreFilter || _poolQuery.IsEmptyIgnoreFilter) return;
            EntityManager manager = _world.EntityManager;
            if (_runtimeQuery.CalculateEntityCount() != 1) return;
            DropRuntimeBaked baked = _runtimeQuery.GetSingleton<DropRuntimeBaked>();
            if (baked.BrickCapacity != bricks.Length || baked.CatCapacity != cats.Length)
            {
                Debug.LogError("Drop ECS SubScene capacity does not match the GameScene views.", this);
                _invalidBakedLayout = true;
                return;
            }
            if (_poolQuery.GetSingleton<EntityPool>().PoolCount < 2) return;
            PlayerSaveData snapshot = _saveSystem.GetSnapshot<PlayerSaveData>();
            int stageIndex = stageBalanceConfig.IsBeyondFinalStage(_currentStageIndex)
                ? stageBalanceConfig.LastDefinedStage : _currentStageIndex;
            int? revision = snapshot.HasStageAttempt ? snapshot.AttemptCatalogRevision : null;
            StageDefinition definition = stageBalanceConfig.GetStageDefinition(stageIndex);
            if (definition != null) _layout = StageBalanceConfig.LayoutFor(definition, revision);
            cameraRig.SetStageLayout(_layout);
            _stage = DropStageFactory.Create(manager, brickConfig, stageBalanceConfig, stageIndex, null, revision);
            int[] health = snapshot.GetAttemptBrickHealth();
            if (health.Length > 0) DropStageFactory.ApplyBrickHealth(manager, _stage, health);
            ApplyStageLayout();
            _board.RefreshControls();
            Render();
        }

        private void RenderImpacts()
        {
            if (!IsAvailable) return;
            EntityManager manager = _world.EntityManager;
            Stage stage = manager.GetComponentData<Stage>(_stage);
            NativeArray<Entity> impacts = _impactQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < impacts.Length; i++)
                {
                    BrickImpactEvent impact = manager.GetComponentData<BrickImpactEvent>(impacts[i]);
                    if (impact.Stage != _stage) continue;
                    cameraRig.NotifyBrickImpact(WorldPlayfieldLayout.DropY(
                        GateCollisionSystem.RowY(impact.Row)));
                    if (impact.RemainingHealth <= 0)
                        cameraRig.FocusBrokenTile(WorldPlayfieldLayout.DropY(
                            GateCollisionSystem.RowY(impact.Row)));
                    int index = impact.Row * stage.BrickColumns + impact.Column;
                    if (index >= 0 && index < bricks.Length)
                        bricks[index].PlayImpact(impact.RemainingHealth, impact.MaxHealth,
                            brickConfig, impact.Gold, impact.Kind);
                    manager.DestroyEntity(impacts[i]);
                }
            }
            finally
            {
                impacts.Dispose();
            }
        }

        private bool ValidateReferences()
        {
            if (hud == null 
                || cameraRig == null 
                || catConfig == null
                || brickConfig == null
                || stageBalanceConfig == null
                || _goalLine == null
                || bricks is not { Length: WorldPlayfieldLayout.BrickCount }
                || cats is not { Length: MergeBoardModel.SlotCount }) return false;
            
            for (int i = 0; i < bricks.Length; i++)
            {
                if (bricks[i] == null 
                    || !bricks[i].IsConfigured 
                    || bricks[i].Row != i / MergeBoardModel.Columns 
                    || bricks[i].Column != i % MergeBoardModel.Columns) return false;
            }
   
            for (int i = 0; i < cats.Length; i++)
            {
                if (cats[i] == null 
                    || !cats[i].IsConfigured 
                    || cats[i].Slot != i) return false;
            }
            return true;
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            if (_board != null) _board.DropRequested -= HandleDropRequested;
            if (IsAvailable) DropStageFactory.Destroy(_world.EntityManager, _stage);
        }

        private async void HandleDropRequested(BoardCatSnapshot[] snapshots)
        {
            if (!CanRequestDrop) return;
            _dropPending = true;
            _board.RefreshControls();
            try
            {
                await _saveSystem.UpdateAsync<PlayerSaveData>(data => data.ReserveAttemptDrop(), _lifetime.Token);
                ReadAttemptState(_saveSystem.GetSnapshot<PlayerSaveData>());
                if (this != null) cameraRig.BeginDrop(() => StartDrop(snapshots));
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _dropPending = false;
                _board.SetDropStatus("Drop을 저장하지 못했습니다. 다시 눌러 주세요.");
                _board.RefreshControls();
            }
        }

        private void StartDrop(BoardCatSnapshot[] snapshots)
        {
            if (!_dropPending || !IsAvailable) return;
            try
            {
                if (DropStageFactory.RequestDrop(_world.EntityManager, _stage, catConfig, snapshots)) return;
            }
            catch (Exception exception) { Debug.LogException(exception, this); }
            _dropRecoveryPending = true;
            RecoverFailedDropAsync();
        }

        private async void RecoverFailedDropAsync()
        {
            if (_savingRewards) return;
            _savingRewards = true;
            try
            {
                await _saveSystem.UpdateAsync<PlayerSaveData>(data => data.RecoverInterruptedAttemptDrop(), _lifetime.Token);
                ReadAttemptState(_saveSystem.GetSnapshot<PlayerSaveData>());
                if (!IsAvailable) return;
                DropStageFactory.RestartStage(_world.EntityManager, _stage);
                int[] health = _saveSystem.GetSnapshot<PlayerSaveData>().GetAttemptBrickHealth();
                if (health.Length > 0) DropStageFactory.ApplyBrickHealth(_world.EntityManager, _stage, health);
                _lastCommittedRunId = _world.EntityManager.GetComponentData<Stage>(_stage).RunId;
                _lastPhase = Phase.Ready;
                _dropRecoveryPending = false;
                _dropPending = false;
                cameraRig.ReturnToMerge();
                _board.SetDropStatus("Drop을 시작하지 못했습니다. 횟수와 브릭을 복원했으니 다시 눌러 주세요.");
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _nextRewardRetryTime = Time.unscaledTime + 3f;
                _board.SetDropStatus("Drop 상태를 복원하는 중입니다. 잠시 후 다시 시도합니다.");
            }
            finally { _savingRewards = false; if (_board != null) _board.RefreshControls(); }
        }

        private void Update()
        {
            if (!IsAvailable) TryCreateStage();
            if (!IsAvailable) return;
            if (_dropRecoveryPending)
            {
                if (!_savingRewards && Time.unscaledTime >= _nextRewardRetryTime) RecoverFailedDropAsync();
                return;
            }
            Stage stage = _world.EntityManager.GetComponentData<Stage>(_stage);
            if (stage.Phase != _lastPhase)
            {
                _lastPhase = stage.Phase;
                _dropPending = false;
                switch (stage.Phase)
                {
                    case Phase.Falling:
                        _board.SetDropStatus("앞의 블록이 깨지면 고양이가 아래로 계속 내려갑니다.");
                        break;
                    case Phase.Won:
                        cameraRig.ReturnToMerge();
                        ReturnCatsToBoard();
                        _board.SetDropStatus("스테이지 클리어! 살아남은 고양이를 골드로 정산합니다.");
                        break;
                    case Phase.Lost:
                        cameraRig.ReturnToMerge();
                        ReturnCatsToBoard();
                        _board.SetDropStatus("파괴한 길은 유지됩니다. 남은 블록을 보고 다음 Drop을 준비하세요.");
                        break;
                }
                _board.RefreshControls();
            }

            if (stage.Phase is Phase.Won or Phase.Lost
                && stage.RunId > _lastCommittedRunId
                && !_savingRewards
                && Time.unscaledTime >= _nextRewardRetryTime)
            {
                SaveRewardsAsync(stage.RunId, stage.GoldEarned, stage.GoldTicketsEarned, stage.ItemsEarned,
                    stage.Phase == Phase.Won);
            }
            Render();
            RenderImpacts();
        }

        private async void SaveRewardsAsync(int runId, int earnedGold, int goldTickets, int earnedItems,
            bool stageCleared)
        {
            _savingRewards = true;
            _board.RefreshControls();
            try
            {
                int[] health = DropStageFactory.CaptureBrickHealth(_world.EntityManager, _stage);
                int[] survivors = stageCleared
                    ? DropStageFactory.CaptureSurvivingCatSlots(_world.EntityManager, _stage) : null;
                int survivorGold = stageCleared ? DropRewardPersistence.SurvivorGoldFor(
                    _saveSystem.GetSnapshot<PlayerSaveData>(), survivors, _board.CatSettlementPrices) : 0;
                await DropRewardPersistence.SaveAttemptDropAsync(_saveSystem, stageBalanceConfig,
                    _currentStageIndex, stageCleared, earnedGold, goldTickets, earnedItems,
                    health, _lifetime.Token, survivors, _board.CatSettlementPrices);
                PlayerSaveData save = _saveSystem.GetSnapshot<PlayerSaveData>();
                ReadAttemptState(save);
                if (stageCleared && save.StageIndex != _currentStageIndex && IsAvailable &&
                    !stageBalanceConfig.IsBeyondFinalStage(save.StageIndex))
                {
                    DropStageFactory.ConfigureBricks(_world.EntityManager, _stage,
                        brickConfig, stageBalanceConfig, save.StageIndex);
                    _layout = stageBalanceConfig.LayoutForStage(save.StageIndex);
                    ApplyStageLayout();
                    _board.ConfigureLayout(_layout);
                }
                else if (stageCleared) _board.RefreshFromSave();
                _currentStageIndex = save.StageIndex;
                cameraRig.SetStageIndex(_currentStageIndex);
                cameraRig.SetStageLayout(_layout);
                _lastCommittedRunId = runId;
                _nextRewardRetryTime = 0f;
                _board.RefreshFromSave();
                if (stageBalanceConfig.IsBeyondFinalStage(save.StageIndex))
                    _board.SetDropStatus($"모든 스테이지 클리어! 생존 고양이 정산 +{survivorGold}G");
                else if (stageCleared)
                    _board.SetDropStatus($"클리어! 생존 고양이 {survivors.Length}마리 정산 +{survivorGold}G · 다음 스테이지에서 새로 소환하세요.");
                else if (!stageCleared)
                    _board.SetDropStatus(save.AttemptDropsRemaining == 0
                        ? "Drop을 모두 사용했습니다. 무료 재도전하면 이번 도전의 소환·머지·보상과 브릭이 시작 상태로 돌아갑니다."
                        : $"길과 피해가 유지됩니다. 남은 Drop {save.AttemptDropsRemaining}회 · 뽑기와 머지로 다음 길을 준비하세요.");
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                // Scene is unloading.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _nextRewardRetryTime = Time.unscaledTime + 3f;
                _board.SetDropStatus("보상을 저장하지 못했습니다. 잠시 후 다시 시도합니다.");
            }
            finally
            {
                _savingRewards = false;
                if (_board != null) _board.RefreshControls();
            }
        }

        public async void RestartAttempt()
        {
            if (!CanRestartAttempt) return;
            _savingRewards = true;
            _board.RefreshControls();
            try
            {
                await _saveSystem.UpdateAsync<PlayerSaveData>(data => data.RestartStageAttempt(
                    stageBalanceConfig.GetStageDefinition(data.StageIndex)?.CatalogRevision ?? 0), _lifetime.Token);
                if (!IsAvailable) return;
                DropStageFactory.ConfigureBricks(_world.EntityManager, _stage, brickConfig,
                    stageBalanceConfig, _currentStageIndex);
                _layout = stageBalanceConfig.LayoutForStage(_currentStageIndex);
                cameraRig.SetStageLayout(_layout);
                _board.ConfigureLayout(_layout);
                ApplyStageLayout();
                _lastPhase = Phase.Ready;
                ReadAttemptState(_saveSystem.GetSnapshot<PlayerSaveData>());
                _board.RefreshFromSave();
                _board.SetDropStatus($"시작 상태로 돌아왔습니다. Drop {DropsRemaining}회로 다른 길을 시도해 보세요.");
                Render();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _board.SetDropStatus("재도전을 저장하지 못했습니다. 다시 눌러 주세요.");
            }
            finally { _savingRewards = false; if (_board != null) _board.RefreshControls(); }
        }

        private void Render()
        {
            if (!IsAvailable) return;
            EntityManager manager = _world.EntityManager;
            Stage stage = manager.GetComponentData<Stage>(_stage);
            long displayedGold = _savedGold;
            if (stage.RunId > _lastCommittedRunId) displayedGold += stage.GoldEarned;
            hud.text = stageBalanceConfig.IsBeyondFinalStage(_currentStageIndex) ? "모든 스테이지 클리어!" : $"STAGE {_currentStageIndex} • GOLD {displayedGold} • DROP {DropsRemaining}/{_dropBudget}";
            Array.Clear(_brickVisible, 0, _brickVisible.Length);
            NativeArray<Entity> brickEntities = _brickQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < brickEntities.Length; i++)
                {
                    Brick brick = manager.GetComponentData<Brick>(brickEntities[i]);
                    if (brick.Stage != _stage || brick.Health <= 0) continue;
                    int index = brick.Row * stage.BrickColumns + brick.Column;
                    if (index < 0 || index >= bricks.Length) continue;
                    _brickVisible[index] = true;
                    Sprite sprite = brickConfig.SpriteFor(brick.Level, brick.Kind);
                    if (sprite != null && _prebakedBrickSprites.Add(sprite))
                        brickConfig.DebrisSpriteFor(sprite, 0);
                    bricks[index].SetLevelSprite(sprite);
                    bricks[index].Render(brick);
                }
            }
            finally
            {
                brickEntities.Dispose(); 
                
            }
            for (int i = 0; i < bricks.Length; i++)
            {
                if (!_brickVisible[i])
                {
                    bricks[i].Hide();
                }
            }

            if (stage.Phase != Phase.Falling)
            {
                ReturnCatsToBoard();
                return;
            }

            Array.Clear(_catVisible, 0, _catVisible.Length);
            NativeArray<Entity> catEntities = _catQuery.ToEntityArray(Allocator.Temp);
            float leadingCatY = float.PositiveInfinity;
            int leadingCatSlot = -1;
            try
            {
                for (int i = 0; i < catEntities.Length; i++)
                {
                    Cat cat = manager.GetComponentData<Cat>(catEntities[i]);
                    if (cat.Stage != _stage 
                        || cat.Slot < 0 
                        || cat.Slot >= cats.Length) continue;
                    _catVisible[cat.Slot] = true;
                    CatSpritePose select = PoseFor(cat);
                    Sprite sprite = catConfig.SpriteFor(cat.Level, select);
                    cats[cat.Slot].SetLevelSprite(sprite);
                    cats[cat.Slot].Render(cat);
                    float worldY = WorldPlayfieldLayout.DropY(cat.Y);
                    if (worldY < leadingCatY - .001f ||
                        Mathf.Abs(worldY - leadingCatY) <= .001f &&
                        cat.Slot == cameraRig.TrackedCatSlot)
                    {
                        leadingCatY = worldY;
                        leadingCatSlot = cat.Slot;
                    }
                }
            }
            finally
            {
                catEntities.Dispose();
            }
            if (leadingCatSlot >= 0) cameraRig.TrackFallingCat(leadingCatSlot, leadingCatY);
            for (int i = 0; i < cats.Length; i++)
            {
                if (!_catVisible[i]) cats[i].Hide();
            }
        }

        private void ReturnCatsToBoard()
        {
            for (int i = 0; i < cats.Length; i++) cats[i].ReturnToBoard();
        }

        private void ApplyStageLayout()
        {
            if (_goalLine != null && IsAvailable)
            {
                Stage state = _world.EntityManager.GetComponentData<Stage>(_stage);
                Vector3 position = _goalLine.localPosition;
                position.y = WorldPlayfieldLayout.DropY(state.GoalProgress);
                _goalLine.localPosition = position;
                Transform line = _goalLine.Find("Line");
                if (line != null)
                {
                    Vector3 scale = line.localScale;
                    scale.x = _layout.BoardColumns * WorldPlayfieldLayout.BoardColumnPitch - .1f;
                    line.localScale = scale;
                }
            }
            if (bricks != null)
            {
                for (int i = 0; i < _layout.BrickSlotCount && i < bricks.Length; i++)
                    if (bricks[i] != null)
                        bricks[i].SetStagePosition(i / _layout.BrickColumns,
                            i % _layout.BrickColumns, _layout.BrickColumns);
            }

        }

        public static CatSpritePose PoseFor(in Cat cat) =>
            cat.IsBouncing && cat.Velocity < 0f
                ? CatSpritePose.Jump
                : CatSpritePose.Drop;

    }
}
