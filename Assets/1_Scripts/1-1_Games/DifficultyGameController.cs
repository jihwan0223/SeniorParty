using UnityEngine;

namespace SeniorParty
{
    // 난이도 있는 게임(ItemSort, CubeCarve, CategoryWord, Matrix) 공통: 1.00(쉬움)~10.00(어려움)
    // 연속값을 세이브에서 불러와 유지하고, 성공/실패에 따라 소수점 둘째자리까지 조정한다.
    // 실제로 이 값에서 어떤 파라미터를 뽑아 쓸지, 언제/얼마나 조정할지는 게임마다 다르므로 자식이 정한다.
    public abstract class DifficultyGameController : MonoBehaviour
    {
        protected const float MinDifficulty = 1f;
        protected const float MaxDifficulty = 10f;

        protected float difficulty;

        // 0(가장 쉬움)~1(가장 어려움)로 정규화한 난이도.
        protected float DifficultyT => (difficulty - MinDifficulty) / (MaxDifficulty - MinDifficulty);

        // 세이브에서 불러온 값을 범위 안으로 자른다.
        protected void LoadDifficulty(float saved)
        {
            difficulty = Mathf.Clamp(saved, MinDifficulty, MaxDifficulty);
        }

        // delta만큼 움직이고 범위를 벗어나지 않게, 소수점 둘째자리로 반올림.
        protected void AdjustDifficulty(float delta)
        {
            difficulty = Mathf.Clamp(difficulty + delta, MinDifficulty, MaxDifficulty);
            difficulty = Mathf.Round(difficulty * 100f) / 100f;
        }
    }
}
