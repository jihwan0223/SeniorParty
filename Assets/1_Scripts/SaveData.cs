using System;

namespace SeniorParty
{
    // 저장 파일에 그대로 직렬화되는 데이터.
    [Serializable]
    public class SaveData
    {
        public float masterVolume = 100f;
        public float bgmVolume = 100f;
        public float sfxVolume = 100f;
        // MainMenuController.Domains와 같은 순서.
        public float[] domainScores = new float[5];

        // 큐브 깎기 자동 난이도 조절 상태. 인덱스 0 하, 1 중, 2 상.
        // 조각 수 오프셋(+면 더 많이 지움)과, 90점 이상 연속 라운드 수.
        public int[] cubeCarvePieceOffset = new int[3];
        public int[] cubeCarveHighStreak = new int[3];
    }
}
