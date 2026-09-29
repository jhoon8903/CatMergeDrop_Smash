using UnityEngine;

namespace Projects.Scripts.GamePlay.Configs
{
    [CreateAssetMenu(fileName = "DropCameraConfig", menuName = "Cat Merge Drop/Drop Camera Config")]
    public sealed class DropCameraConfig : ScriptableObject
    {
        [Header("Drop Framing")]
        [SerializeField, Min(0.01f)] private float dropZoomMultiplier = 1.18f;
        [SerializeField, Min(0f)] private float minimumDropSize = 5.1f;
        [SerializeField, Min(1)] private int extendedDropStage = 10;
        [SerializeField, Min(0f)] private float extendedDropSizeAddition = .55f;

        [Header("Camera Motion")]
        [SerializeField, Min(0.01f)] private float positionSmoothTime = .18f;
        [SerializeField, Min(0.01f)] private float targetSwitchSmoothTime = .5f;
        [SerializeField, Min(0.01f)] private float targetSwitchDampingDuration = .8f;
        [SerializeField, Min(0f)] private float maximumCameraSpeed = 18f;
        [SerializeField, Min(0.01f)] private float sizeSmoothTime = .28f;

        [Header("Falling Cat")]
        [SerializeField, Min(0f)] private float fallingViewSize = 3.7f;
        [SerializeField] private float fallingLead = 1.15f;

        [Header("Merge Framing")]
        [SerializeField, Min(0f)] private float mergeFramingMargin = .35f;
        [SerializeField, Min(0f)] private float minimumMergeSize = 3.2f;

        public float DropZoomMultiplier => Mathf.Max(.01f, dropZoomMultiplier);
        public float MinimumDropSize => Mathf.Max(0f, minimumDropSize);
        public int ExtendedDropStage => Mathf.Max(1, extendedDropStage);
        public float ExtendedDropSizeAddition => Mathf.Max(0f, extendedDropSizeAddition);
        public float PositionSmoothTime => Mathf.Max(.01f, positionSmoothTime);
        public float TargetSwitchSmoothTime => Mathf.Max(PositionSmoothTime, targetSwitchSmoothTime);
        public float TargetSwitchDampingDuration => Mathf.Max(.01f, targetSwitchDampingDuration);
        public float MaximumCameraSpeed => Mathf.Max(0f, maximumCameraSpeed);
        public float SizeSmoothTime => Mathf.Max(.01f, sizeSmoothTime);
        public float FallingViewSize => Mathf.Max(0f, fallingViewSize);
        public float FallingLead => fallingLead;
        public float MergeFramingMargin => Mathf.Max(0f, mergeFramingMargin);
        public float MinimumMergeSize => Mathf.Max(0f, minimumMergeSize);

        private void OnValidate()
        {
            dropZoomMultiplier = Mathf.Max(.01f, dropZoomMultiplier);
            minimumDropSize = Mathf.Max(0f, minimumDropSize);
            extendedDropStage = Mathf.Max(1, extendedDropStage);
            extendedDropSizeAddition = Mathf.Max(0f, extendedDropSizeAddition);
            positionSmoothTime = Mathf.Max(.01f, positionSmoothTime);
            targetSwitchSmoothTime = Mathf.Max(positionSmoothTime, targetSwitchSmoothTime);
            targetSwitchDampingDuration = Mathf.Max(.01f, targetSwitchDampingDuration);
            maximumCameraSpeed = Mathf.Max(0f, maximumCameraSpeed);
            sizeSmoothTime = Mathf.Max(.01f, sizeSmoothTime);
            fallingViewSize = Mathf.Max(0f, fallingViewSize);
            mergeFramingMargin = Mathf.Max(0f, mergeFramingMargin);
            minimumMergeSize = Mathf.Max(0f, minimumMergeSize);
        }
    }
}
