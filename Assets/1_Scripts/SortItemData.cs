using UnityEngine;

namespace SeniorParty
{
    // 정답 기준: Keep = 통과/보관 Discard = 버리기
    public enum ItemAction
    {
        Keep = 0,
        Discard = 1,
    }

    // 물건 분류 게임
    [CreateAssetMenu(menuName = "SeniorParty/Sort Item", fileName = "NewSortItem")]
    public class SortItemData : ScriptableObject
    {
        public Sprite sprite;
        public ItemAction correctAction;
    }
}
