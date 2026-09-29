using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;

namespace Framework.Addressable
{
    public interface IAssetLoader
    {
        UniTask<AssetLease<T>> LoadAsync<T>(AssetReferenceT<T> reference, CancellationToken token = default) where T : UnityEngine.Object;
    }
}
