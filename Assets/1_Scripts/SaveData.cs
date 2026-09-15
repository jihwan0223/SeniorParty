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
        // MainMenuController.Domains와 같은 순서.
        public float[] domainScores = new float[5];
    }
}
