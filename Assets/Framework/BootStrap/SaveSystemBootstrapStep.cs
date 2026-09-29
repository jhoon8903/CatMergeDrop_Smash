using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Framework.Data;
using Framework.Data.Entity;
using UnityEngine;

namespace Framework.BootStrap
{
    /// <summary>
    /// Selects the save backend and loads the game's save model during boot.
    /// </summary>
    public sealed class SaveSystemBootstrapStep : BootstrapStep
    {
        [SerializeField] private SaveStorageMode storageMode = SaveStorageMode.Local;
        [SerializeField] private SaveDataDefinition saveDataDefinition;

        [Header("Firebase Firestore (Network mode)")]
        [Tooltip("Firebase 콘솔의 Project ID. 플랫폼 설정 파일의 프로젝트와 일치해야 합니다.")]
        [SerializeField] private string firebaseProjectId;
        [Tooltip("기본 Firestore 데이터베이스는 (default)를 사용합니다.")]
        [SerializeField] private string firestoreDatabaseId = "(default)";
        [SerializeField] private string usersCollection = "users";
        [SerializeField] private string savesCollection = "saves";

        public SaveStorageMode StorageMode => storageMode;
        public SaveDataDefinition Definition => saveDataDefinition;
        public string FirebaseProjectId => firebaseProjectId;
        public string FirestoreDatabaseId => firestoreDatabaseId;
        public string UsersCollection => usersCollection;
        public string SavesCollection => savesCollection;

        public override UniTask InitializeAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("SaveSystemBootstrapStep must be initialized through BootstrapContext.");

        public override async UniTask InitializeAsync(BootstrapContext context, CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            await context.Resolve<SaveSystem>().LoadAsync(cancellationToken);
        }

        public override void Shutdown()
        {
            // Firebase and local repositories are owned by the application container.
        }
    }
}
