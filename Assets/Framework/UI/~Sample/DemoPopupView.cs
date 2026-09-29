using System;
using Framework.UI.Components;
using UnityEngine;
using UnityEngine.UI;

namespace Framework.UI._Sample
{
    public sealed class DemoPopupView : UIView
    {
        [SerializeField] private Button closeButton;
        public event Action CloseRequested;

        private void Awake()
        {
            if (closeButton == null)
            {
                
            }
        }

        protected override void OnInitialized()
        {
            if (closeButton != null) closeButton.onClick.AddListener(RequestClose);
        }

        protected override void OnShown(bool immediate)
        {
            
        }

        protected override void OnHidden(bool immediate)
        {
            
        }

        private void OnDestroy()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(RequestClose);
        }

        private void RequestClose() => CloseRequested?.Invoke();
    }
}
