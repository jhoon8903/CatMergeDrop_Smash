using Framework.UI.Animation;
using UnityEditor;
using UnityEngine;

namespace Framework.Editor.UI
{
    /// <summary>Project 창의 우클릭 Framework/UI 메뉴에서 UI 설정 에셋을 생성한다.</summary>
    internal static class UIAssetCreationMenu
    {
        [MenuItem("Assets/Framework/UI/Animation Profile", false, 10)]
        private static void CreateAnimationProfile()
        {
            ProjectWindowUtil.CreateAsset(
                ScriptableObject.CreateInstance<UIAnimationProfile>(),
                "UIAnimationProfile.asset");
        }
    }
}
