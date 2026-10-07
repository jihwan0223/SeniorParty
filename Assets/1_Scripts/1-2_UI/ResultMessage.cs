namespace SeniorParty
{
    // 결과창에 점수(0~100) 숫자 대신 보여줄 칭찬/격려 문구. 점수가 낮아도 긍정적인 말로 안내한다.
    public static class ResultMessage
    {
        // 점수 구간별 칭찬 문구를 고른다.
        public static string FromScore(int score)
        {
            if (score >= 90)
            {
                return "최고예요!";
            }
            if (score >= 70)
            {
                return "잘했어요!";
            }
            if (score >= 50)
            {
                return "좋아요!";
            }
            return "수고했어요!";
        }
    }
}
