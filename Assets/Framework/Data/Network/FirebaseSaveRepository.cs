using System;
using System.Threading;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using Framework.Data.Entity;

namespace Framework.Data.Network
{
    /// <summary>Shared Firebase anonymous-authentication and Cloud Firestore adapter.</summary>
    public sealed class FirebaseSaveRepository : ISaveRepository
    {
        private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);
        private readonly string _expectedProjectId;
        private readonly string _databaseId;
        private readonly string _usersCollection;
        private readonly string _savesCollection;
        private readonly IFirestoreSaveCodec _codec;
        private FirebaseAuth _auth;
        private FirebaseFirestore _firestore;
        private CollectionReference _saveCollection;

        public FirebaseSaveRepository(string expectedProjectId, string databaseId, string usersCollection, string savesCollection, IFirestoreSaveCodec codec)
        {
            if (string.IsNullOrWhiteSpace(expectedProjectId)) throw new ArgumentException("Network mode requires the Firebase Project ID in SaveSystemBootstrapStep.", nameof(expectedProjectId));
            ValidateCollectionName(usersCollection, nameof(usersCollection));
            ValidateCollectionName(savesCollection, nameof(savesCollection));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _expectedProjectId = expectedProjectId.Trim();
            _databaseId = string.IsNullOrWhiteSpace(databaseId) ? "(default)" : databaseId.Trim();
            _usersCollection = usersCollection.Trim();
            _savesCollection = savesCollection.Trim();
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
            => EnsureReadyAsync(cancellationToken);

        public async Task<SaveRecord> LoadAsync(string saveKey, CancellationToken cancellationToken)
        {
            ValidateSaveKey(saveKey);
            await EnsureReadyAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            DocumentSnapshot snapshot = await _saveCollection.Document(saveKey).GetSnapshotAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return !snapshot.Exists 
                ? null 
                : _codec.Decode(snapshot.ToDictionary());
        }

        public async Task SaveAsync(string saveKey, SaveRecord record, CancellationToken cancellationToken)
        {
            ValidateSaveKey(saveKey);
            if (record == null) throw new ArgumentNullException(nameof(record));
            await EnsureReadyAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await _saveCollection.Document(saveKey).SetAsync(_codec.Encode(record));
        }

        private async Task EnsureReadyAsync(CancellationToken cancellationToken)
        {
            if (_saveCollection != null) return;
            await _initializationLock.WaitAsync(cancellationToken);
            try
            {
                if (_saveCollection != null) return;
                DependencyStatus dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
                if (dependencyStatus != DependencyStatus.Available)
                    throw new InvalidOperationException($"Firebase dependencies are unavailable: {dependencyStatus}.");

                string configuredProjectId = FirebaseApp.DefaultInstance.Options.ProjectId;
                if (!string.Equals(configuredProjectId, _expectedProjectId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Firebase project mismatch: SaveSystemBootstrapStep expects '{_expectedProjectId}', " +
                        $"but the platform Firebase configuration uses '{configuredProjectId ?? "<missing>"}'.");
                _auth = FirebaseAuth.DefaultInstance;
                FirebaseUser user = _auth.CurrentUser ?? (await _auth.SignInAnonymouslyAsync()).User;
                if (user == null || string.IsNullOrWhiteSpace(user.UserId)) throw new InvalidOperationException("Firebase Authentication did not return a user ID.");
                cancellationToken.ThrowIfCancellationRequested();
                _firestore = _databaseId == "(default)"
                    ? FirebaseFirestore.DefaultInstance
                    : FirebaseFirestore.GetInstance(_databaseId);
                _saveCollection = _firestore.Collection(_usersCollection).Document(user.UserId).Collection(_savesCollection);
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        private static void ValidateSaveKey(string saveKey)
        {
            if (string.IsNullOrWhiteSpace(saveKey)) throw new ArgumentException("A save key is required.", nameof(saveKey));
            if (saveKey.Contains("/")) throw new ArgumentException("Save keys cannot contain '/'.", nameof(saveKey));
        }

        private static void ValidateCollectionName(string name, string argumentName)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains("/"))
                throw new ArgumentException("Firestore collection names must be nonempty path segments.", argumentName);
        }
    }
}
