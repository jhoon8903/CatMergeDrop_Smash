using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Framework.UI.Components
{
    /// <summary>입력을 받지 않는 하위 Graphic의 raycastTarget을 끈다.</summary>
    [DisallowMultipleComponent]
    public sealed class RaycastTargetCleaner : MonoBehaviour
    {
        [SerializeField] private bool includeInactive = true;
        [SerializeField] private bool preserveRootGraphic = true;
        [SerializeField] private bool applyOnEnable;

        private void OnEnable()
        {
            if (applyOnEnable) Apply();
        }

        [ContextMenu("Disable Unused Raycast Targets")]
        public void Apply()
        {
            Graphic[] graphics = GetComponentsInChildren<Graphic>(includeInactive);
            foreach (Graphic graphic in graphics)
            {
                if (preserveRootGraphic && graphic.transform == transform) continue;
                graphic.raycastTarget = ReceivesPointer(graphic.gameObject);
            }
        }

        private static bool ReceivesPointer(GameObject target)
        {
            if (target.GetComponent<Selectable>() != null
                || target.GetComponent<EventTrigger>() != null 
                || target.GetComponent<ActionButton>() != null
                || target.GetComponent<CanvasGroup>()) return true;
            foreach (MonoBehaviour component in target.GetComponents<MonoBehaviour>())
            {
                if (component is IPointerClickHandler or IPointerDownHandler or IPointerUpHandler or IBeginDragHandler or IDragHandler)
                    return true;
            }
            return false;
        }
    }
}
