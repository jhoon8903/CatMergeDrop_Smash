using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Framework.Data.Entity;
using Framework.Data.Local;
using Projects.Scripts.Data;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor
{
    [CustomEditor(typeof(PlayerSaveDefinition))]
    public sealed class PlayerSaveDefinitionEditor : UnityEditor.Editor
    {
        private PlayerSaveDefinition _definition;
        private LocalJsonSaveRepository _repository;
        private SaveRecord _loadedRecord;
        private PlayerSaveData _draft;
        private string _draftJson;
        private string _message;
        private MessageType _messageType = MessageType.Info;
        private bool _busy;
        private bool _dirty;
        private bool _jsonNeedsParsing;
        private bool _showJson;

        private string SavePath => Path.Combine(Application.persistentDataPath, "saves",
            _definition.SaveKey + ".json");

        private void OnEnable()
        {
            _definition = (PlayerSaveDefinition)target;
            _repository = new LocalJsonSaveRepository();
            Reload();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Local Player Save", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(SavePath, EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
            EditorGUILayout.HelpBox(
                "로컬 저장 파일을 편집합니다. Play Mode가 실행 중이라면 종료한 뒤 저장하고, 변경 사항은 게임을 다시 시작해야 반영됩니다.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_busy))
                    if (GUILayout.Button("파일 다시 읽기")) Reload();
                if (GUILayout.Button("파일 위치 열기")) EditorUtility.RevealInFinder(SavePath);
            }

            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _messageType);
            if (_busy) { EditorGUILayout.LabelField("파일 처리 중..."); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Play Mode를 종료한 뒤 저장하거나 초기화하세요.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(!File.Exists(SavePath) || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("로컬 데이터 초기화") && EditorUtility.DisplayDialog(
                        "로컬 데이터 초기화",
                        "스테이지, Gold, 보유 고양이, 인벤토리를 기본값으로 되돌립니다. 현재 파일은 .editor-backup으로 백업합니다.",
                        "초기화", "취소")) Reset();
            if (_draft == null) return;

            EditorGUILayout.LabelField(_loadedRecord == null ? "새 로컬 세이브" : "현재 로컬 세이브",
                EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_jsonNeedsParsing))
            {
                EditorGUI.BeginChangeCheck();
                int stage = EditorGUILayout.IntField("스테이지", _draft.StageIndex);
                int gold = EditorGUILayout.IntField("Gold", _draft.Gold);
                if (EditorGUI.EndChangeCheck())
                {
                    _draft.SetStageIndex(stage);
                    _draft.SetGold(gold);
                    _draftJson = JsonUtility.ToJson(_draft, true);
                    _dirty = true;
                }
            }
            EditorGUILayout.LabelField("보유 고양이", _draft.ActiveOwnedCatCount.ToString());
            EditorGUILayout.LabelField("최대 머지 레벨", _draft.MaxMergedCatLevel.ToString());

            _showJson = EditorGUILayout.Foldout(_showJson, "고급: 전체 저장 데이터 JSON 편집", true);
            if (_showJson)
            {
                EditorGUILayout.HelpBox("고양이 편성·보유 목록·인벤토리도 편집할 수 있습니다. JSON을 수정한 뒤 검증/반영을 누르세요.",
                    MessageType.Info);
                EditorGUI.BeginChangeCheck();
                string edited = EditorGUILayout.TextArea(_draftJson, GUILayout.MinHeight(180f));
                if (EditorGUI.EndChangeCheck())
                {
                    _draftJson = edited;
                    _jsonNeedsParsing = true;
                    _dirty = true;
                }
                if (_jsonNeedsParsing && GUILayout.Button("JSON 검증/반영")) ParseDraftJson();
            }

            using (new EditorGUI.DisabledScope(!_dirty || _busy || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button(_loadedRecord == null ? "로컬 세이브 생성" : "로컬 세이브 저장")) Save();
        }

        private bool ParseDraftJson()
        {
            try
            {
                PlayerSaveData parsed = JsonUtility.FromJson<PlayerSaveData>(_draftJson)
                    ?? throw new InvalidDataException("JSON에서 PlayerSaveData를 읽지 못했습니다.");
                parsed.Validate();
                _draft = parsed;
                _draftJson = JsonUtility.ToJson(parsed, true);
                _jsonNeedsParsing = false;
                _message = null;
                return true;
            }
            catch (Exception exception)
            {
                _message = "저장 데이터를 확인하세요: " + exception.Message;
                _messageType = MessageType.Error;
                return false;
            }
        }

        private async void Reload()
        {
            if (_busy) return;
            _busy = true;
            _message = null;
            Repaint();
            try
            {
                (SaveRecord record, PlayerSaveData data) = await PlayerLocalSaveEditorStorage.LoadAsync(
                    _repository, _definition.SaveKey);
                if (this == null) return;
                _loadedRecord = record;
                _draft = data;
                _draftJson = JsonUtility.ToJson(data, true);
                _jsonNeedsParsing = false;
                _dirty = record == null;
                if (record == null)
                {
                    _message = "로컬 파일이 없습니다. 기본값을 확인한 뒤 생성할 수 있습니다.";
                    _messageType = MessageType.Info;
                }
            }
            catch (Exception exception)
            {
                _loadedRecord = null;
                _draft = null;
                _dirty = false;
                _message = "로컬 세이브를 읽지 못했습니다: " + exception.Message;
                _messageType = MessageType.Error;
            }
            finally
            {
                _busy = false;
                if (this != null) Repaint();
            }
        }

        private async void Save()
        {
            if (_busy || EditorApplication.isPlayingOrWillChangePlaymode || !ParseDraftJson()) return;
            _busy = true;
            Repaint();
            try
            {
                bool hadExistingFile = _loadedRecord != null;
                SaveRecord saved = await PlayerLocalSaveEditorStorage.SaveAsync(
                    _repository, _definition.SaveKey, _loadedRecord, _draft, true);
                if (this == null) return;
                _loadedRecord = saved;
                _dirty = false;
                _message = hadExistingFile
                    ? "저장했습니다. 이전 파일은 같은 위치의 .editor-backup 파일에 보관했습니다."
                    : "로컬 세이브를 생성했습니다.";
                _messageType = MessageType.Info;
            }
            catch (Exception exception)
            {
                _message = "저장하지 못했습니다: " + exception.Message;
                _messageType = MessageType.Error;
            }
            finally
            {
                _busy = false;
                if (this != null) Repaint();
            }
        }

        private async void Reset()
        {
            if (_busy || EditorApplication.isPlayingOrWillChangePlaymode) return;
            _busy = true;
            Repaint();
            try
            {
                SaveRecord saved = await PlayerLocalSaveEditorStorage.ResetAsync(
                    _repository, _definition.SaveKey);
                if (this == null) return;
                _loadedRecord = saved;
                _draft = new PlayerSaveData();
                _draftJson = JsonUtility.ToJson(_draft, true);
                _jsonNeedsParsing = false;
                _dirty = false;
                _message = "로컬 데이터를 기본값으로 초기화했습니다. 이전 파일은 .editor-backup에 보관했습니다. 게임을 다시 시작하면 반영됩니다.";
                _messageType = MessageType.Info;
            }
            catch (Exception exception)
            {
                _message = "로컬 데이터를 초기화하지 못했습니다: " + exception.Message;
                _messageType = MessageType.Error;
            }
            finally
            {
                _busy = false;
                if (this != null) Repaint();
            }
        }
    }

    internal static class PlayerLocalSaveEditorStorage
    {
        public static async Task<(SaveRecord Record, PlayerSaveData Data)> LoadAsync(
            LocalJsonSaveRepository repository, string saveKey)
        {
            await repository.InitializeAsync(CancellationToken.None);
            SaveRecord record = await repository.LoadAsync(saveKey, CancellationToken.None);
            if (record == null) return (null, new PlayerSaveData());
            if (record.SchemaVersion != PlayerSaveData.CurrentSchemaVersion)
                throw new InvalidDataException($"지원하지 않는 세이브 버전: {record.SchemaVersion}");
            PlayerSaveData data = JsonUtility.FromJson<PlayerSaveData>(record.Payload)
                ?? throw new InvalidDataException("PlayerSaveData를 읽지 못했습니다.");
            data.Validate();
            return (record, data);
        }

        public static async Task<SaveRecord> SaveAsync(LocalJsonSaveRepository repository,
            string saveKey, SaveRecord expected, PlayerSaveData data, bool backupExisting)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            data.Validate();
            await repository.InitializeAsync(CancellationToken.None);
            SaveRecord current = await repository.LoadAsync(saveKey, CancellationToken.None);
            if (!Matches(current, expected))
                throw new InvalidOperationException("파일이 외부에서 변경되었습니다. 현재 파일을 다시 읽고 편집하세요.");
            if (backupExisting && current != null)
            {
                string path = Path.Combine(Application.persistentDataPath, "saves", saveKey + ".json");
                File.Copy(path, path + ".editor-backup", true);
            }
            SaveRecord record = new SaveRecord(PlayerSaveData.CurrentSchemaVersion,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), JsonUtility.ToJson(data));
            await repository.SaveAsync(saveKey, record, CancellationToken.None);
            return record;
        }

        public static async Task<SaveRecord> ResetAsync(LocalJsonSaveRepository repository,
            string saveKey)
        {
            await repository.InitializeAsync(CancellationToken.None);
            string path = Path.Combine(Application.persistentDataPath, "saves", saveKey + ".json");
            if (File.Exists(path)) File.Copy(path, path + ".editor-backup", true);
            var record = new SaveRecord(PlayerSaveData.CurrentSchemaVersion,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), JsonUtility.ToJson(new PlayerSaveData()));
            await repository.SaveAsync(saveKey, record, CancellationToken.None);
            return record;
        }

        private static bool Matches(SaveRecord left, SaveRecord right)
        {
            if (left == null || right == null) return left == null && right == null;
            return left.SchemaVersion == right.SchemaVersion
                && left.SavedAtUnixMilliseconds == right.SavedAtUnixMilliseconds
                && left.Payload == right.Payload;
        }
    }
}
