using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Framework.BootStrap;
using Framework.Data;
using Projects.Scripts.Data;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;
using VContainer;

namespace Projects.Scripts.GamePlay.Board
{
    /// <summary>Board input and save state for scene-authored world sprites.</summary>
    [DisallowMultipleComponent]
    public sealed class MergeBoardController : MonoBehaviour
    {
        [SerializeField] private MergeBoardCell[] cells = new MergeBoardCell[MergeBoardModel.SlotCount];
        [SerializeField] private DropStageController dropController;
        [SerializeField] private DropCameraController cameraRig;
        [SerializeField] private CatConfig catConfig;
        [SerializeField] private StageBalanceConfig stageBalanceConfig;
        [SerializeField] private SpriteRenderer dragGhost;
        [Header("Canvas UI only")]
        [SerializeField] private Text status;
        [SerializeField] private Button addButton;
        [SerializeField] private Button dropButton;
        [SerializeField] private Button sellButton;
        [SerializeField] private Button autoMergeButton;
        [SerializeField] private Button autoSummonButton;
        [SerializeField] private CatSaleConfig catSaleConfig;

        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private MergeBoardModel _board;
        private SaveSystem _saveSystem;
        private Camera _camera;
        private Text _addButtonLabel;
        private Text _autoSummonButtonLabel;
        private Button _openRewardsButton;
        private Text _openRewardsButtonLabel;
        private int _selectedSlot = -1;
        private int _pressedSlot = -1;
        private Vector2 _pressPosition;
        private bool _dragging;
        private bool _busy;
        private int _activeColumns = MergeBoardModel.Columns;
        private int _activeRows = MergeBoardModel.Rows;

        public event Action<BoardCatSnapshot[]> DropRequested;
        public bool IsBoardPreparationAvailable => _board != null && !_busy && !HasPendingBrickRewards &&
            dropController != null && dropController.CanRequestDrop;
        public int CurrentStageIndex => _saveSystem == null ? 1 : _saveSystem.GetSnapshot<PlayerSaveData>().StageIndex;
        public int[] CatSettlementPrices => catSaleConfig?.PricesByLevel;

        public int GetOwnedCatLevel(int catId) => _saveSystem.GetSnapshot<PlayerSaveData>().GetOwnedCatLevel(catId);

        public void RefreshFromSave()
        {
            if (_saveSystem == null || _board == null) return;
            _selectedSlot = -1;
            RebuildBoard(_saveSystem.GetSnapshot<PlayerSaveData>());
            Render(status.text);
        }

        /// <summary>Applies a stage's board capacity.</summary>
        public async void ConfigureLayout(StageLayout layout)
        {
            if (layout.BoardColumns < 1 || layout.BoardRows < 1 || layout.BoardSlotCount > MergeBoardModel.SlotCount)
                throw new ArgumentOutOfRangeException(nameof(layout));
            _activeColumns = layout.BoardColumns;
            _activeRows = layout.BoardRows;
            if (_board == null) return;
            _selectedSlot = -1;
            _busy = true;
            try
            {
                await _saveSystem.UpdateAsync<PlayerSaveData>(data =>
                {
                    data.FitFormationToSlotCount(layout.BoardSlotCount);
                }, _lifetime.Token);
                RebuildBoard(_saveSystem.GetSnapshot<PlayerSaveData>());
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception exception) { Debug.LogException(exception, this); }
            finally { _busy = false; if (this != null) Render(status.text); }
        }

