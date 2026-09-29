using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Framework.Data.Entity;
using UnityEngine;

namespace Framework.Data.Local
{
    /// <summary>
    /// Prototype/offline repository that stores one JSON envelope per save key.
    /// </summary>
    public sealed class LocalJsonSaveRepository : ISaveRepository
    {
        private readonly string _saveDirectory;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);

        public LocalJsonSaveRepository()
        {
            _saveDirectory = Path.Combine(Application.persistentDataPath, "saves");
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_saveDirectory);
            return Task.CompletedTask;
        }

        public async Task<SaveRecord> LoadAsync(string saveKey, CancellationToken cancellationToken)
        {
            string path = GetPath(saveKey);
            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(path)) return null;

                LocalSaveEnvelope envelope = JsonUtility.FromJson<LocalSaveEnvelope>(await File.ReadAllTextAsync(path, cancellationToken));
                if (envelope == null || string.IsNullOrEmpty(envelope.payload)) throw new InvalidDataException($"Local save '{saveKey}' is empty or malformed.");
                return new SaveRecord(envelope.schemaVersion, envelope.savedAtUnixMilliseconds, envelope.payload);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task SaveAsync(string saveKey, SaveRecord record, CancellationToken cancellationToken)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            string path = GetPath(saveKey);
            string temporaryPath = path + ".tmp";

            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(_saveDirectory);
                string json = JsonUtility.ToJson(new LocalSaveEnvelope
                {
                    schemaVersion = record.SchemaVersion,
                    savedAtUnixMilliseconds = record.SavedAtUnixMilliseconds,
                    payload = record.Payload
                });
                await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                finally
                {
                    _operationLock.Release();
                }
            }
        }

        private string GetPath(string saveKey)
        {
            if (string.IsNullOrWhiteSpace(saveKey)) throw new ArgumentException("A save key is required.", nameof(saveKey));
            for (int i = 0; i < saveKey.Length; i++)
            {
                char character = saveKey[i];
                if (!(char.IsLetterOrDigit(character) || character == '_' || character == '-'))
                    throw new ArgumentException("Save keys may contain only letters, numbers, '_' and '-'.", nameof(saveKey));
            }

            return Path.Combine(_saveDirectory, saveKey + ".json");
        }

        [Serializable]
        private sealed class LocalSaveEnvelope
        {
            public int schemaVersion;
            public long savedAtUnixMilliseconds;
            public string payload;
        }
    }
}
