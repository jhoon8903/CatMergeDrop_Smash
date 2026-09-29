using Framework.Data;
using Framework.Data.Entity;
using Framework.Data.Local;
using Framework.Data.Network;
using Framework.Scene;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Framework.BootStrap
{
    /// <summary>
    /// BootScene의 Composition Root.
    /// Scene 전환에 필요한 구현체를 이곳에서만 연결하고, 실행 컴포넌트는 인터페이스만 의존한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BootLifetimeScope : LifetimeScope
    {
        [Header("Scene Services")]
        [SerializeField] private SceneCatalog sceneCatalog;
        [SerializeField] private SceneEntryResolver sceneEntryResolver;
        [SerializeField] private TransitionView transitionView;
        [SerializeField] private LoadingView loadingView;

        [Header("Boot Components")]
        [SerializeField] private Bootstrapper bootstrapper;
        [SerializeField] private SceneSwitcher sceneSwitcher;

        [Header("Save")]
        [SerializeField] private SaveSystemBootstrapStep saveSystemBootstrapStep;

        protected override void Configure(IContainerBuilder builder)
        {
            if (sceneCatalog == null) throw new System.InvalidOperationException("BootLifetimeScope에 SceneCatalog가 없습니다.");
            if (sceneEntryResolver == null) throw new System.InvalidOperationException("BootLifetimeScope에 SceneEntryResolver가 없습니다.");
            if (bootstrapper == null) throw new System.InvalidOperationException("BootLifetimeScope에 Bootstrapper가 없습니다.");
            if (sceneSwitcher == null) throw new System.InvalidOperationException("BootLifetimeScope에 SceneSwitcher가 없습니다.");
            if (saveSystemBootstrapStep == null) throw new System.InvalidOperationException("BootLifetimeScope에 SaveSystemBootstrapStep이 없습니다.");
            if (!bootstrapper.ContainsStep(saveSystemBootstrapStep)) throw new System.InvalidOperationException("SaveSystemBootstrapStep을 Bootstrapper의 steps에도 추가하세요.");
            if (saveSystemBootstrapStep.Definition == null) throw new System.InvalidOperationException("SaveSystemBootstrapStep에 SaveDataDefinition이 없습니다.");

            builder.RegisterInstance(sceneCatalog).As<ISceneCatalog>();
            builder.RegisterInstance(sceneEntryResolver);

            if (transitionView != null) builder.RegisterComponent(transitionView).As<ITransitionView>();
            else builder.Register<NullTransitionView>(Lifetime.Singleton).As<ITransitionView>();
            if (loadingView != null) builder.RegisterComponent(loadingView).As<ILoadingView>();
            else builder.Register<NullLoadingView>(Lifetime.Singleton).As<ILoadingView>();

            builder.RegisterComponent(sceneSwitcher).As<ISceneSwitcher>();
            builder.RegisterComponent(bootstrapper);

            builder.RegisterInstance(saveSystemBootstrapStep.Definition).As<SaveDataDefinition>();
            builder.Register<SaveSystem>(Lifetime.Singleton);

            SaveStorageMode saveStorageMode = saveSystemBootstrapStep.StorageMode;

            switch (saveStorageMode)
            {
                case SaveStorageMode.Local:
                    builder.Register<LocalJsonSaveRepository>(Lifetime.Singleton).As<ISaveRepository>();
                    break;
                case SaveStorageMode.Network:
                    if (saveSystemBootstrapStep.Definition is not IFirestoreSaveCodec codec)
                    {
                        throw new System.InvalidOperationException("Network save mode requires the game's SaveDataDefinition to implement IFirestoreSaveCodec.");
                    }
                    builder.RegisterInstance(new FirebaseSaveRepository(
                        saveSystemBootstrapStep.FirebaseProjectId,
                        saveSystemBootstrapStep.FirestoreDatabaseId,
                        saveSystemBootstrapStep.UsersCollection,
                        saveSystemBootstrapStep.SavesCollection,
                        codec)).As<ISaveRepository>();
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(saveStorageMode), saveStorageMode, "Unknown save mode.");
            }
        }
    }
}
