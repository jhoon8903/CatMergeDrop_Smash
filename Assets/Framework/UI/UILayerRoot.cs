using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Framework.UI.Components;
using Framework.UI.Interfaces;
using Framework.UI.LifeTimeScope;
using Framework.UI.Navigation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Framework.UI
{
    /// <summary>한 Canvas 레이어에 속한 씬 상주 View의 등록과 표시 정책을 소유한다.</summary>
    [DisallowMultipleComponent]
    public sealed class UILayerRoot : LifetimeScope
    {
        [SerializeField] private UIViewLayer layer;
        private readonly Dictionary<int, UIHandle> _handles = new();
        private readonly Dictionary<int, IUIViewPresenter> _presenters = new();
        private readonly List<UIHandle> _openHandles = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private UIView[] _views = Array.Empty<UIView>();
        private bool _initialized;

        public UIViewLayer Layer => layer;

        /// <summary>
        /// 레이어 하위의 View와 설치 컴포넌트를 자동 수집한다.
        /// Presenter 자체는 순수 C# 객체일 수 있으므로, 등록은 IUILayerInstaller가 담당한다.
        /// </summary>
        protected override void Configure(IContainerBuilder builder)
        {
            _views = GetComponentsInChildren<UIView>(true);
            for (int i = 0; i < _views.Length; i++)
            {
                UIView view = _views[i];
                builder.RegisterComponent(view).As(view.GetType());
            }

            IUILayerInstaller[] installers = GetComponentsInChildren<IUILayerInstaller>(true);
            for (int i = 0; i < installers.Length; i++)
            {
                installers[i].Install(builder);
            }

            builder.RegisterBuildCallback(resolver =>
            {
                foreach (IUIViewPresenter presenter in resolver.Resolve<IEnumerable<IUIViewPresenter>>())
                {
                    _presenters[presenter.ViewCode] = presenter;
                }
            });
        }

        public bool TryGetHandle(int routeCode, out UIHandle handle)
        {
            Initialize();
            return _handles.TryGetValue(routeCode, out handle);
        }

        internal async UniTask<UIHandle> OpenAsync(UIHandle handle, IUIViewArgs args, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (handle == null || handle.Owner != this) throw new InvalidOperationException($"{layer} Root에 속하지 않은 View Handle입니다.");
                if (handle.IsOpen) await CloseInternalAsync(handle, cancellationToken);
                if (layer == UIViewLayer.Screen) await CloseAllInternalAsync(cancellationToken);
                if (layer == UIViewLayer.Overlay) await CloseAllInternalAsync(cancellationToken);

                handle.BeginPresentation();
                try
                {
                    await handle.Presenter.PresentAsync(args, handle.PresentationToken);
                    await handle.View.SetVisibleAsync(true, false, cancellationToken);
                    _openHandles.Add(handle);
                    return handle;
                }
                catch
                {
                    await CloseInternalAsync(handle, CancellationToken.None);
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async UniTask<bool> CloseAsync(UIHandle handle, CancellationToken cancellationToken)
        {
            if (handle == null || handle.Owner != this) return false;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!handle.IsOpen) return false;
                await CloseInternalAsync(handle, cancellationToken);
                return true;
            }
            finally { _gate.Release(); }
        }

        public async UniTask<bool> CloseTopAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_openHandles.Count == 0) return false;
                await CloseInternalAsync(_openHandles[^1], cancellationToken);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            for (int i = 0; i < _views.Length; i++)
            {
                UIView view = _views[i];
                if (view == null) continue;
                if (!_presenters.TryGetValue(view.ViewCode, out IUIViewPresenter presenter))
                {
                    Debug.LogError($"[UILayerRoot] {view.name}의 Presenter가 등록되지 않았습니다.", view);
                    continue;
                }
                if (!_handles.TryAdd(view.ViewCode, new UIHandle(view.ViewCode, view, presenter, this))) Debug.LogError($"[UILayerRoot] 중복 ViewCode: {view.ViewCode}", view);
                view.Initialize(Layer);
            }
        }

        private async UniTask CloseAllInternalAsync(CancellationToken cancellationToken)
        {
            for (int i = _openHandles.Count - 1; i >= 0; i--)
            {
                await CloseInternalAsync(_openHandles[i], cancellationToken);
            }
        }

        private async UniTask CloseInternalAsync(UIHandle handle, CancellationToken cancellationToken)
        {
            _openHandles.Remove(handle);
            try
            {
                await handle.View.SetVisibleAsync(false, false, cancellationToken);
                await handle.Presenter.DismissAsync(CancellationToken.None);
            }
            finally
            {
                handle.EndPresentation();
            }
        }
    }
}
