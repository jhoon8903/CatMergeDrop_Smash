using UnityEngine;

namespace Projects.Scripts.GamePlay.Configs
{
    [CreateAssetMenu(fileName = "CatSaleConfig", menuName = "Cat Merge Drop/Cat Sale Config")]
    public sealed class CatSaleConfig : ScriptableObject
    {
        [SerializeField, Min(0)] private int[] pricesByLevel = { 2, 3, 4, 5, 6, 7, 8, 9 };
        public int[] PricesByLevel => pricesByLevel;
    }
}
