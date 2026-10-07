using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 주제와 단어 찾기 게임 (Bertola et al. 2014, 의미 유창성 검사 기반).
    // 위에 주제가 나오면(예: "과일") 아래 카드 8개 중 그 주제에 맞는 단어만 찾아 터치한다.
    // 오답을 눌러도 감점/실패 없음(빨갛게 잠깐 표시만). 제한시간(60초) 안에 다 못 찾으면 그 라운드만 실패,
    // 게임 전체는 계속 진행(게임 1과 같은 방식).
    // 자동 난이도(주제 전환 방식)가 3단계로 올라간다: 0 초급 = 게임 내내 같은 주제,
    // 1 중급 = 라운드마다 주제 변경, 2 고급 = 한 라운드 안에서도 주제가 한 번 더 바뀜
    // (1차 주제 다 찾으면 2차 주제로 전환). 라운드를 연속으로 클리어하면 올라가고,
    // 시간 초과로 실패하면 바로 한 단계 내려간다. 세이브에 저장해서 다음에도 이어짐.
    [RequireComponent(typeof(UIDocument))]
    public class CategoryWordGameController : DifficultyGameController
    {
        [SerializeField] private int roundCount = 6;

        // 난이도가 쉬운 쪽(1.00)일 때 60초, 어려운 쪽(10.00)일 때 35초. 그 사이는 선형 보간.
        private const float RoundTimeEasy = 60f;
        private const float RoundTimeHard = 35f;
        private float RoundTimeSeconds => Mathf.Lerp(RoundTimeEasy, RoundTimeHard, DifficultyT);

        // 노인에게 익숙한 주제/단어. 주제별 8개씩(하/중은 4개, 상은 3개만 뽑아서 씀).
        private static readonly (string category, string[] words)[] CategoryPool =
        {
            ("과일", new[] { "사과", "바나나", "포도", "수박", "딸기", "귤", "참외", "복숭아" }),
            ("동물", new[] { "강아지", "고양이", "소", "곰", "토끼", "호랑이", "코끼리", "사자" }),
            ("채소", new[] { "당근", "오이", "배추", "감자", "양파", "호박", "고구마", "마늘" }),
            ("가구", new[] { "의자", "책상", "침대", "소파", "장롱", "식탁", "서랍장", "거울" }),
            ("색깔", new[] { "빨강", "파랑", "노랑", "초록", "주황", "보라", "하양", "검정" }),
            ("몸", new[] { "손", "발", "어깨", "코", "입", "귀", "팔", "허리" }),
            ("날씨", new[] { "비", "태풍", "바람", "구름", "천둥", "번개", "안개", "무지개" }),
            ("교통수단", new[] { "자동차", "버스", "기차", "비행기", "트럭", "택시", "자전거", "오토바이" }),
            ("직업", new[] { "의사", "선생님", "경찰", "소방관", "농부", "요리사", "가수", "화가" }),
            ("음식", new[] { "김치", "된장찌개", "불고기", "비빔밥", "떡볶이", "라면", "잡채", "삼겹살" }),
        };
        private const int CardCount = 8;

        // 자동 난이도: 구간별로 전환 방식(stage)을 정한다:
        // 0 초급(주제 고정) / 1 중급(라운드마다 변경) / 2 고급(라운드 중 전환).
        // 클리어하면 올라가고, 시간 초과 실패면 바로 내려간다.
        // 시니어 대상 앱이라 한 판(라운드 6번) 만에 확 어려워지면 안 됨 — 아주 조금씩만 움직인다.
        // 완벽하게 수행한다고 가정하면(한 판 6라운드 전부 클리어) 약 50세션 만에 난이도 1->10
        // (소수점 2자리 제한 안에서 목표 45세션에 가장 가까운 값 — 원래 값과 같음).
        private const float RaiseStep = 0.03f;
        private const float LowerStep = 0.05f;
        private const float MidStageFrom = 4f;
        private const float HighStageFrom = 7f;
        private SaveData saveData;
        private int Stage => difficulty < MidStageFrom ? 0 : difficulty < HighStageFrom ? 1 : 2;

        private Button[] cardButtons;
        private Label roundLabel;
        private Label timerLabel;
        private VisualElement timerBarFill;
        private Label categoryLabel;
        private VisualElement resultPanel;
        private VisualElement quitConfirmPanel;
        private Label finalScoreLabel;
        private VisualElement trialResultsContainer;

        private string[] cardWords;
        private string[] cardCategories;
        private bool[] cardFound;

        // 이번 라운드에서 지금 찾아야 하는 주제. 상 난이도에서는 1차 다 찾으면 2차로 바뀐다.
        private string activeTargetCategory;
        private string secondTargetCategory;
        private bool phaseTwoActive;
        private int remainingToFind;

        // 하 난이도용: 게임 시작할 때 한 번 정해서 내내 쓰는 주제.
        private string fixedCategory;
        // 중/상 난이도용: 바로 이전 라운드의(1차) 주제. 연속으로 같은 주제가 나오지 않게 막는 용도.
        private string lastTargetCategory;

        private int roundIndex;
        private int correctRounds;
        private float timeRemaining;
        private float roundTimeTotal;
        private bool roundResolved;
        private bool finished;
        // 라운드별 성공/실패(null = 나가기로 중간에 끝난 라운드). 결과창에 O/X로 보여준다.
        private readonly List<bool?> roundResults = new();
        private IVisualElementScheduledItem timerSchedule;

        private const int FlashMs = 400;
        private const int MaxWaitFrames = 60;

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
                Debug.LogWarning("CategoryWordGameController: UI가 끝내 준비되지 않아 초기화를 건너뜀");
                yield break;
            }

            roundLabel = root.Q<Label>("round-label");
            timerLabel = root.Q<Label>("timer-label");
            timerBarFill = root.Q<VisualElement>("timer-bar-fill");
            categoryLabel = root.Q<Label>("category-label");
            resultPanel = root.Q<VisualElement>("result-panel");
            quitConfirmPanel = root.Q<VisualElement>("quit-confirm-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            trialResultsContainer = root.Q<VisualElement>("trial-results");

            cardButtons = new Button[CardCount];
            for (int i = 0; i < CardCount; i++)
            {
                int card = i;
                cardButtons[i] = root.Q<Button>($"card-{i}");
                cardButtons[i].clicked += () => OnCardClicked(card);
            }

            root.Q<Button>("quit-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.RemoveFromClassList("hidden"); };
            root.Q<Button>("quit-confirm-button").clicked += () => { quitConfirmPanel.AddToClassList("hidden"); OnQuit(); };
            root.Q<Button>("quit-cancel-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.AddToClassList("hidden"); };
            root.Q<Button>("retry-button").clicked += OnRetry;
            root.Q<Button>("exit-button").clicked += OnExit;

            resultPanel.AddToClassList("hidden");
            quitConfirmPanel.AddToClassList("hidden");

            saveData = SaveManager.Load();
            LoadDifficulty(saveData.categoryWordDifficulty);

            fixedCategory = CategoryPool[Random.Range(0, CategoryPool.Length)].category;
            roundIndex = 0;
            correctRounds = 0;
            roundResults.Clear();
            StartRound();
        }

        // 새 라운드의 카드 8개를 뽑아서 배치하고 타이머를 시작한다.
        private void StartRound()
        {
            roundResolved = false;
            phaseTwoActive = false;

            var cards = new List<(string word, string category)>();

            int stage = Stage;
            if (stage >= 2)
            {
                var catIdx = PickCategoryIndices(3);
                AddWords(cards, catIdx[0], 3);
                AddWords(cards, catIdx[1], 3);
                AddWords(cards, catIdx[2], 2);
                activeTargetCategory = CategoryPool[catIdx[0]].category;
                secondTargetCategory = CategoryPool[catIdx[1]].category;
            }
            else
            {
                int targetIdx = stage == 1 ? PickTargetIndexExcluding(lastTargetCategory)
                    : System.Array.FindIndex(CategoryPool, c => c.category == fixedCategory);
                int distractorIdx = PickCategoryIndices(1, targetIdx)[0];
                AddWords(cards, targetIdx, 4);
                AddWords(cards, distractorIdx, 4);
                activeTargetCategory = CategoryPool[targetIdx].category;
                secondTargetCategory = null;
            }
            lastTargetCategory = activeTargetCategory;

            cards = cards.OrderBy(_ => Random.value).ToList();
            cardWords = cards.Select(c => c.word).ToArray();
            cardCategories = cards.Select(c => c.category).ToArray();
            cardFound = new bool[CardCount];

            for (int i = 0; i < CardCount; i++)
            {
                cardButtons[i].text = cardWords[i];
                cardButtons[i].RemoveFromClassList("found");
                cardButtons[i].RemoveFromClassList("wrong");
                cardButtons[i].SetEnabled(true);
            }

            remainingToFind = cardCategories.Count(c => c == activeTargetCategory);
            roundLabel.text = $"{roundIndex + 1}회차 / {roundCount}회";
            categoryLabel.text = $"주제 '{activeTargetCategory}'{WaGwa(activeTargetCategory)} 관련된 단어를\n모두 찾아 터치하세요";

            roundTimeTotal = RoundTimeSeconds;
            timeRemaining = roundTimeTotal;
            UpdateTimerLabel();
            timerSchedule?.Pause();
            timerSchedule = cardButtons[0].schedule.Execute(TickTimer).Every(100);
        }

        // 제외할 주제(있으면)를 뺀 나머지 중 무작위 count개의 인덱스.
        private static List<int> PickCategoryIndices(int count, params int?[] exclude)
        {
            return Enumerable.Range(0, CategoryPool.Length)
                .Where(i => !exclude.Contains((int?)i))
                .OrderBy(_ => Random.value)
                .Take(count)
                .ToList();
        }

        private static int PickTargetIndexExcluding(string excludedCategory)
        {
            var candidates = Enumerable.Range(0, CategoryPool.Length)
                .Where(i => CategoryPool[i].category != excludedCategory)
                .ToList();
            return candidates[Random.Range(0, candidates.Count)];
        }

        // 주어진 주제 풀에서 count개를 무작위로 뽑아 cards에 추가.
        private static void AddWords(List<(string word, string category)> cards, int categoryIndex, int count)
        {
            var pool = CategoryPool[categoryIndex].words;
            var picks = Enumerable.Range(0, pool.Length).OrderBy(_ => Random.value).Take(count);
            foreach (int i in picks)
            {
                cards.Add((pool[i], CategoryPool[categoryIndex].category));
            }
        }

        // 단어 마지막 글자 받침 유무로 "와"/"과" 조사를 고른다(받침 없으면 와, 있으면 과).
        private static string WaGwa(string word)
        {
            char last = word[word.Length - 1];
            if (last < 0xAC00 || last > 0xD7A3)
            {
                return "와";
            }
            int finalConsonant = (last - 0xAC00) % 28;
            return finalConsonant == 0 ? "와" : "과";
        }

        // 주제에 맞는 카드면 찾음 처리(1차 다 찾으면 상 난이도에선 2차 주제로 전환), 아니면 잠깐 빨갛게 표시만.
        private void OnCardClicked(int card)
        {
            if (roundResolved || cardFound[card])
            {
                return;
            }

            if (cardCategories[card] != activeTargetCategory)
            {
                SoundManager.Instance.PlaySFX("Wrong");
                Flash(card);
                return;
            }

            SoundManager.Instance.PlaySFX("Correct");
            cardFound[card] = true;
            cardButtons[card].AddToClassList("found");
            cardButtons[card].SetEnabled(false);
            remainingToFind--;

            if (remainingToFind > 0)
            {
                return;
            }

            if (!phaseTwoActive && secondTargetCategory != null)
            {
                SoundManager.Instance.PlaySFX("Change");
                phaseTwoActive = true;
                activeTargetCategory = secondTargetCategory;
                remainingToFind = cardCategories.Count(c => c == activeTargetCategory);
                categoryLabel.text = $"좋아요! 이번엔 주제 '{activeTargetCategory}'{WaGwa(activeTargetCategory)}\n관련된 단어를 모두 찾아 터치하세요";
            }
            else
            {
                StartCoroutine(EndRound(true));
            }
        }

        private void Flash(int card)
        {
            cardButtons[card].AddToClassList("wrong");
            cardButtons[card].schedule.Execute(() => cardButtons[card].RemoveFromClassList("wrong")).StartingIn(FlashMs);
        }

        private void TickTimer()
        {
            if (roundResolved)
            {
                timerSchedule?.Pause();
                return;
            }

            timeRemaining -= 0.1f;
            UpdateTimerLabel();

            if (timeRemaining <= 0f)
            {
                timerSchedule?.Pause();
                StartCoroutine(EndRound(false));
            }
        }

        private void UpdateTimerLabel()
        {
            timerLabel.text = Mathf.Max(0f, timeRemaining).ToString("F1");
            if (roundTimeTotal > 0f)
            {
                timerBarFill.style.width = Length.Percent(Mathf.Clamp01(timeRemaining / roundTimeTotal) * 100f);
            }
        }

        // 라운드 결과를 잠깐 보여주고 다음 라운드로(또는 마지막이면 결과 화면으로).
        private IEnumerator EndRound(bool cleared)
        {
            roundResolved = true;
            timerSchedule?.Pause();
            roundResults.Add(cleared);
            foreach (var button in cardButtons)
            {
                button.SetEnabled(false);
            }

            if (cleared)
            {
                correctRounds++;
                categoryLabel.text = "잘했어요!";
                AdjustDifficulty(RaiseStep);
            }
            else
            {
                SoundManager.Instance.PlaySFX("Wrong");
                categoryLabel.text = "괜찮아요, 다음 회차로 가볼게요";
                AdjustDifficulty(-LowerStep);
            }
            saveData.categoryWordDifficulty = difficulty;
            SaveManager.Save(saveData);
            Debug.Log($"라운드 {roundIndex + 1}: {(cleared ? "성공" : "실패")}, 난이도 {difficulty:F2}");

            yield return new WaitForSeconds(1.2f);

            roundIndex++;
            if (roundIndex >= roundCount)
            {
                ShowResult();
                yield break;
            }
            StartRound();
        }

        private void ShowResult()
        {
            finished = true;
            SoundManager.Instance.PlaySFX("End");
            int score = roundCount > 0 ? Mathf.RoundToInt((float)correctRounds / roundCount * 100f) : 0;
            finalScoreLabel.text = ResultMessage.FromScore(score);
            TrialResultsView.Build(trialResultsContainer, roundResults);
            resultPanel.RemoveFromClassList("hidden");
        }

        // ◀ 나가기: 진행 중인 라운드를 멈추고 지금까지 기록으로 결과 화면.
        private void OnQuit()
        {
            if (finished)
            {
                return;
            }
            StopAllCoroutines();
            if (!roundResolved)
            {
                roundResults.Add(null);
            }
            roundResolved = true;
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
