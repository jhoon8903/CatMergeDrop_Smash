namespace Framework.UI.Navigation
{
    public sealed class UILayerRootRegistry
    {
        private readonly UILayerRoot[] _roots;

        public UILayerRootRegistry(UILayerRoot screenRoot, UILayerRoot popupRoot, UILayerRoot overlayRoot)
            => _roots = new[] { screenRoot, popupRoot, overlayRoot };

        public bool TryGetHandle(int viewCode, out UIHandle handle)
        {
            for (int i = 0; i < _roots.Length; i++)
            {
                UILayerRoot candidate = _roots[i];
                if (candidate == null || !candidate.TryGetHandle(viewCode, out handle)) continue;
                return true;
            }

            handle = null;
            return false;
        }

        public UILayerRoot Get(UIViewLayer layer)
        {
            for (int i = 0; i < _roots.Length; i++)
            {
                UILayerRoot root = _roots[i];
                if (root != null && root.Layer == layer) return root;
            }

            return null;
        }
    }
}