        public async void SellSelectedCat()
        {
            if (_busy || _board == null || HasPendingBrickRewards || !dropController.CanRequestDrop) return;
            int catId = _selectedSlot >= 0 ? _board.GetCatId(_selectedSlot) : 0;
            if (catId == 0) { Render("판매할 고양이를 선택하세요."); return; }
            PlayerSaveData snapshot = _saveSystem.GetSnapshot<PlayerSaveData>();
            int level;
            try { level = snapshot.GetOwnedCatLevel(catId); }
            catch (ArgumentOutOfRangeException) { Render("선택한 고양이를 찾을 수 없습니다."); return; }
            int payout = CatSaleRules.PriceForLevel(level, catSaleConfig != null ? catSaleConfig.PricesByLevel : null);
            _busy = true;
            try
            {
                await _saveSystem.UpdateAsync<PlayerSaveData>(data =>
                {
                    data.SellOwnedCat(catId, payout);
                }, _lifetime.Token);
                _selectedSlot = -1;
                RebuildBoard(_saveSystem.GetSnapshot<PlayerSaveData>());
                dropController.RefreshSavedGold();
                Render($"Lv{level} 고양이를 {payout}G에 판매했습니다.");
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception exception) { Debug.LogException(exception, this); Render("판매를 저장하지 못했습니다."); }
            finally { _busy = false; if (this != null) Render(status.text); }
        }

        private async void Start()
        {
            if (!ValidateReferences())
            {
                Debug.LogError("GameScene board references are incomplete. Rebuild the scene assets.", this);
                enabled = false;
                return;
            }

            BootLifetimeScope boot = FindFirstObjectByType<BootLifetimeScope>();
            if (boot != null && boot.Container != null) _saveSystem = boot.Container.Resolve<SaveSystem>();
            if (_saveSystem is not { IsLoaded: true })
            {
                Debug.LogError("MergeBoard requires a loaded SaveSystem. Enter through BootScene.", this);
                enabled = false;
                return;
            }

            _camera = Camera.main;
            if (_camera == null)
            {
                Debug.LogError("World board requires the BootScene MainCamera.", this);
                enabled = false;
                return;
            }

            try
            {
                await _saveSystem.UpdateAsync<PlayerSaveData>(data =>
                {
                    data.RecoverInterruptedAttemptDrop();
                    DropRewardPersistence.EnsureAttemptStarted(data, stageBalanceConfig);
                }, _lifetime.Token);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                enabled = false;
                return;
            }
            PlayerSaveData save = _saveSystem.GetSnapshot<PlayerSaveData>();
            StageDefinition definition = stageBalanceConfig.GetStageDefinition(save.StageIndex);
            StageLayout startupLayout = definition != null && save.HasStageAttempt
                ? StageBalanceConfig.LayoutFor(definition, save.AttemptCatalogRevision)
                : stageBalanceConfig.LayoutForStage(save.StageIndex);
            _activeColumns = startupLayout.BoardColumns;
            _activeRows = startupLayout.BoardRows;
            // Legacy saves may contain cats from the former reserve. Migrate those cats
            // onto currently vacant board cells once; new gameplay never creates them.
            if (ActiveSlotCount < MergeBoardModel.SlotCount || save.GetUnformedCatIds().Length > 0)
            {
                try
                {
                    await _saveSystem.UpdateAsync<PlayerSaveData>(data =>
                    {
                        data.FitFormationToSlotCount(ActiveSlotCount);
                        data.FillFormationFromUnformed(ActiveSlotCount);
                    }, _lifetime.Token);
                    save = _saveSystem.GetSnapshot<PlayerSaveData>();
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    return;
                }
            }
            if (this == null) return;
            RebuildBoard(save);
            dragGhost.gameObject.SetActive(false);
            cameraRig.Initialize(_camera, WorldPlayfieldLayout.BoardCenterY, WorldPlayfieldLayout.BoardHalfWidth);
            dropController.Initialize(this, _saveSystem);
            _addButtonLabel = addButton.GetComponentInChildren<Text>();
            _autoSummonButtonLabel = autoSummonButton.GetComponentInChildren<Text>();
            addButton.onClick.AddListener(SummonCat);
            dropButton.onClick.AddListener(RequestDrop);
            if (sellButton != null) sellButton.onClick.AddListener(SellSelectedCat);
            autoMergeButton.onClick.AddListener(AutoMerge);
            autoSummonButton.onClick.AddListener(AutoSummon);
            CreateOpenRewardsButton();
            Render(dropController.IsAvailable
                ? "같은 레벨의 고양이를 겹치면 머지됩니다."
                : "드롭 구성을 불러오지 못했습니다.");
        }

