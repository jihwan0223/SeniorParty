using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 그림-단어 짝짓기 게임 (Boston Naming Test 기반 대면이름대기, Hwang & Kim 2014).
    // 사물 그림 1개를 보고 아래 단어 4개 중 알맞은 이름을 누른다. 문항마다 제한시간이 있고,
    // 시간이 지나면 그 문항은 틀림 처리 후 다음 문항(게임은 계속). 틀리면 정답을 잠깐 보여준다.
    // 난이도(1.00~10.00)로 기획서 표의 세 변수를 정한다: 목표 단어 빈도 구간(PictureWordSet의 level),
    // 오답 중 유사어 개수(1~4: 0개, 5~6: 1개, 7~8: 2개, 9~10: 전부), 제한시간(20초 -> 7초, 보간).
    // 문항마다 맞히면 조금 올리고 틀리면 더 많이 내려서 정답률 약 75% 근처에서 머문다(색깔 찾기 게임과 같은 방식).
    [RequireComponent(typeof(UIDocument))]
    public class PictureWordGameController : DifficultyGameController
    {
        [SerializeField] private PictureWordSet wordSet;
        [SerializeField] private int itemCount = 10;

        // 기획서 3.3 표: 난이도 1~10의 (오답 중 유사어 개수, 제한시간 초).
        private static readonly (int similar, float seconds)[] Levels =
        {
            (0, 20f), (0, 18f), (0, 16f), (0, 14f), (1, 12f),
            (1, 11f), (2, 10f), (2, 9f), (3, 8f), (3, 7f),
        };
        private const int ChoiceCount = 4;

        // 올리는 폭 : 내리는 폭 = 1 : 3 → 맞힌 비율이 75%일 때 균형.
        // 완벽하게 수행한다고 가정하면(한 판 10문항 전부 정답) 정확히 45세션 만에 난이도 1->10.
        private const float RaiseStep = 0.02f;
        private const float LowerStep = 0.06f;

        private const float FeedbackSeconds = 1.2f;
        private const int MaxWaitFrames = 60;

        private Label progressLabel;
        private Label instructionLabel;
        private VisualElement pictureImage;
        private Label picturePlaceholder;
        private Label timerLabel;
        private VisualElement timeBar;
        private readonly Button[] choiceButtons = new Button[ChoiceCount];
        private VisualElement resultPanel;
        private VisualElement quitConfirmPanel;
        private Label finalScoreLabel;
        private VisualElement trialResultsContainer;

        private SaveData saveData;
        private readonly HashSet<PictureWordSet.Entry> usedEntries = new();
        private int answerIndex;
        private int itemIndex;
        private int correctCount;
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
                Debug.LogWarning("PictureWordGameController: UI가 끝내 준비되지 않아 초기화를 건너뜀");
                yield break;
            }

            progressLabel = root.Q<Label>("progress-label");
            instructionLabel = root.Q<Label>("instruction-label");
            pictureImage = root.Q<VisualElement>("picture-image");
            picturePlaceholder = root.Q<Label>("picture-placeholder");
            timerLabel = root.Q<Label>("timer-label");
            timeBar = root.Q<VisualElement>("time-bar");
            resultPanel = root.Q<VisualElement>("result-panel");
            quitConfirmPanel = root.Q<VisualElement>("quit-confirm-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            trialResultsContainer = root.Q<VisualElement>("trial-results");

            for (int i = 0; i < ChoiceCount; i++)
            {
                int choice = i;
                choiceButtons[i] = root.Q<Button>($"choice-{i}");
                choiceButtons[i].clicked += () => OnChoiceClicked(choice);
            }

            root.Q<Button>("quit-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.RemoveFromClassList("hidden"); };
            root.Q<Button>("quit-confirm-button").clicked += () => { quitConfirmPanel.AddToClassList("hidden"); OnQuit(); };
            root.Q<Button>("quit-cancel-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.AddToClassList("hidden"); };
            root.Q<Button>("retry-button").clicked += OnRetry;
            root.Q<Button>("exit-button").clicked += OnExit;

            resultPanel.AddToClassList("hidden");
            quitConfirmPanel.AddToClassList("hidden");

            if (wordSet == null || wordSet.entries.Count == 0)
            {
                Debug.LogWarning("PictureWordGameController: 단어 목록(PictureWordSet)이 비어 있음");
                yield break;
            }

            saveData = SaveManager.Load();
            LoadDifficulty(saveData.pictureWordDifficulty);

            itemIndex = 0;
            correctCount = 0;
            itemResults.Clear();
            StartItem();
        }

        // 난이도를 반올림한 단계(1~10). 빈도 구간/유사어 개수는 단계별로 딱 끊기는 값이라 반올림으로 고른다.
        private int CurrentStep => Mathf.Clamp(Mathf.RoundToInt(difficulty), 1, Levels.Length);

        // 제한시간은 난이도 값 사이를 보간한다. 예: 3.50이면 3단계와 4단계의 중간.
        private float CurrentSeconds()
        {
            float position = difficulty - MinDifficulty;
            int low = Mathf.Clamp(Mathf.FloorToInt(position), 0, Levels.Length - 1);
            int high = Mathf.Min(low + 1, Levels.Length - 1);
            return Mathf.Lerp(Levels[low].seconds, Levels[high].seconds, position - low);
        }

        // 새 문항: 단어/그림을 고르고 오답 선택지를 섞어 배치한 뒤 타이머를 시작한다.
        private void StartItem()
        {
            itemResolved = false;
            int step = CurrentStep;

            PictureWordSet.Entry entry = PickEntry();
            usedEntries.Add(entry);

            var choices = new List<string> { entry.word };
            choices.AddRange(PickDistractors(entry, Levels[step - 1].similar));
            choices = choices.OrderBy(_ => Random.value).ToList();
            answerIndex = choices.IndexOf(entry.word);

            for (int i = 0; i < ChoiceCount; i++)
            {
                choiceButtons[i].text = i < choices.Count ? choices[i] : "";
                choiceButtons[i].SetEnabled(i < choices.Count);
                choiceButtons[i].RemoveFromClassList("correct");
                choiceButtons[i].RemoveFromClassList("wrong");
            }

            pictureImage.style.backgroundImage = entry.picture != null
                ? new StyleBackground(entry.picture)
                : new StyleBackground(StyleKeyword.None);
            picturePlaceholder.style.display = entry.picture != null ? DisplayStyle.None : DisplayStyle.Flex;

            instructionLabel.text = "그림에 알맞은 이름을 눌러주세요";
            progressLabel.text = $"{itemIndex + 1}회차 / {itemCount}회";

            itemSeconds = CurrentSeconds();
            timeRemaining = itemSeconds;
            UpdateTimeBar();
            timerSchedule?.Pause();
            timerSchedule = pictureImage.schedule.Execute(TickTimer).Every(100);
        }

        // 이번 판에 아직 안 나온 단어 중 무작위로 하나(난이도와 무관하게 40개 전부 골고루 나오게).
        // 다 나왔으면 처음부터 다시 쓴다.
        private PictureWordSet.Entry PickEntry()
        {
            var candidates = wordSet.entries.Where(e => !usedEntries.Contains(e)).ToList();
            if (candidates.Count == 0)
            {
                usedEntries.Clear();
                candidates = wordSet.entries.ToList();
            }
            return candidates[Random.Range(0, candidates.Count)];
        }

        // 오답 3개: 유사어 similarCount개(그 단어의 similarWords -> 같은 카테고리 단어 순으로 채움),
        // 나머지는 다른 카테고리 단어(완전 무관). 같은 단어가 두 번 나오지 않게 한다.
        private List<string> PickDistractors(PictureWordSet.Entry entry, int similarCount)
        {
            int needed = ChoiceCount - 1;
            var picked = new List<string>();

            var similar = (entry.similarWords ?? new string[0]).OrderBy(_ => Random.value)
                .Concat(wordSet.entries.Where(e => e != entry && e.category == entry.category)
                    .Select(e => e.word).OrderBy(_ => Random.value));
            AddDistinct(picked, similar, entry.word, similarCount);

            var unrelated = wordSet.entries.Where(e => e.category != entry.category)
                .Select(e => e.word).OrderBy(_ => Random.value);
            AddDistinct(picked, unrelated, entry.word, needed);

            // 단어 목록이 작아 무관한 단어가 모자라면 아무 단어로 채운다.
            AddDistinct(picked, wordSet.entries.Select(e => e.word).OrderBy(_ => Random.value), entry.word, needed);
            return picked;
        }

        private static void AddDistinct(List<string> picked, IEnumerable<string> source, string answer, int upTo)
        {
            foreach (string word in source)
            {
                if (picked.Count >= upTo)
                {
                    return;
                }
                if (!string.IsNullOrEmpty(word) && word != answer && !picked.Contains(word))
                {
                    picked.Add(word);
                }
            }
        }

        // 고른 선택지가 정답 인덱스와 같은지로 정오답을 가린다.
        private void OnChoiceClicked(int choice)
        {
            if (itemResolved)
            {
                return;
            }

            bool correct = choice == answerIndex;
            if (!correct)
            {
                choiceButtons[choice].AddToClassList("wrong");
            }
            StartCoroutine(EndItem(correct, correct ? "맞았어요!" : "괜찮아요, 정답은 초록색이에요"));
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
                StartCoroutine(EndItem(false, "괜찮아요, 정답은 초록색이에요"));
            }
        }

        // 남은 응답 시간을 막대 길이로만 보여준다(숫자 카운트다운 강조 지양).
        private void UpdateTimeBar()
        {
            float ratio = itemSeconds > 0f ? Mathf.Clamp01(timeRemaining / itemSeconds) : 0f;
            timeBar.style.width = Length.Percent(ratio * 100f);
            timerLabel.text = Mathf.Max(0f, timeRemaining).ToString("F1");
        }

        // 정답을 표시하고 난이도를 조정/저장한 뒤, 잠깐 결과 문구를 보여주고 다음 문항으로.
        private IEnumerator EndItem(bool correct, string message)
        {
            if (itemResolved)
            {
                yield break;
            }
            itemResolved = true;
            timerSchedule?.Pause();

            choiceButtons[answerIndex].AddToClassList("correct");
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
            }
            instructionLabel.text = message;
            saveData.pictureWordDifficulty = difficulty;
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
