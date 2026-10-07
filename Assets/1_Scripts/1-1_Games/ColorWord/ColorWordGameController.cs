using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 색상-단어 간섭 게임 (Stroop 과제 변형, Forte et al. 2024).
    // 색 이름 단어가 어떤 잉크색으로 나오면, 글자의 '뜻'이 아니라 실제 '글자 색'에 맞는 버튼을 누른다.
    // 문항마다 응답 제한시간이 있고, 시간이 지나면 그 문항은 틀림 처리 후 다음 문항(게임은 계속).
    // 난이도(1.00~10.00)로 기획서 표의 불일치 문항 비율/문항당 제한시간을 보간해서 쓴다.
    // 문항마다 맞히면 조금 올리고 틀리면 더 많이 내려서, 정답률 약 75%(정확도 기준) 근처에서 난이도가 머문다.
    // 세이브에 저장해서 다음에도 이어짐.
    [RequireComponent(typeof(UIDocument))]
    public class ColorWordGameController : DifficultyGameController
    {
        [SerializeField] private int itemCount = 30;

        // 올리는 폭 : 내리는 폭 = 1 : 3 → 맞힌 비율이 75%일 때 오르내림이 균형.
        // 완벽하게 수행한다고 가정하면 약 43세션 만에 난이도 1->10(목표 45 ±5 이내).
        private const float RaiseStep = 0.007f;
        private const float LowerStep = 0.021f;

        // 기획서 3.2 표 변형: 문항당 응답 제한시간은 그대로 쓰되, 불일치 문항 비율은 1단계부터
        // 50%로 시작해서 6단계부터 100%가 되도록 올렸다(기획서는 0%->100%를 10단계에 걸쳐 올림).
        private static readonly (float mismatch, float seconds)[] Levels =
        {
            (0.50f, 5.0f), (0.60f, 4.7f), (0.70f, 4.4f), (0.80f, 4.1f), (0.90f, 3.8f),
            (1f, 3.5f), (1f, 3.2f), (1f, 2.8f), (1f, 2.4f), (1f, 2.0f),
        };

        // 0 빨강, 1 파랑, 2 노랑, 3 초록. 버튼 순서(color-0~3)와 같다.
        private static readonly string[] ColorNames = { "빨강", "파랑", "노랑", "초록" };
        // 노랑은 밝은 배경에서 잘 안 보여서 진하게.
        private static readonly Color[] InkColors =
        {
            new Color(0.85f, 0.12f, 0.12f),
            new Color(0.12f, 0.30f, 0.85f),
            new Color(0.93f, 0.72f, 0.00f),
            new Color(0.08f, 0.58f, 0.20f),
        };

        private const float FeedbackSeconds = 0.6f;
        private const int MaxWaitFrames = 60;

        private Label progressLabel;
        private Label instructionLabel;
        private Label wordLabel;
        private Label timerLabel;
        private VisualElement timeBar;
        private readonly Button[] colorButtons = new Button[4];
        private VisualElement resultPanel;
        private VisualElement quitConfirmPanel;
        private Label finalScoreLabel;
        private VisualElement trialResultsContainer;

        private SaveData saveData;
        private int itemIndex;
        private int correctCount;
        private int inkColor;
        private float itemSeconds;
        private float timeRemaining;
        // 답했거나 시간이 지난 뒤 다음 문항이 나오기 전까지 true(입력 무시).
        private bool itemResolved;
        private bool finished;
        // 문항별 정답/오답(null = 나가기로 중간에 끝난 문항). 결과창에 O/X로 보여준다.
        private readonly List<bool?> itemResults = new();
        private IVisualElementScheduledItem timerSchedule;

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
                Debug.LogWarning("ColorWordGameController: UI가 끝내 준비되지 않아 초기화를 건너뜀");
                yield break;
            }

            progressLabel = root.Q<Label>("progress-label");
            instructionLabel = root.Q<Label>("instruction-label");
            wordLabel = root.Q<Label>("word-label");
            timerLabel = root.Q<Label>("timer-label");
            timeBar = root.Q<VisualElement>("time-bar");
            resultPanel = root.Q<VisualElement>("result-panel");
            quitConfirmPanel = root.Q<VisualElement>("quit-confirm-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            trialResultsContainer = root.Q<VisualElement>("trial-results");

            for (int i = 0; i < colorButtons.Length; i++)
            {
                int color = i;
                colorButtons[i] = root.Q<Button>($"color-{i}");
                colorButtons[i].clicked += () => OnColorClicked(color);
            }

            root.Q<Button>("quit-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.RemoveFromClassList("hidden"); };
            root.Q<Button>("quit-confirm-button").clicked += () => { quitConfirmPanel.AddToClassList("hidden"); OnQuit(); };
            root.Q<Button>("quit-cancel-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.AddToClassList("hidden"); };
            root.Q<Button>("retry-button").clicked += OnRetry;
            root.Q<Button>("exit-button").clicked += OnExit;

            resultPanel.AddToClassList("hidden");
            quitConfirmPanel.AddToClassList("hidden");

            saveData = SaveManager.Load();
            LoadDifficulty(saveData.colorWordDifficulty);

            itemIndex = 0;
            correctCount = 0;
            itemResults.Clear();
            StartItem();
        }

        // 난이도 값 사이를 보간한 (불일치 비율, 제한시간). 예: 3.50이면 3단계와 4단계의 중간.
        private (float mismatch, float seconds) CurrentLevel()
        {
            float position = difficulty - MinDifficulty;
            int low = Mathf.Clamp(Mathf.FloorToInt(position), 0, Levels.Length - 1);
            int high = Mathf.Min(low + 1, Levels.Length - 1);
            float t = position - low;
            return (Mathf.Lerp(Levels[low].mismatch, Levels[high].mismatch, t),
                Mathf.Lerp(Levels[low].seconds, Levels[high].seconds, t));
        }

        // 새 문항: 잉크색을 정하고, 불일치 비율만큼의 확률로 글자 뜻을 다른 색 이름으로 바꾼다.
        private void StartItem()
        {
            itemResolved = false;
            var level = CurrentLevel();

            inkColor = Random.Range(0, InkColors.Length);
            int wordColor = Random.value < level.mismatch
                ? (inkColor + Random.Range(1, InkColors.Length)) % InkColors.Length
                : inkColor;

            wordLabel.text = ColorNames[wordColor];
            wordLabel.style.color = InkColors[inkColor];
            instructionLabel.text = "글자의 '색깔'을 눌러주세요";
            progressLabel.text = $"{itemIndex + 1}회차 / {itemCount}회";
            foreach (var button in colorButtons)
            {
                button.RemoveFromClassList("correct");
                button.RemoveFromClassList("wrong");
            }

            itemSeconds = level.seconds;
            timeRemaining = itemSeconds;
            UpdateTimeBar();
            timerSchedule?.Pause();
            timerSchedule = wordLabel.schedule.Execute(TickTimer).Every(100);
        }

        // 고른 색이 실제 잉크 색과 같은지로 정오답을 가린다(글자 뜻은 무시).
        private void OnColorClicked(int color)
        {
            if (itemResolved)
            {
                return;
            }

            bool correct = color == inkColor;
            colorButtons[color].AddToClassList(correct ? "correct" : "wrong");
            StartCoroutine(EndItem(correct, correct ? "맞았어요!" : "괜찮아요, 다음 문제예요"));
        }

        private void TickTimer()
        {
            if (itemResolved)
            {
                timerSchedule?.Pause();
                return;
            }

            timeRemaining -= 0.1f;
            UpdateTimeBar();

            if (timeRemaining <= 0f)
            {
                StartCoroutine(EndItem(false, "괜찮아요, 다음 문제예요"));
            }
        }

        // 남은 응답 시간을 막대 길이로만 보여준다(숫자 카운트다운 강조 지양).
        private void UpdateTimeBar()
        {
            float ratio = itemSeconds > 0f ? Mathf.Clamp01(timeRemaining / itemSeconds) : 0f;
            timeBar.style.width = Length.Percent(ratio * 100f);
            timerLabel.text = Mathf.Max(0f, timeRemaining).ToString("F1");
        }

        // 난이도를 조정/저장하고 잠깐 결과 문구를 보여준 뒤 다음 문항(또는 결과 화면)으로.
        private IEnumerator EndItem(bool correct, string message)
        {
            if (itemResolved)
            {
                yield break;
            }
            itemResolved = true;
            timerSchedule?.Pause();

            itemResults.Add(correct);
            if (correct)
            {
                SoundManager.Instance.PlaySFX("Correct");
                correctCount++;
                AdjustDifficulty(RaiseStep);
            }
            else
            {
                SoundManager.Instance.PlaySFX("Wrong");
                AdjustDifficulty(-LowerStep);
                colorButtons[inkColor].AddToClassList("correct");
            }
            instructionLabel.text = message;
            saveData.colorWordDifficulty = difficulty;
            SaveManager.Save(saveData);
            Debug.Log($"문항 {itemIndex + 1}: {(correct ? "맞음" : "틀림")}, 난이도 {difficulty:F2}");

            yield return new WaitForSeconds(FeedbackSeconds);

            itemIndex++;
            if (itemIndex >= itemCount)
            {
                ShowResult();
                yield break;
            }
            StartItem();
        }

        private void ShowResult()
        {
            finished = true;
            SoundManager.Instance.PlaySFX("End");
            int answered = Mathf.Max(1, itemIndex);
            int score = Mathf.RoundToInt((float)correctCount / answered * 100f);
            finalScoreLabel.text = ResultMessage.FromScore(score);
            TrialResultsView.Build(trialResultsContainer, itemResults);
            resultPanel.RemoveFromClassList("hidden");
        }

        // ◀ 나가기: 진행 중인 문항을 멈추고 지금까지 기록으로 결과 화면.
        private void OnQuit()
        {
            if (finished)
            {
                return;
            }
            StopAllCoroutines();
            if (!itemResolved && itemIndex < itemCount)
            {
                itemResults.Add(null);
            }
            itemResolved = true;
            timerSchedule?.Pause();
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