        private bool ValidateReferences()
        {
            if (cells == null || cells.Length != MergeBoardModel.SlotCount || dropController == null ||
                cameraRig == null || catConfig == null || stageBalanceConfig == null || dragGhost == null || status == null || addButton == null || dropButton == null ||
                autoMergeButton == null || autoSummonButton == null)
                return false;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == null || !cells[i].IsConfigured || cells[i].Slot != i) return false;
            }
            return true;
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            if (addButton != null) addButton.onClick.RemoveListener(SummonCat);
            if (dropButton != null) dropButton.onClick.RemoveListener(RequestDrop);
            if (sellButton != null) sellButton.onClick.RemoveListener(SellSelectedCat);
            if (autoMergeButton != null) autoMergeButton.onClick.RemoveListener(AutoMerge);
            if (autoSummonButton != null) autoSummonButton.onClick.RemoveListener(AutoSummon);
            if (_openRewardsButton != null) _openRewardsButton.onClick.RemoveListener(OpenBrickRewards);
        }

        private void Update()
        {
            if (_board == null || !TryReadPointer(out Vector2 position, out bool down, out bool held, out bool up))
                return;

            if (down)
            {
                _pressedSlot = FindCell(position);
                _pressPosition = position;
                _dragging = false;
            }

            if (held && _pressedSlot >= 0 && CanDrag(_pressedSlot) &&
                (position - _pressPosition).sqrMagnitude > 20f * 20f)
            {
                if (!_dragging) BeginDrag(_pressedSlot);
                _dragging = true;
                MoveDrag(position);
            }

            if (!up) return;
            int target = FindCell(position);
            if (_dragging)
            {
                if (target >= 0 && _pressedSlot >= 0 && target != _pressedSlot)
                    OnCellDropped(_pressedSlot, target);
                EndDrag();
            }
            else if (target >= 0) OnCellClicked(target);
            _pressedSlot = -1;
            _dragging = false;
        }

        private static bool TryReadPointer(out Vector2 position, out bool down, out bool held, out bool up)
        {
            TouchControl touch = Touchscreen.current?.primaryTouch;
            if (touch != null && (touch.press.isPressed || touch.press.wasPressedThisFrame ||
                touch.press.wasReleasedThisFrame))
            {
                position = touch.position.ReadValue();
                down = touch.press.wasPressedThisFrame;
                held = touch.press.isPressed;
                up = touch.press.wasReleasedThisFrame;
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                position = mouse.position.ReadValue();
                down = mouse.leftButton.wasPressedThisFrame;
                held = mouse.leftButton.isPressed;
                up = mouse.leftButton.wasReleasedThisFrame;
                return down || held || up;
            }

            position = default;
            down = held = up = false;
            return false;
        }

        private int FindCell(Vector2 screenPosition)
        {
            Vector3 worldPoint = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -_camera.transform.position.z));
            Collider2D hit = Physics2D.OverlapPoint(worldPoint);
            return hit != null && hit.TryGetComponent(out MergeBoardCell cell) ? cell.Slot : -1;
        }

        public bool CanDrag(int slot) => !_busy && !HasPendingBrickRewards && dropController.CanRequestDrop &&
            _board != null && _board.GetLevel(slot) > 0;

        public void OnCellClicked(int slot)
        {
            if (_busy || _board == null || HasPendingBrickRewards || !dropController.CanRequestDrop || !IsActiveSlot(slot)) return;
            if (_selectedSlot < 0)
            {
                if (_board.GetLevel(slot) == 0) return;
                _selectedSlot = slot;
                Render($"{CatCombatLabel(_board.GetLevel(slot))} · 옮길 칸을 선택하세요.");
                return;
            }

            int source = _selectedSlot;
            _selectedSlot = -1;
            if (source == slot) Render("선택을 취소했습니다.");
            else CommitMoveAsync(source, slot);
        }

        public void OnCellDropped(int source, int target)
        {
            if (_busy || _board == null || HasPendingBrickRewards || source == target || !dropController.CanRequestDrop || !IsActiveSlot(source) || !IsActiveSlot(target)) return;
            _selectedSlot = -1;
            CommitMoveAsync(source, target);
        }

        private string CatCombatLabel(int level) =>
            $"Lv{level} 피해 {catConfig.ImpactDamageFor(level)} · 튕김 {catConfig.BounceCountFor(level)}회 · 클리어 생존 시 {CatSaleRules.PriceForLevel(level, CatSettlementPrices)}G";

        private void BeginDrag(int slot)
        {
            _selectedSlot = -1;
            dragGhost.sprite = cells[slot].CatSprite;
            dragGhost.gameObject.SetActive(dragGhost.sprite != null);
        }

        private void MoveDrag(Vector2 screenPosition)
        {
            if (!dragGhost.gameObject.activeSelf) return;
            Vector3 point = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -_camera.transform.position.z));
            dragGhost.transform.position = new Vector3(point.x, point.y, 0f);
        }

        private void EndDrag() => dragGhost.gameObject.SetActive(false);

        public BoardCatSnapshot[] GetDropSnapshot()
        {
            if (_board == null) return Array.Empty<BoardCatSnapshot>();
            List<BoardCatSnapshot> snapshots = new List<BoardCatSnapshot>();
            for (int slot = 0; slot < ActiveSlotCount; slot++)
            {
                int level = _board.GetLevel(slot);
                if (level > 0)
                    snapshots.Add(new BoardCatSnapshot(slot, slot % _activeColumns,
                        slot / _activeColumns, level, _board.GetCatId(slot)));
            }
            return snapshots.ToArray();
        }

        private async void CommitMoveAsync(int source, int target)
        {
            if (_busy || HasPendingBrickRewards || !dropController.CanRequestDrop) return;
            MergeBoardModel candidate = new MergeBoardModel(_board.CopyLevels(), _board.CopyCatIds());
            if (!candidate.TryMoveOrMerge(source, target, out MergeBoardOperation operation))
            {
                Render("이동할 수 없는 칸입니다.");
                return;
            }
            await PersistAsync(data =>
            {
                if (operation.Change == MergeBoardChange.Merge) data.MergeFormationCats(source, target);
                else data.MoveFormationCat(source, target);
            }, operation.Change == MergeBoardChange.Merge ? "머지 성공!" : "고양이를 옮겼습니다.");
        }

        private async void SummonCat()
        {
            if (_busy || HasPendingBrickRewards || !dropController.CanRequestDrop) return;
            int[] levels = _board.CopyLevels();
            int firstEmpty = Array.FindIndex(levels, 0, ActiveSlotCount, value => value == 0);
            if (firstEmpty < 0)
            {
                Render("보드가 가득 찼습니다.");
                return;
            }
            PlayerSaveData snapshot = _saveSystem.GetSnapshot<PlayerSaveData>();
            int price = stageBalanceConfig.SummonCostFor(snapshot.MaxMergedCatLevel);
            if (snapshot.Gold < price)
            {
                Render($"Gold가 부족합니다. 소환 비용: {price}G");
                return;
            }

            int summonedLevel = stageBalanceConfig.RollSummonLevel(snapshot.MaxMergedCatLevel);
            levels[firstEmpty] = summonedLevel;
            _busy = true;
            Render("고양이를 소환하는 중...");
            int paidPrice = price;
            try
            {
                await _saveSystem.UpdateAsync<PlayerSaveData>(data =>
                {
                    int actualCost = stageBalanceConfig.SummonCostFor(data.MaxMergedCatLevel);
                    if (data.Gold < actualCost) throw new InsufficientGoldException();
                    data.SetGold(data.Gold - actualCost);
                    data.AddOwnedCat(summonedLevel, firstEmpty);
                    paidPrice = actualCost;
                }, _lifetime.Token);
                RebuildBoard(_saveSystem.GetSnapshot<PlayerSaveData>());
                dropController.RefreshSavedGold();
                Render($"Lv{summonedLevel} 고양이 소환! {paidPrice}G 사용");
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                // Scene is unloading.
            }
            catch (InsufficientGoldException)
            {
                Render("Gold가 부족합니다.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Render("소환을 저장하지 못했습니다. 다시 시도해 주세요.");
            }
            finally
            {
                _busy = false;
                if (this != null) Render(status.text);
            }
        }

        private async void AutoMerge()
        {
            if (_busy || _board == null || HasPendingBrickRewards || !dropController.CanRequestDrop) return;
            _busy = true;
            Render("자동 머지 중...");
            try
            {
                int merged = 0;
                await _saveSystem.UpdateAsync<PlayerSaveData>(data =>
                    merged = BoardAutoActions.MergeAll(data, ActiveSlotCount), _lifetime.Token);
                _selectedSlot = -1;
                RebuildBoard(_saveSystem.GetSnapshot<PlayerSaveData>());
                Render(merged > 0 ? $"자동 머지 {merged}회 완료!" : "머지 가능한 고양이가 없습니다.");
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Render("자동 머지를 저장하지 못했습니다.");
            }
            finally { _busy = false; if (this != null) Render(status.text); }
        }

        private async void AutoSummon()
        {
            if (_busy || _board == null || HasPendingBrickRewards || !dropController.CanRequestDrop) return;
            _busy = true;
            Render("빈 칸에 고양이를 소환하는 중...");
            try
            {
                AutoSummonResult result = default;
                await _saveSystem.UpdateAsync<PlayerSaveData>(data =>
                    result = BoardAutoActions.SummonIntoEmpty(data, ActiveSlotCount,
                        stageBalanceConfig, stageBalanceConfig.RollSummonLevel), _lifetime.Token);
                _selectedSlot = -1;
                RebuildBoard(_saveSystem.GetSnapshot<PlayerSaveData>());
                dropController.RefreshSavedGold();
                Render(result.Count > 0
                    ? $"{result.Count}마리 자동 소환! {result.GoldSpent}G 사용"
                    : "소환할 빈 칸이나 Gold가 없습니다.");
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Render("자동 소환을 저장하지 못했습니다.");
            }
            finally { _busy = false; if (this != null) Render(status.text); }
        }

        private void CreateOpenRewardsButton()
        {
            _openRewardsButton = Instantiate(autoMergeButton, autoMergeButton.transform.parent);
            _openRewardsButton.name = "OpenBrickRewardsButton";
            _openRewardsButton.onClick.RemoveAllListeners();
            _openRewardsButton.onClick.AddListener(OpenBrickRewards);
            RectTransform rect = _openRewardsButton.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0, 290);
            rect.sizeDelta = new Vector2(920, 100);
            Image image = _openRewardsButton.GetComponent<Image>();
            if (image != null) image.color = new Color(.96f, .82f, .43f);
            _openRewardsButtonLabel = _openRewardsButton.GetComponentInChildren<Text>();
        }

        private async void OpenBrickRewards()
        {
            if (_busy || _board == null || !dropController.CanOpenRewards) return;
            _busy = true;
            Render("브릭 보상을 여는 중...");
            try
            {
                BrickRewardTickets.OpenResult opened = default;
                await _saveSystem.UpdateAsync<PlayerSaveData>(data =>
                {
                    opened = BrickRewardTickets.OpenAll(data, stageBalanceConfig, ActiveSlotCount);
                }, _lifetime.Token);
                _selectedSlot = -1;
                RebuildBoard(_saveSystem.GetSnapshot<PlayerSaveData>());
                dropController.RefreshSavedGold();
                int shownCount = opened.CatLevels == null ? 0 : Math.Min(opened.CatLevels.Length, 5);
                string[] shownLevels = new string[shownCount];
                for (int i = 0; i < shownCount; i++) shownLevels[i] = $"Lv{opened.CatLevels[i]}";
                string cats = shownCount == 0 ? "" :
                    $" · 고양이 {opened.ItemOpened}마리 ({string.Join(", ", shownLevels)}" +
                    (opened.ItemOpened > shownCount ? ", …)" : ")");
                int remainingCats = BrickRewardTickets.Count(_saveSystem.GetSnapshot<PlayerSaveData>(), BrickRewardTickets.ItemId);
                Render($"브릭 보상 개봉: +{opened.GoldGranted}G{cats}" +
                    (remainingCats > 0 ? $" · 보드가 가득 차 고양이 티켓 {remainingCats}장 남음" : ""));
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Render("브릭 보상을 저장하지 못했습니다.");
            }
            finally { _busy = false; if (this != null) Render(status.text); }
        }

        private async Task PersistAsync(Action<PlayerSaveData> persist, string successMessage)
        {
            _busy = true;
            Render("저장 중...");
            try
            {
                await _saveSystem.UpdateAsync<PlayerSaveData>(data =>
                {
                    persist(data);
                }, _lifetime.Token);
                RebuildBoard(_saveSystem.GetSnapshot<PlayerSaveData>());
                Render(successMessage);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                // Scene is unloading.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Render("저장에 실패했습니다. 다시 시도해 주세요.");
            }
            finally
            {
                _busy = false;
                if (this != null) Render(status.text);
            }
        }

        private void RequestDrop()
        {
            if (_busy) return;
            if (dropController.CanRestartAttempt)
            {
                dropController.RestartAttempt();
                return;
            }
            if (HasPendingBrickRewards)
            {
                Render("먼저 브릭 보상을 열어 주세요.");
                return;
            }
            if (!dropController.CanRequestDrop)
            {
                Render("드롭이 끝나고 보상을 저장한 뒤 다시 Drop할 수 있습니다.");
                return;
            }
            BoardCatSnapshot[] snapshots = GetDropSnapshot();
            if (snapshots.Length == 0)
            {
                Render("고양이를 추가한 뒤 Drop하세요.");
                return;
            }
            DropRequested?.Invoke(snapshots);
            Render($"{snapshots.Length}마리가 보드에서 뛰어내립니다!");
        }

        public void SetDropStatus(string message) => Render(message);

        public void RefreshControls()
        {
            bool showBoardControls = _board != null && !dropController.IsRunning;
            if (dropButton != null) dropButton.gameObject.SetActive(showBoardControls);
            if (addButton != null) addButton.gameObject.SetActive(showBoardControls);
            if (sellButton != null) sellButton.gameObject.SetActive(showBoardControls);
            if (autoMergeButton != null) autoMergeButton.gameObject.SetActive(showBoardControls);
            if (autoSummonButton != null) autoSummonButton.gameObject.SetActive(showBoardControls);
            if (_openRewardsButton != null)
            {
                PlayerSaveData rewardSave = _saveSystem.GetSnapshot<PlayerSaveData>();
                int goldOpenings = BrickRewardTickets.Count(rewardSave, BrickRewardTickets.GoldId);
                int itemOpenings = BrickRewardTickets.Count(rewardSave, BrickRewardTickets.ItemId);
                _openRewardsButton.gameObject.SetActive(showBoardControls && (goldOpenings > 0 || itemOpenings > 0));
                _openRewardsButton.interactable = !_busy && dropController.CanOpenRewards;
                if (_openRewardsButtonLabel != null)
                    _openRewardsButtonLabel.text = $"브릭 보상 열기  Gold {goldOpenings} · 고양이 {itemOpenings}" +
                        (itemOpenings > 0 && !HasVacantBoardSlot() ? " (보드 가득 참)" : "");
            }
            if (dropButton != null && _board != null)
            {
                dropButton.interactable = !_busy && (dropController.CanRestartAttempt ||
                    (!HasPendingBrickRewards && GetDropSnapshot().Length > 0 && dropController.CanRequestDrop));
                Text label = dropButton.GetComponentInChildren<Text>();
                if (label != null) label.text = dropController.CanRestartAttempt
                    ? "무료 재도전" : $"Drop · {dropController.DropsRemaining}회 남음";
            }
            if (addButton == null || _board == null) return;
            int[] levels = _board.CopyLevels();
            PlayerSaveData save = _saveSystem.GetSnapshot<PlayerSaveData>();
            int cost = stageBalanceConfig.SummonCostFor(save.MaxMergedCatLevel);
            stageBalanceConfig.GetSummonLevelRange(save.MaxMergedCatLevel,
                out int minLevel, out int maxLevel);
            if (_addButtonLabel != null) _addButtonLabel.text = $"고양이 소환 {cost}G\nLv{minLevel}~{maxLevel}";
            if (_autoSummonButtonLabel != null)
                _autoSummonButtonLabel.text = $"빈칸 자동 소환\n{cost}G/마리";
            bool canPrepare = !_busy && !HasPendingBrickRewards && dropController.CanRequestDrop;
            bool hasEmptySlot = Array.FindIndex(levels, 0, ActiveSlotCount, value => value == 0) >= 0;
            bool canSummon = canPrepare && hasEmptySlot && save.Gold >= cost;
            addButton.interactable = canSummon;
            if (autoSummonButton != null) autoSummonButton.interactable = canSummon;
            if (autoMergeButton != null)
                autoMergeButton.interactable = canPrepare &&
                    BoardAutoActions.HasMergePair(levels, ActiveSlotCount);
            if (sellButton != null) sellButton.interactable = canPrepare && _selectedSlot >= 0;
        }

        private void Render(string message)
        {
            if (status == null || _board == null) return;
            status.text = message;
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].gameObject.SetActive(IsActiveSlot(i));
                if (!IsActiveSlot(i)) continue;
                Transform cellTransform = cells[i].transform;
                Vector3 position = cellTransform.localPosition;
                cellTransform.localPosition = new Vector3(
                    WorldPlayfieldLayout.ColumnX(i % _activeColumns, _activeColumns),
                    WorldPlayfieldLayout.BoardRowY(i / _activeColumns, _activeRows), position.z);
                int level = _board.GetLevel(i);
                cells[i].Render(level, i == _selectedSlot, catConfig.SpriteFor(level), _activeColumns);
            }
            RefreshControls();
        }

        private int ActiveSlotCount => _activeColumns * _activeRows;
        private bool HasPendingBrickRewards
        {
            get
            {
                if (_saveSystem == null) return false;
                PlayerSaveData save = _saveSystem.GetSnapshot<PlayerSaveData>();
                return BrickRewardTickets.Count(save, BrickRewardTickets.GoldId) > 0;
            }
        }
        private bool HasVacantBoardSlot()
            => _board != null && Array.FindIndex(_board.CopyLevels(), 0, ActiveSlotCount, level => level == 0) >= 0;
        private bool IsActiveSlot(int slot) => slot >= 0 && slot < ActiveSlotCount;

        private void RebuildBoard(PlayerSaveData save)
        {
            int[] levels = new int[MergeBoardModel.SlotCount];
            int[] ids = new int[MergeBoardModel.SlotCount];
            for (int i = 0; i < levels.Length; i++)
            {
                ids[i] = save.GetFormationCatId(i);
                levels[i] = ids[i] == 0 ? 0 : save.GetOwnedCatLevel(ids[i]);
            }
            _board = new MergeBoardModel(levels, ids);
        }

    }

    internal sealed class InsufficientGoldException : Exception { }

    public readonly struct BoardCatSnapshot
    {
        public readonly int Slot;
        public readonly int Column;
        public readonly int Row;
        public readonly int Level;
        public readonly int CatId;

        public BoardCatSnapshot(int slot, int column, int row, int level, int catId = 0)
        {
            Slot = slot;
            Column = column;
            Row = row;
            Level = level;
            CatId = catId;
        }
    }
}
