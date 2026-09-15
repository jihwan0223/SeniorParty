using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 물건 분류 게임
    [RequireComponent(typeof(UIDocument))]
    public class ItemSortGameController : MonoBehaviour
    {
        [SerializeField] private List<SortItemData> easyItems = new();
        [SerializeField] private List<SortItemData> normalItems = new();
        [SerializeField] private List<SortItemData> hardItems = new();

        [SerializeField] private int roundCount = 30;        // 등장 아이템 개수

        private VisualElement itemBox;
        private VisualElement itemIcon;
        private VisualElement nextItemIcon;
        private Label remainingLabel;
        private Label timerLabel;
        private VisualElement resultPanel;
        private Label finalScoreLabel;
        private Label finalTimeLabel;

        private List<SortItemData> roundItems;
        private int currentIndex;
        private int correctCount;
        private float startTime;

        // 버튼 클릭과 타임아웃이 겹쳐도 라운드가 두 번 안 넘어가게 막는 잠금
        private bool roundResolved;
        private float timeRemaining;
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
            nextItemIcon = root.Q<VisualElement>("next-item-image");
            remainingLabel = root.Q<Label>("remaining-label");
            timerLabel = root.Q<Label>("timer-label");
            resultPanel = root.Q<VisualElement>("result-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            finalTimeLabel = root.Q<Label>("final-time-label");

            Bind(root, "keep-button", () => ResolveRound(ItemAction.Keep));
            Bind(root, "discard-button", () => ResolveRound(ItemAction.Discard));
            Bind(root, "retry-button", OnRetry);
            Bind(root, "exit-button", OnExit);

            resultPanel?.AddToClassList("hidden");

            roundItems = BuildRoundItems();
            correctCount = 0;
            currentIndex = 0;
            startTime = Time.time;
            ShowCurrentItem();
        }

        // 난이도별 아이템 풀.
        private List<SortItemData> GetPool()
        {
            return MainMenuController.LastDifficulty switch
            {
                "중" => normalItems,
                "상" => hardItems,
                _ => easyItems,
            };
        }

        // 난이도별 제한 시간(초).
        private float GetTimerSeconds()
        {
            return MainMenuController.LastDifficulty switch
            {
                "중" => 5f,
                "상" => 3f,
                _ => 7f,
            };
        }

        // 난이도별 순서로 라운드 아이템 목록을 만든다.
        // 하: 최대 6개 연속(같은 물건이 자주 반복), 중: 최대 5개 연속, 상: 완전 랜덤(연속 제한 없음).
        private List<SortItemData> BuildRoundItems()
        {
            var pool = GetPool();
            if (pool == null || pool.Count == 0)
            {
                Debug.LogWarning($"선택된 난이도의 아이템 풀이 비어 있음 (난이도=\"{MainMenuController.LastDifficulty}\", " +
                    $"easy={easyItems.Count} normal={normalItems.Count} hard={hardItems.Count})");
                return new List<SortItemData>();
            }

            return MainMenuController.LastDifficulty switch
            {
                "상" => BuildFullyRandom(pool),
                "중" => BuildStreaky(pool, maxRun: 5),
                _ => BuildStreaky(pool, maxRun: 6),
            };
        }

        private List<SortItemData> BuildFullyRandom(List<SortItemData> pool)
        {
            var result = new List<SortItemData>(roundCount);
            for (int i = 0; i < roundCount; i++)
            {
                result.Add(pool[Random.Range(0, pool.Count)]);
            }
            return result;
        }

        // 같은 아이템을 1~maxRun개 연속으로 묶어서 채운다.
        private List<SortItemData> BuildStreaky(List<SortItemData> pool, int maxRun)
        {
            var result = new List<SortItemData>(roundCount);
            while (result.Count < roundCount)
            {
                var item = pool[Random.Range(0, pool.Count)];
                int runLength = Random.Range(1, maxRun + 1);
                for (int i = 0; i < runLength && result.Count < roundCount; i++)
                {
                    result.Add(item);
                }
            }
            return result;
        }

        // 현재 물건을 가운데 박스에 띄우고 왼쪽에서 넘어오는 연출 + 타이머 시작. 다음 물건 미리보기도 갱신.
        private void ShowCurrentItem()
        {
            if (currentIndex >= roundItems.Count)
            {
                ShowResult();
                return;
            }

            SetSprite(itemIcon, roundItems[currentIndex].sprite);
            SetSprite(nextItemIcon, currentIndex + 1 < roundItems.Count ? roundItems[currentIndex + 1].sprite : null);

            if (remainingLabel != null)
            {
                remainingLabel.text = $"{roundItems.Count - currentIndex} / {roundItems.Count}";
            }

            itemBox.style.translate = new Translate(Length.Percent(-400), 0);
            itemBox.schedule.Execute(() => itemBox.style.translate = new Translate(0, 0)).StartingIn(20);

            StartTimer();
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

        // 라운드 타이머를 (재)시작한다.
        private void StartTimer()
        {
            roundResolved = false;
            timeRemaining = GetTimerSeconds();
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
                timerLabel.text = Mathf.Max(0, Mathf.CeilToInt(timeRemaining)).ToString();
            }
        }

        // 보관/버리기 버튼 또는 시간 초과(null)로 라운드를 확정한다.
        // roundResolved 잠금으로, 버튼 클릭과 타임아웃이 같은 라운드에서 겹쳐도 한 번만 처리된다.
        private void ResolveRound(ItemAction? chosen)
        {
            if (roundResolved || currentIndex >= roundItems.Count)
            {
                return;
            }

            roundResolved = true;
            timerSchedule?.Pause();

            bool correct = chosen.HasValue && chosen.Value == roundItems[currentIndex].correctAction;
            Debug.Log(correct ? "맞음" : "틀림");

            if (correct)
            {
                correctCount++;
            }

            currentIndex++;
            ShowCurrentItem();
        }

        private void ShowResult()
        {
            int score = roundItems.Count > 0
                ? Mathf.RoundToInt((float)correctCount / roundItems.Count * 100f)
                : 0;

            if (finalScoreLabel == null || finalTimeLabel == null || resultPanel == null)
            {
                Debug.LogWarning("ItemSortGameController: 결과 화면 UI를 못 찾아서 결과를 표시하지 못함");
                return;
            }

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
