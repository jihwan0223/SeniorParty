using System;

namespace SeniorParty
{
    // 저장 파일에 그대로 직렬화되는 데이터.
    [Serializable]
    public class SaveData
    {
        public float masterVolume = 80f;
        public float bgmVolume = 80f;
        public float sfxVolume = 80f;

        // 게임별 난이도: 상중하 단계가 아니라 1.00(가장 쉬움)~10.00(가장 어려움) 연속값으로,
        // 사용자의 실제 인지기능 수행에 맞춰 소수점 둘째자리까지 세밀하게 조정된다.
        // 시작값을 1이 아니라 3으로 잡아서, 처음부터 점수(정보 탭)가 0점이 아니라 약 22점에서 시작한다.
        public float cubeCarveDifficulty = 3f;
        public float itemSortDifficulty = 3f;
        public float categoryWordDifficulty = 3f;
        public float matrixDifficulty = 3f;
        public float targetSearchingDifficulty = 3f;
        public float numberTrailDifficulty = 3f;
        public float colorWordDifficulty = 3f;
        public float pictureWordDifficulty = 3f;
        public float rotateShapesDifficulty = 3f;
        public float rotateGridDifficulty = 3f;
    }
}
