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
        // 모양 이름(예: "동그라미"). 미리보기가 작아서 잘 안 보일 때 텍스트로도 알려주는 용도.
        public string shapeName;
    }
}
