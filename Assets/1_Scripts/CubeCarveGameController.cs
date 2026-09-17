using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using SeniorParty.UI;

namespace SeniorParty
{
    // 큐브 깎기 게임: 뚫린 모양을 몇 초 보여주고, 사라진 뒤 새 큐브를 눌러서 같은 모양으로 깎는다.
    // 라운드마다 완성 버튼으로 제출, 틀려도 맞게 지운 만큼 부분 점수. 끝나면 결과창(다시하기/나가기).
    [RequireComponent(typeof(UIDocument))]
    public class CubeCarveGameController : MonoBehaviour
    {
        [SerializeField] private int roundCount = 2;

        private const string MemorizeText = "이 모양대로 큐브를 깎으세요!";
        private const string CarveText = "기억한 모양대로 눌러서 깎으세요";
        // 보여주던 큐브가 사라지고 새 큐브가 나오기까지 비워두는 시간.
        private const int SwapDelayMs = 500;
        private const int MaxWaitFrames = 60;

        // 난이도별 시작 설정(인덱스 0 하, 1 중, 2 상). 조각 수는 자동 조절 오프셋만큼 위아래로 움직인다.
        // maxLayer: 0 겉면만, 1 한 겹 안쪽까지, 2 제한 없음.
        private static readonly (int min, int max, int maxLayer, CubeView.Spread spread, float seconds)[] Levels =
        {
            (2, 3, 0, CubeView.Spread.Clustered, 10f),
            (3, 5, 1, CubeView.Spread.Mixed, 8f),
            (5, 7, 2, CubeView.Spread.Scattered, 6f),
        };

        // 자동 난이도 조절: 연속 2라운드 90점 이상이면 조각 +1, 50점 미만이면 바로 -1.
        // 한 판이 짧아서 판이 끝나도 이어지게 세이브에 저장한다.
        private const float RaiseScore = 0.9f;
        private const float LowerScore = 0.5f;
        private const int RaiseStreak = 2;
        private const int MinPieces = 1;
        private const int MaxPieces = 10;

        private CubeView cubeView;
        private Label instructionLabel;
        private Label timerLabel;
        private Label remainingLabel;
        private VisualElement carveButtons;
        private VisualElement resultPanel;
        private Label finalScoreLabel;
        private Label finalTimeLabel;

        private SaveData saveData;
        private int levelIndex;
        private bool[] targetRemoved;
        private int currentRound;
        // 라운드별 점수(0~1)의 합.
        private float scoreSum;
        private float startTime;
        private float timeRemaining;
        private IVisualElementScheduledItem timerSchedule;
        // 깎는 중일 때만 true. 외우는 중이거나 이미 제출한 뒤 완성 버튼이 눌리는 것 방지.
        private bool carving;

        private void OnEnable()
        {
            StartCoroutine(InitializeWhenUiReady());
        }

