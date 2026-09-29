using System;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Framework.Addressable
{
    public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        private AsyncOperationHandle<T> _handle;
        private T _asset;
        private bool _disposed;

        internal AssetLease(AsyncOperationHandle<T> handle, T asset)
        {
            _handle = handle;
            _asset = asset;
        }

        public T Asset => _disposed ? throw new ObjectDisposedException(nameof(AssetLease<T>)) : _asset;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle.IsValid()) Addressables.Release(_handle);
            _handle = default;
            _asset = null;
        }
    }
}
