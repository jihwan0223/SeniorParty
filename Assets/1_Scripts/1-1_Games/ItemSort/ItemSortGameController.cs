using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 물건 분류 게임
    [RequireComponent(typeof(UIDocument))]
    public class ItemSortGameController : DifficultyGameController
    {
        [SerializeField] private List<SortItemData> items = new();
        [SerializeField] private int roundCount = 30;        // 등장 아이템 개수

        // 자동 난이도: 한 묶음을 끝까지 다 맞히면 올라가고, 하나라도 틀리면 내려간다.
        // 조정은 "묶음 하나 끝날 때"마다 일어나는데, 묶음 길이가 난이도에 따라 평균 6개(쉬움)~2개(어려움)로
        // 달라져서(CurrentRunRange) 완벽하게 수행한다고 가정했을 때 약 40세션 만에 난이도 1->10
        // (소수점 2자리 제한 안에서 목표 45세션에 가장 가까운 값 — 원래 값과 같음).
        private const float RaiseStep = 0.03f;
        private const float LowerStep = 0.05f;
        private const int MinRunLength = 1;
        private const int MaxRunLength = 6;

        // 연속 출현 개수를 고정 상한이 아니라 a~b 범위로 뽑는다. 난이도가 높을수록 평균 길이(중심)는
        // 짧아지지만 동시에 폭(half-width)이 넓어져서, 최고난도에서도 "매번 전환"처럼 예측 가능해지지
        // 않고 언제 바뀔지 더 예측하기 어려워진다(전환이 너무 규칙적이면 오히려 쉬워진다는 논문 근거).
        private const float RunCenterEasy = 6f;
        private const float RunCenterHard = 2f;
        private const float RunHalfWidthEasy = 0.5f;
        private const float RunHalfWidthHard = 2.5f;

        private const int FeedbackMs = 1200;

        private VisualElement itemBox;
        private VisualElement itemIcon;
        private VisualElement previewBox;
        private VisualElement nextItemIcon;
        private Label itemLabel;
        private Label nextItemLabel;
        private Label remainingLabel;
        private Label instructionLabel;
        private string instructionText;
        private IVisualElementScheduledItem instructionRestore;
        private Label timerLabel;
        private VisualElement timerBarFill;
        private VisualElement resultPanel;
        private VisualElement quitConfirmPanel;
        private Label finalScoreLabel;
        private VisualElement trialResultsContainer;
        private Button keepButton;
        private Button discardButton;

        private SaveData saveData;

        private (int a, int b) CurrentRunRange
        {
            get
            {
                float center = Mathf.Lerp(RunCenterEasy, RunCenterHard, DifficultyT);
                float halfWidth = Mathf.Lerp(RunHalfWidthEasy, RunHalfWidthHard, DifficultyT);
                int a = Mathf.Clamp(Mathf.RoundToInt(center - halfWidth), MinRunLength, MaxRunLength);
                int b = Mathf.Clamp(Mathf.RoundToInt(center + halfWidth), a, MaxRunLength);
                return (a, b);
            }
        }

        // 지금 보여주는 묶음: 물건과, 이 물건이 몇 번 더(이번 포함) 나올지, 지금까지 다 맞혔는지.
        private SortItemData currentItem;
        private int currentRunLeft;
        private bool currentRunAllCorrect;
        // 다음 묶음으로 미리 정해둔 물건/길이(미리보기 아이콘과 전환 시 그대로 사용).
        private SortItemData nextRunItem;
        private int nextRunLength;

        private int shownCount;
        private int correctCount;
        // 라운드별 성공/실패(null = 나가기로 중간에 끝난 라운드). 결과창에 O/X로 보여준다.
        private readonly List<bool?> roundResults = new();

        // 버튼 클릭과 타임아웃이 겹쳐도 라운드가 두 번 안 넘어가게 막는 잠금
        private bool roundResolved;
        // 결과 화면이 떴으면 true. 나가기 뒤에 남아 있던 타이머 콜백이 다시 켜지 않게 막는다.
        private bool finished;
        private float timeRemaining;
        private float timerTotalSeconds;
        private IVisualElementScheduledItem timerSchedule;

        // SceneManager.LoadScene으로 런타임에 이 씬이 막 로드되면 UIDocument가 UXML을
        // 붙이는 데 몇 프레임이 걸릴 수 있다(에디터에서 씬 열고 바로 Play할 때는 안 그럼 —
        // 런타임 씬 전환일 때만 생기는 타이밍 문제, 정확히 몇 프레임 걸릴지 보장 안 됨).
        // 그래서 한 프레임만 기다리지 않고 실제로 붙을 때까지 매 프레임 확인한다.
        private const int MaxWaitFrames = 60;

        private void OnEnable()
        {
            StartCoroutine(InitializeWhenUiReady());
        }

        // UI 요소를 전부 찾아 연결하고 첫 묶음/아이템을 세팅해서 게임을 시작한다.
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
                Debug.LogWarning("ItemSortGameController: UI가 끝내 준비되지 않아 초기화를 건너뜀");
                yield break;
            }

            itemBox = root.Q<VisualElement>("item-box");
            itemIcon = root.Q<VisualElement>("item-image");
            previewBox = root.Q<VisualElement>("preview-box");
            nextItemIcon = root.Q<VisualElement>("next-item-image");
            itemLabel = root.Q<Label>("item-label");
            nextItemLabel = root.Q<Label>("next-item-label");
            remainingLabel = root.Q<Label>("remaining-label");
            instructionLabel = root.Q<Label>("instruction-label");
            instructionText = instructionLabel?.text;
            timerLabel = root.Q<Label>("timer-label");
            timerBarFill = root.Q<VisualElement>("timer-bar-fill");
            resultPanel = root.Q<VisualElement>("result-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            trialResultsContainer = root.Q<VisualElement>("trial-results");
            quitConfirmPanel = root.Q<VisualElement>("quit-confirm-panel");

            keepButton = root.Q<Button>("keep-button");
            discardButton = root.Q<Button>("discard-button");
            Bind(root, "keep-button", () => ResolveRound(ItemAction.Keep));
            Bind(root, "discard-button", () => ResolveRound(ItemAction.Discard));
            Bind(root, "retry-button", OnRetry);
            Bind(root, "exit-button", OnExit);
            Bind(root, "quit-button", () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel?.RemoveFromClassList("hidden"); });
            Bind(root, "quit-confirm-button", () => { quitConfirmPanel?.AddToClassList("hidden"); OnQuit(); });
            Bind(root, "quit-cancel-button", () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel?.AddToClassList("hidden"); });

            resultPanel?.AddToClassList("hidden");
            quitConfirmPanel?.AddToClassList("hidden");

            if (items == null || items.Count == 0)
            {
                Debug.LogWarning("ItemSortGameController: 아이템 목록이 비어 있음");
                yield break;
            }

            saveData = SaveManager.Load();
            LoadDifficulty(saveData.itemSortDifficulty);

            shownCount = 0;
            correctCount = 0;
            roundResults.Clear();

            currentItem = PickItem();
            var initialRange = CurrentRunRange;
            currentRunLeft = Random.Range(initialRange.a, initialRange.b + 1);
            currentRunAllCorrect = true;
            GenerateNextRun();

            ShowCurrentItem();
        }

        private SortItemData PickItem()
        {
            return items[Random.Range(0, items.Count)];
        }

        // 다음 묶음은 지금 묶음과 반드시 다른 물건으로 뽑는다. 이게 없으면(둘 다 완전 무작위라서)
        // 우연히 같은 물건이 연달아 뽑혀 묶음 경계가 안 보이고 동그라미/네모가 10개 넘게 이어지는 일이 잦았다.
        private SortItemData PickItemExcluding(SortItemData exclude)
        {
            if (items.Count <= 1)
            {
                return PickItem();
            }
            SortItemData picked;
            do
            {
                picked = PickItem();
            } while (picked == exclude);
            return picked;
        }

        private void GenerateNextRun()
        {
            nextRunItem = PickItemExcluding(currentItem);
            var range = CurrentRunRange;
            nextRunLength = Random.Range(range.a, range.b + 1);
        }

        // 난이도가 쉬운 쪽(1.00)일 때 7초, 어려운 쪽(10.00)일 때 3초. 그 사이는 선형 보간.
        private float GetTimerSeconds()
        {
            return Mathf.Lerp(7f, 3f, DifficultyT);
        }

        // 지금 물건을 아이콘/이름표에 띄우고 미리보기·회차 표시를 갱신한 뒤 타이머를 시작한다.
        private void ShowCurrentItem()
        {
            if (shownCount >= roundCount)
            {
                ShowResult();
                return;
            }

            SetSprite(itemIcon, currentItem.sprite);
            if (itemLabel != null)
            {
                itemLabel.text = currentItem.shapeName;
            }

            UpdatePreviewDisplay();
            UpdateRemainingLabel();
            StartTimer();
        }

        // "다음" 칸에 보여줄 물건(마지막 라운드면 없음)을 계산해서 아이콘/이름표에 반영.
        private void UpdatePreviewDisplay()
        {
            SortItemData previewItem = null;
            if (shownCount + 1 < roundCount)
            {
                previewItem = currentRunLeft > 1 ? currentItem : nextRunItem;
            }
            SetSprite(nextItemIcon, previewItem != null ? previewItem.sprite : null);
            if (nextItemLabel != null)
            {
                nextItemLabel.text = previewItem != null ? previewItem.shapeName : "";
            }
        }

        private void UpdateRemainingLabel()
        {
            if (remainingLabel != null)
            {
                remainingLabel.text = $"{Mathf.Min(shownCount + 1, roundCount)}회차 / {roundCount}회";
            }
        }

        // 버튼을 찾아서 클릭 콜백을 연결. 이름이 안 맞거나 UI가 덜 만들어졌으면
        // 앱 전체가 죽는 대신 경고만 남기고 나머지 초기화는 계속 진행한다.
        private static void Bind(VisualElement root, string buttonName, System.Action onClick)
        {
            var button = root.Q<Button>(buttonName);
            if (button == null)
            {
                Debug.LogWarning($"ItemSortGameController: '{buttonName}' 버튼을 찾지 못함");
                return;
            }
            button.clicked += onClick;
        }

        private static void SetSprite(VisualElement target, Sprite sprite)
        {
            target.style.backgroundImage = sprite != null
                ? new StyleBackground(sprite)
                : new StyleBackground(StyleKeyword.None);
        }

        private void SetAnswerButtonsEnabled(bool enabled)
        {
            keepButton?.SetEnabled(enabled);
            discardButton?.SetEnabled(enabled);
        }

        // 라운드 타이머를 (재)시작하고 버튼을 풀어준다.
        private void StartTimer()
        {
            if (finished)
            {
                return;
            }
            SetAnswerButtonsEnabled(true);
            roundResolved = false;
            timerTotalSeconds = GetTimerSeconds();
            timeRemaining = timerTotalSeconds;
            UpdateTimerLabel();

            timerSchedule?.Pause();
            timerSchedule = itemBox.schedule.Execute(TickTimer).Every(100);
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
                ResolveRound(null); // 시간 초과 = 틀림 처리
            }
        }

        private void UpdateTimerLabel()
        {
            if (timerLabel != null)
            {
                timerLabel.text = Mathf.Max(0f, timeRemaining).ToString("F1");
            }
            if (timerBarFill != null && timerTotalSeconds > 0f)
            {
                timerBarFill.style.width = Length.Percent(Mathf.Clamp01(timeRemaining / timerTotalSeconds) * 100f);
            }
        }

        // 보관/버리기 버튼 또는 시간 초과(null)로 라운드를 확정한다.
        // roundResolved 잠금으로, 버튼 클릭과 타임아웃이 같은 라운드에서 겹쳐도 한 번만 처리된다.
        private void ResolveRound(ItemAction? chosen)
        {
            if (finished || roundResolved || shownCount >= roundCount)
            {
                return;
            }

            roundResolved = true;
            timerSchedule?.Pause();

            bool correct = chosen.HasValue && chosen.Value == currentItem.correctAction;
            Debug.Log(correct ? "맞음" : "틀림");
            SoundManager.Instance.PlaySFX(correct ? "Correct" : "Wrong");
            roundResults.Add(correct);
            // 게임 4처럼 틀리거나 시간이 지나도 긍정 문구로 안내하고, 잠시 뒤 설명 문구로 되돌린다.
            if (instructionLabel != null)
            {
                instructionLabel.text = correct ? "잘했어요!" : "괜찮아요!";
                instructionRestore?.Pause();
                instructionRestore = instructionLabel.schedule.Execute(() => instructionLabel.text = instructionText)
                    .StartingIn(FeedbackMs);
            }

            if (correct)
            {
                correctCount++;
            }
            else
            {
                currentRunAllCorrect = false;
            }

            shownCount++;
            currentRunLeft--;
            if (currentRunLeft <= 0)
            {
                OnRunFinished(currentRunAllCorrect);
                currentItem = nextRunItem;
                currentRunLeft = nextRunLength;
                currentRunAllCorrect = true;
                GenerateNextRun();
            }

            ShowCurrentItem();
        }

        // 방금 끝난 묶음을 다 맞혔으면 난이도를 올리고(전환 잦아짐), 하나라도 틀렸으면 내린다.
        private void OnRunFinished(bool allCorrect)
        {
            AdjustDifficulty(allCorrect ? RaiseStep : -LowerStep);
            saveData.itemSortDifficulty = difficulty;
            SaveManager.Save(saveData);
        }

        private void ShowResult()
        {
            int score = roundCount > 0
                ? Mathf.RoundToInt((float)correctCount / roundCount * 100f)
                : 0;

            if (finalScoreLabel == null || trialResultsContainer == null || resultPanel == null)
            {
                Debug.LogWarning("ItemSortGameController: 결과 화면 UI를 못 찾아서 결과를 표시하지 못함");
                return;
            }

            finished = true;
            SoundManager.Instance.PlaySFX("End");
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
            if (!roundResolved && shownCount < roundCount)
            {
                roundResults.Add(null);
            }
            roundResolved = true;
            timerSchedule?.Pause();
            SetAnswerButtonsEnabled(false);
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
