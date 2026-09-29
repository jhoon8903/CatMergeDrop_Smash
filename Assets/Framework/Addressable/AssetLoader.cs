using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Framework.Addressable
{
    public sealed class AssetLoader : IAssetLoader
    {
        public async UniTask<AssetLease<T>> LoadAsync<T>(AssetReferenceT<T> r, CancellationToken t = default) where T : UnityEngine.Object
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (!r.RuntimeKeyIsValid()) throw new InvalidOperationException($"{typeof(T).Name} 어드레서블 런타임 키 미할당");
            t.ThrowIfCancellationRequested();
            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(r.RuntimeKey);
            try
            {
                await UniTask.WaitUntil(() => handle.IsDone, cancellationToken: t);
                t.ThrowIfCancellationRequested();
                if (handle.Status != AsyncOperationStatus.Succeeded) throw handle.OperationException ?? new InvalidOperationException($"에셋 로드 실패: {typeof(T).Name} Key: {r.RuntimeKey}");
                T asset = handle.Result;
                return asset == null ? throw new InvalidOperationException($"Asset: {typeof(T).Name}, Key: {r.RuntimeKey}는 존재하지 않습니다.") : new AssetLease<T>(handle, asset);
            }
            catch (Exception e)
            {
                if (handle.IsValid()) Addressables.Release(handle);
                throw;
            }
        }
    }
}
