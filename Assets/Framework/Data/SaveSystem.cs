using System;
using System.Threading;
using System.Threading.Tasks;
using Framework.Data.Entity;
using UnityEngine;
using VContainer;

namespace Framework.Data
{
    /// <summary>One active save model, shared by Local and Network repositories.</summary>
    public sealed class SaveSystem
    {
        private readonly ISaveRepository _repository;
        private readonly SaveDataDefinition _definition;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private ISaveData _current;

        public bool IsLoaded => _current != null;

        [Preserve]
        public SaveSystem(ISaveRepository repository, SaveDataDefinition definition)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.SaveKey)) throw new ArgumentException("Save definition requires a stable key.", nameof(definition));
            if (definition.DataType == null || !typeof(ISaveData).IsAssignableFrom(definition.DataType)) throw new ArgumentException("Save definition requires an ISaveData type.", nameof(definition));
        }

        /// <summary>Called by SaveSystemBootstrapStep before loading the first scene.</summary>
        public async Task LoadAsync(CancellationToken cancellationToken)
        {
            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                await _repository.InitializeAsync(cancellationToken);
                SaveRecord record = await _repository.LoadAsync(_definition.SaveKey, cancellationToken);
                ISaveData data;
                if (record == null) data = _definition.CreateDefault();
                else
                {
                    if (string.IsNullOrWhiteSpace(record.Payload)) throw new InvalidOperationException("Saved payload is empty.");
                    data = JsonUtility.FromJson(record.Payload, _definition.DataType) as ISaveData;
                    if (data == null || data.SchemaVersion != record.SchemaVersion) throw new InvalidOperationException("Saved data type or schema version does not match the definition.");
                }

                if (data == null || data.GetType() != _definition.DataType) throw new InvalidOperationException("Save definition produced the wrong data type.");
                data.Validate();
                if (record == null)
                {
                    SaveRecord initialRecord = new SaveRecord(data.SchemaVersion, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), JsonUtility.ToJson(data));
                    await _repository.SaveAsync(_definition.SaveKey, initialRecord, cancellationToken);
                }
                _current = data;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>Returns a detached copy so callers cannot change state without saving.</summary>
        public T GetSnapshot<T>() where T : class, ISaveData
        {
            if (_current == null) throw new InvalidOperationException("Save data has not been loaded.");
            return _current is not T data ? throw new InvalidOperationException($"Active save data is not {typeof(T).Name}.") : Clone(data);
        }

        /// <summary>Persists a validated change, then makes it current only after the write succeeds.</summary>
        public async Task UpdateAsync<T>(Action<T> change, CancellationToken cancellationToken = default)
            where T : class, ISaveData
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                if (_current is not T current) throw new InvalidOperationException($"Active save data is not {typeof(T).Name} or is not loaded.");
                T candidate = Clone(current);
                change(candidate);
                candidate.Validate();
                string payload = JsonUtility.ToJson(candidate);
                SaveRecord record = new SaveRecord(candidate.SchemaVersion, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), payload);
                await _repository.SaveAsync(_definition.SaveKey, record, cancellationToken);
                _current = candidate;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>Rewrites the current snapshot without changing its fields.</summary>
        public async Task SaveCurrentAsync(CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                if (_current == null) throw new InvalidOperationException("Save data has not been loaded.");
                _current.Validate();
                SaveRecord record = new SaveRecord(_current.SchemaVersion, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), JsonUtility.ToJson(_current));
                await _repository.SaveAsync(_definition.SaveKey, record, cancellationToken);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private static T Clone<T>(T source) where T : class, ISaveData
        {
            T clone = JsonUtility.FromJson<T>(JsonUtility.ToJson(source));
            if (clone == null) throw new InvalidOperationException("Save data could not be cloned.");
            clone.Validate();
            return clone;
        }
    }
}
