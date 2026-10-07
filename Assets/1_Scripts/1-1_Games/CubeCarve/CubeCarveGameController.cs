using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 큐브 깎기 게임: 뚫린 모양을 몇 초 보여주고, 사라진 뒤 새 큐브를 눌러서 같은 모양으로 깎는다.
    // 라운드마다 완성 버튼으로 제출, 틀려도 맞게 지운 만큼 부분 점수. 끝나면 결과창(다시하기/나가기).
    [RequireComponent(typeof(UIDocument))]
    public class CubeCarveGameController : DifficultyGameController
    {
        [SerializeField] private int roundCount = 2;
        [SerializeField] private Cube3DView cube3D;

        private const string MemorizeText = "이 모양대로 주사위를 깎으세요!";
        private const string CarveText = "기억한 모양대로 눌러서 깎으세요";
        // 보여주던 큐브가 사라지고 새 큐브가 나오기까지 비워두는 시간.
        private const int SwapDelayMs = 500;
        // 제출 후 격려 문구를 보여주는 시간(게임 4 표적 찾기와 같음).
        private const int FeedbackMs = 1200;
        private const int MaxWaitFrames = 60;

        // 자동 난이도: 여기서 조각 개수 범위, 접근 방식(maxLayer), 흩어짐(spread), 노출시간을
        // 매 라운드 새로 뽑아 쓴다. maxLayer: 0 겉면만, 1 한 겹 안쪽까지, 2 제한 없음.
        // 시니어 대상 앱이라 한 판(라운드 2번) 만에 확 어려워지면 안 됨 — 아주 조금씩만 움직인다.
        // 완벽하게 수행한다고 가정하면(매 라운드 만점) 약 45세션 만에 난이도 1->10.
        private const float RaiseScore = 0.9f;
        private const float LowerScore = 0.5f;
        private const float RaiseStep = 0.1f;
        private const float LowerStep = 0.17f;
        private const int MinPieces = 1;
        private const int MaxPieces = 10;
        // 난이도를 세 구간으로 나눠 접근 방식(maxLayer)과 흩어짐을 정한다.
        private const float MidTierFrom = 4f;
        private const float HighTierFrom = 7f;

        private VisualElement cubeSurface;
        private Label instructionLabel;
        private Label timerLabel;
        private VisualElement timerBarFill;
        private VisualElement timerBarTrack;
        private Label remainingLabel;
        private VisualElement carveButtons;
        private VisualElement resultPanel;
        private VisualElement quitConfirmPanel;
        private Label finalScoreLabel;
        private VisualElement trialResultsContainer;

        private SaveData saveData;
        private bool[] targetRemoved;
        private int currentRound;
        // 라운드별 점수(0~1)의 합.
        private float scoreSum;
        private float timeRemaining;
        private float timerTotalSeconds;
        // 라운드별 완벽(true)/부분 또는 실패(false)/나가기로 중간에 끝남(null). 결과창에 O/X로 보여준다.
        private readonly List<bool?> roundResults = new();
        private IVisualElementScheduledItem timerSchedule;
        // 깎는 중일 때만 true. 외우는 중이거나 이미 제출한 뒤 완성 버튼이 눌리는 것 방지.
        private bool carving;
        // 결과 화면이 떴으면 true. 나가기 뒤에 남아 있던 예약 콜백이 라운드를 다시 시작하지 않게 막는다.
        private bool finished;

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

            cubeSurface = root.Q<VisualElement>("cube-view");
            cube3D.Bind(cubeSurface);
            instructionLabel = root.Q<Label>("instruction-label");
            timerLabel = root.Q<Label>("timer-label");
            timerBarFill = root.Q<VisualElement>("timer-bar-fill");
            timerBarTrack = root.Q<VisualElement>("timer-bar-track");
            remainingLabel = root.Q<Label>("remaining-label");
            carveButtons = root.Q<VisualElement>("carve-buttons");
            resultPanel = root.Q<VisualElement>("result-panel");
            quitConfirmPanel = root.Q<VisualElement>("quit-confirm-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            trialResultsContainer = root.Q<VisualElement>("trial-results");

            root.Q<Button>("done-button").clicked += OnDone;
            root.Q<Button>("undo-button").clicked += OnUndo;
            root.Q<Button>("retry-button").clicked += OnRetry;
            root.Q<Button>("exit-button").clicked += OnExit;
            root.Q<Button>("quit-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.RemoveFromClassList("hidden"); };
            root.Q<Button>("quit-confirm-button").clicked += () => { quitConfirmPanel.AddToClassList("hidden"); OnQuit(); };
            root.Q<Button>("quit-cancel-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.AddToClassList("hidden"); };

            resultPanel.AddToClassList("hidden");
            quitConfirmPanel.AddToClassList("hidden");
            saveData = SaveManager.Load();
            LoadDifficulty(saveData.cubeCarveDifficulty);
            currentRound = 0;
            scoreSum = 0f;
            roundResults.Clear();
            StartRound();
        }

        // 외우기 단계: 뚫린 큐브를 보여주고 타이머 시작.
        private void StartRound()
        {
            if (finished)
            {
                return;
            }
            if (currentRound >= roundCount)
            {
                ShowResult();
                return;
            }

            float t = (difficulty - MinDifficulty) / (MaxDifficulty - MinDifficulty);
            int min = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(2f, 6f, t)), MinPieces, MaxPieces);
            int max = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(3f, 9f, t)), min, MaxPieces);
            int maxLayer = difficulty < MidTierFrom ? 0 : difficulty < HighTierFrom ? 1 : 2;
            var spread = difficulty < MidTierFrom ? Cube3DView.Spread.Clustered
                : difficulty < HighTierFrom ? Cube3DView.Spread.Mixed
                : Cube3DView.Spread.Scattered;
            float seconds = Mathf.Lerp(10f, 5f, t);
            targetRemoved = Cube3DView.PickRemoved(Random.Range(min, max + 1), maxLayer, spread);

            remainingLabel.text = $"{currentRound + 1}회차 / {roundCount}회";
            instructionLabel.text = MemorizeText;
            cube3D.Interactive = false;
            cube3D.SetRemoved(targetRemoved);
            cubeSurface.visible = true;
            timerLabel.visible = true;
            timerBarTrack.visible = true;
            carveButtons.visible = false;
            carving = false;

            timerTotalSeconds = seconds;
            timeRemaining = seconds;
            UpdateTimerLabel();
            timerSchedule?.Pause();
            timerSchedule = cubeSurface.schedule.Execute(TickTimer).Every(100);
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
            cubeSurface.visible = false;
            timerLabel.visible = false;
            timerBarTrack.visible = false;
            cubeSurface.schedule.Execute(StartCarving).StartingIn(SwapDelayMs);
        }

        private void UpdateTimerLabel()
        {
            timerLabel.text = Mathf.Max(0f, timeRemaining).ToString("F1");
            if (timerTotalSeconds > 0f)
            {
                timerBarFill.style.width = Length.Percent(Mathf.Clamp01(timeRemaining / timerTotalSeconds) * 100f);
            }
        }

        // 깎기 단계: 온전한 큐브에서 조각을 눌러 지우고, 완성 버튼으로 제출.
        private void StartCarving()
        {
            if (finished)
            {
                return;
            }
            SoundManager.Instance.PlaySFX("Change");
            instructionLabel.text = CarveText;
            cube3D.ClearRemoved();
            cube3D.Interactive = true;
            cubeSurface.visible = true;
            carveButtons.visible = true;
            carving = true;
        }

        // 되돌리기: 잘못 눌렀을 때 가장 마지막에 깎은 조각 하나만 되살림(누를 때마다 한 칸씩).
        private void OnUndo()
        {
            if (carving)
            {
                SoundManager.Instance.PlaySFX("CubeCarveUndo");
                cube3D.UndoLast();
            }
        }

        // 완성 버튼: 지운 모양을 채점하고, 잠깐 격려 문구를 보여준 뒤 다음 라운드로.
        private void OnDone()
        {
            if (!carving)
            {
                return;
            }

            carving = false;
            cube3D.Interactive = false;

            float roundScore = RoundScore(cube3D.GetRemoved(), targetRemoved);
            Debug.Log(roundScore >= 1f ? "맞음" : $"틀림 (부분 점수 {Mathf.RoundToInt(roundScore * 100f)}점)");
            SoundManager.Instance.PlaySFX(roundScore >= 1f ? "Correct" : "Wrong");
            scoreSum += roundScore;
            roundResults.Add(roundScore >= 1f);
            OnRoundFinished(roundScore);

            // 게임 4처럼 틀려도 긍정 문구로 안내하고 잠깐 뒤에 다음 라운드(또는 결과).
            currentRound++;
            bool last = currentRound >= roundCount;
            instructionLabel.text = roundScore >= 1f ? "잘했어요!"
                : last ? "괜찮아요, 수고했어요!" : "괜찮아요, 다음 회차로 가볼게요";
            carveButtons.visible = false;
            cubeSurface.schedule.Execute(StartRound).StartingIn(FeedbackMs);
        }

        private void OnRoundFinished(float roundScore)
        {
            if (roundScore >= RaiseScore)
            {
                AdjustDifficulty(RaiseStep);
            }
            else if (roundScore < LowerScore)
            {
                AdjustDifficulty(-LowerStep);
            }

            saveData.cubeCarveDifficulty = difficulty;
            SaveManager.Save(saveData);
            Debug.Log($"자동 난이도: {difficulty:F2}");
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
            finished = true;
            SoundManager.Instance.PlaySFX("End");
            int score = Mathf.RoundToInt(scoreSum / Mathf.Max(1, roundCount) * 100f);
            finalScoreLabel.text = ResultMessage.FromScore(score);
            TrialResultsView.Build(trialResultsContainer, roundResults);
            resultPanel.RemoveFromClassList("hidden");
        }

        // ◀ 나가기: 게임 4처럼 진행 중인 라운드를 멈추고 지금까지 기록으로 결과 화면.
        private void OnQuit()
        {
            if (finished)
            {
                return;
            }
            if (roundResults.Count <= currentRound)
            {
                roundResults.Add(null);
            }
            timerSchedule?.Pause();
            carving = false;
            cube3D.Interactive = false;
            ShowResult();
        }

        // 다시하기: 이 씬을 처음부터 다시 시작(난이도는 세이브에 이어서 자동으로 적용됨)
        private void OnRetry()
        {
            SoundManager.Instance.PlaySFX("UIClick");
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        // 나가기: 게임 시작 누르면 나오는 게임 탭 화면으로 이동
        private void OnExit()
        {
            SoundManager.Instance.PlaySFX("UIClick");
            MainMenuController.PendingReturnTarget = MainMenuController.ReturnTarget.GameTabs;
            SceneManager.LoadScene("MainScene");
        }
    }
}