        // 게임 1과 같은 이유로, 런타임 씬 전환 직후엔 UI가 붙을 때까지 기다린다.
        private IEnumerator InitializeWhenUiReady()
        {
            var document = GetComponent<UIDocument>();
            VisualElement root = null;
            for (int i = 0; i < MaxWaitFrames; i++)
            {
                root = document.rootVisualElement;
                if (root != null && root.childCount > 0)
                {
                    break;
                }
                yield return null;
            }

            if (root == null || root.childCount == 0)
            {
                Debug.LogWarning("CubeCarveGameController: UI가 끝내 준비되지 않아 초기화를 건너뜀");
                yield break;
            }

            cubeView = root.Q<CubeView>("cube-view");
            instructionLabel = root.Q<Label>("instruction-label");
            timerLabel = root.Q<Label>("timer-label");
            remainingLabel = root.Q<Label>("remaining-label");
            carveButtons = root.Q<VisualElement>("carve-buttons");
            resultPanel = root.Q<VisualElement>("result-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            finalTimeLabel = root.Q<Label>("final-time-label");

            root.Q<Button>("done-button").clicked += OnDone;
            root.Q<Button>("reset-button").clicked += OnReset;
            root.Q<Button>("retry-button").clicked += OnRetry;
            root.Q<Button>("exit-button").clicked += OnExit;

            resultPanel.AddToClassList("hidden");
            saveData = SaveManager.Load();
            // 예전 세이브엔 이 값이 없거나 길이가 다를 수 있다.
            if (saveData.cubeCarvePieceOffset == null || saveData.cubeCarvePieceOffset.Length != Levels.Length)
            {
                saveData.cubeCarvePieceOffset = new int[Levels.Length];
            }
            if (saveData.cubeCarveHighStreak == null || saveData.cubeCarveHighStreak.Length != Levels.Length)
            {
                saveData.cubeCarveHighStreak = new int[Levels.Length];
            }
            levelIndex = MainMenuController.LastDifficulty switch
            {
                "중" => 1,
                "상" => 2,
                _ => 0,
            };
            currentRound = 0;
            scoreSum = 0f;
            startTime = Time.time;
            StartRound();
        }

        // 외우기 단계: 뚫린 큐브를 보여주고 타이머 시작.
        private void StartRound()
        {
            if (currentRound >= roundCount)
            {
                ShowResult();
                return;
            }

            var level = Levels[levelIndex];
            int offset = saveData.cubeCarvePieceOffset[levelIndex];
            int min = Mathf.Clamp(level.min + offset, MinPieces, MaxPieces);
            int max = Mathf.Clamp(level.max + offset, min, MaxPieces);
            targetRemoved = CubeView.PickRemoved(Random.Range(min, max + 1), level.maxLayer, level.spread);

            remainingLabel.text = $"{roundCount - currentRound} / {roundCount}";
            instructionLabel.text = MemorizeText;
            cubeView.Interactive = false;
            cubeView.SetRemoved(targetRemoved);
            cubeView.visible = true;
            timerLabel.visible = true;
            carveButtons.visible = false;
            carving = false;

            timeRemaining = level.seconds;
            UpdateTimerLabel();
            timerSchedule?.Pause();
            timerSchedule = cubeView.schedule.Execute(TickTimer).Every(100);
        }

        private void TickTimer()
        {
            timeRemaining -= 0.1f;
            UpdateTimerLabel();
            if (timeRemaining > 0f)
            {
                return;
            }

            // 보여준 큐브를 잠깐 없앴다가 안 뚫린 새 큐브로 바꿔서 깎기 시작.
            timerSchedule.Pause();
            cubeView.visible = false;
            timerLabel.visible = false;
            cubeView.schedule.Execute(StartCarving).StartingIn(SwapDelayMs);
        }

        private void UpdateTimerLabel()
        {
            timerLabel.text = Mathf.Max(0, Mathf.CeilToInt(timeRemaining)).ToString();
        }

        // 깎기 단계: 온전한 큐브에서 조각을 눌러 지우고, 완성 버튼으로 제출.
        private void StartCarving()
        {
            instructionLabel.text = CarveText;
            cubeView.ClearRemoved();
            cubeView.Interactive = true;
            cubeView.visible = true;
            carveButtons.visible = true;
            carving = true;
        }

        // 초기화: 지운 조각을 전부 되살려서 처음부터 다시 깎기.
        private void OnReset()
        {
            if (carving)
            {
                cubeView.ClearRemoved();
            }
        }

        private void OnDone()
        {
            if (!carving)
            {
                return;
            }

            carving = false;
            cubeView.Interactive = false;

            float roundScore = RoundScore(cubeView.GetRemoved(), targetRemoved);
            Debug.Log(roundScore >= 1f ? "맞음" : $"틀림 (부분 점수 {Mathf.RoundToInt(roundScore * 100f)}점)");
            scoreSum += roundScore;
            AdjustDifficulty(roundScore);

            currentRound++;
            StartRound();
        }

        private void AdjustDifficulty(float roundScore)
        {
            int[] offsets = saveData.cubeCarvePieceOffset;
            int[] streaks = saveData.cubeCarveHighStreak;
            var level = Levels[levelIndex];

            if (roundScore >= RaiseScore)
            {
                streaks[levelIndex]++;
                if (streaks[levelIndex] >= RaiseStreak)
                {
                    streaks[levelIndex] = 0;
                    offsets[levelIndex]++;
                }
            }
            else
            {
                streaks[levelIndex] = 0;
                if (roundScore < LowerScore)
                {
                    offsets[levelIndex]--;
                }
            }

            // 조각 수가 MinPieces~MaxPieces를 벗어나는 만큼은 오프셋이 쌓이지 않게 자른다.
            offsets[levelIndex] = Mathf.Clamp(offsets[levelIndex], MinPieces - level.min, MaxPieces - level.max);
            SaveManager.Save(saveData);
            Debug.Log($"자동 난이도: 조각 수 {level.min + offsets[levelIndex]}~{level.max + offsets[levelIndex]}개");
        }

        // 부분 점수 = 맞게 지운 조각 수 / (정답 조각 수 + 잘못 지운 조각 수).
        // 정확히 같으면 1, 아무것도 안 지웠으면 0, 엉뚱한 조각까지 지우면 그만큼 깎인다.
        private static float RoundScore(bool[] carved, bool[] target)
        {
            int matched = 0;
            int union = 0;
            for (int i = 0; i < target.Length; i++)
            {
                if (carved[i] && target[i])
                {
                    matched++;
                }
                if (carved[i] || target[i])
                {
                    union++;
                }
            }
            return union == 0 ? 1f : (float)matched / union;
        }

        private void ShowResult()
        {
            int score = Mathf.RoundToInt(scoreSum / Mathf.Max(1, roundCount) * 100f);
            finalScoreLabel.text = $"최종 점수: {score}";
            finalTimeLabel.text = $"총 걸린 시간: {FormatElapsed(Time.time - startTime)}";
            resultPanel.RemoveFromClassList("hidden");
        }

        private static string FormatElapsed(float seconds)
        {
            int totalSeconds = Mathf.RoundToInt(seconds);
            int minutes = totalSeconds / 60;
            int remainSeconds = totalSeconds % 60;
            return minutes > 0 ? $"{minutes}분 {remainSeconds}초" : $"{remainSeconds}초";
        }

        // 다시하기: 난이도 선택 화면으로 이동
        private void OnRetry()
        {
            MainMenuController.PendingReturnTarget = MainMenuController.ReturnTarget.GameTabsWithDifficulty;
            SceneManager.LoadScene("MainScene");
        }

        // 나가기: 게임 시작 누르면 나오는 게임 탭 화면으로 이동
        private void OnExit()
        {
            MainMenuController.PendingReturnTarget = MainMenuController.ReturnTarget.GameTabs;
            SceneManager.LoadScene("MainScene");
        }
    }
}
