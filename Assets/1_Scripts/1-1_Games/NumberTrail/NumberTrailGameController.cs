using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 숫자 순서 연결하기 게임 (Trail Making Test Part A 변형, Tombaugh 2004 / Suzuki et al. 2022).
    // 1~N 숫자 버튼이 화면에 흩어져 있고, 1부터 순서대로 누른다. 맞게 누른 숫자는 초록색으로 바뀐다.
    // 다음 순서가 아닌 숫자를 누르면 그 버튼만 가볍게 흔들리고 같은 숫자부터 다시(실패 아님).
    // 제한시간 안에 N까지 다 누르면 클리어, 시간이 끝나면 실패. 한 판 = 한 번.
    // 난이도(1.00~10.00)로 기획서 표의 숫자 개수/제한시간을 보간해서 쓴다. 숫자가 많을수록 같은 영역에 촘촘해진다.
    // 클리어하면 다음 판이 조금 어려워지고, 시간 초과면 조금 쉬워진다. 세이브에 저장해서 다음에도 이어짐.
    [RequireComponent(typeof(UIDocument))]
    public class NumberTrailGameController : DifficultyGameController
    {
        // 기획서 3.1 표: 난이도 1~10의 (숫자 개수, 제한시간 초).
        private static readonly (int count, float seconds)[] Levels =
        {
            (8, 90f), (10, 100f), (12, 110f), (14, 130f), (16, 150f),
            (18, 170f), (20, 190f), (22, 210f), (24, 225f), (25, 240f),
        };

        // 한 판에 딱 한 번만 조정되는 게임이라(매트릭스와 같음) 라운드마다 조정하는 게임보다는 폭이 크다.
        // 완벽하게 수행한다고 가정하면(매 판 클리어) 정확히 45세션 만에 난이도 1->10.
        private const float RaiseStep = 0.20f;
        private const float LowerStep = 0.28f;

        // 오답(다음 순서가 아닌 숫자)을 누를 때마다 남은 시간에서 깎는 패널티.
        // 난이도가 쉬운 쪽(1.00)일 때 3초, 어려운 쪽(10.00)일 때 8초. 그 사이는 선형 보간.
        private const float WrongTapPenaltyEasy = 3f;
        private const float WrongTapPenaltyHard = 8f;
        private float WrongTapPenaltySeconds => Mathf.Lerp(WrongTapPenaltyEasy, WrongTapPenaltyHard, DifficultyT);

        // .number 크기와 같아야 한다. 태블릿 터치 영역 88dp(약 142) 이상.
        private const float ButtonSize = 142f;
        // 버튼끼리 최소 이만큼은 떨어뜨린다(배율 1 = 딱 붙는 정도). 자리가 안 나면 점점 줄여서라도 배치한다.
        private const float MinSpacingRatio = 1.15f;
        private const int PlacementAttemptsPerNumber = 300;
        // 시도를 다 써도 못 찾으면 최소 간격을 이 비율만큼 줄이고 다시 시도(완전히 못 찾는 상황 방지).
        private const float SpacingRelaxFactor = 0.92f;

        private const int ShakeStepMs = 50;
        private static readonly float[] ShakeOffsets = { -12f, 12f, -8f, 8f, -4f, 0f };
        private const float FeedbackSeconds = 1.2f;
        private const int MaxWaitFrames = 60;

        private Label progressLabel;
        private Label timerLabel;
        private VisualElement timerBarFill;
        private Label instructionLabel;
        private VisualElement field;
        private VisualElement resultPanel;
        private VisualElement quitConfirmPanel;
        private Label finalScoreLabel;
        private Label finalTimeLabel;

        private SaveData saveData;
        private Button[] numberButtons;
        private int numberCount;
        // 다음에 눌러야 할 숫자의 인덱스(0 = 숫자 1).
        private int nextIndex;
        private float timeRemaining;
        private float timerTotalSeconds;
        private bool resolved;
        private bool finished;
        private float startTime;
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
                Debug.LogWarning("NumberTrailGameController: UI가 끝내 준비되지 않아 초기화를 건너뜀");
                yield break;
            }

            progressLabel = root.Q<Label>("progress-label");
            timerLabel = root.Q<Label>("timer-label");
            timerBarFill = root.Q<VisualElement>("timer-bar-fill");
            instructionLabel = root.Q<Label>("instruction-label");
            field = root.Q<VisualElement>("number-field");
            resultPanel = root.Q<VisualElement>("result-panel");
            quitConfirmPanel = root.Q<VisualElement>("quit-confirm-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            finalTimeLabel = root.Q<Label>("final-time-label");

            root.Q<Button>("quit-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.RemoveFromClassList("hidden"); };
            root.Q<Button>("quit-confirm-button").clicked += () => { quitConfirmPanel.AddToClassList("hidden"); OnQuit(); };
            root.Q<Button>("quit-cancel-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.AddToClassList("hidden"); };
            root.Q<Button>("retry-button").clicked += OnRetry;
            root.Q<Button>("exit-button").clicked += OnExit;

            resultPanel.AddToClassList("hidden");
            quitConfirmPanel.AddToClassList("hidden");

            saveData = SaveManager.Load();
            LoadDifficulty(saveData.numberTrailDifficulty);

            // 숫자 배치는 영역 크기가 정해져야 할 수 있어서, 크기가 잡힌 뒤에 시작한다.
            if (field.contentRect.width > 0f && field.contentRect.height > 0f)
            {
                StartGame();
            }
            else
            {
                field.RegisterCallback<GeometryChangedEvent>(OnFirstLayout);
            }
        }

        // field 크기가 처음 잡히는 순간(레이아웃 완료) 한 번만 게임을 시작한다.
        private void OnFirstLayout(GeometryChangedEvent evt)
        {
            if (field.contentRect.width <= 0f || field.contentRect.height <= 0f)
            {
                return;
            }
            field.UnregisterCallback<GeometryChangedEvent>(OnFirstLayout);
            StartGame();
        }

        // 난이도 값 사이를 보간한 (숫자 개수, 제한시간). 예: 3.50이면 3단계와 4단계의 중간.
        private (int count, float seconds) CurrentLevel()
        {
            float position = difficulty - MinDifficulty;
            int low = Mathf.Clamp(Mathf.FloorToInt(position), 0, Levels.Length - 1);
            int high = Mathf.Min(low + 1, Levels.Length - 1);
            float t = position - low;
            return (Mathf.RoundToInt(Mathf.Lerp(Levels[low].count, Levels[high].count, t)),
                Mathf.Lerp(Levels[low].seconds, Levels[high].seconds, t));
        }

        // 이번 난이도의 숫자 개수/제한시간을 정하고, 버튼을 흩뿌려 배치한 뒤 타이머를 시작한다.
        private void StartGame()
        {
            var level = CurrentLevel();
            numberCount = level.count;
            nextIndex = 0;
            resolved = false;

            field.Clear();

            Vector2[] positions = PlaceNumbers(numberCount, field.contentRect.size);
            numberButtons = new Button[numberCount];
            for (int i = 0; i < numberCount; i++)
            {
                int index = i;
                var button = new Button { text = (i + 1).ToString() };
                button.AddToClassList("number");
                // 퍼센트로 두면 화면이 돌아가 영역 크기가 바뀌어도 비율대로 따라간다.
                button.style.left = Length.Percent(positions[i].x * 100f);
                button.style.top = Length.Percent(positions[i].y * 100f);
                button.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
                button.clicked += () => OnNumberClicked(index);
                numberButtons[i] = button;
                field.Add(button);
            }

            UpdateProgress();
            timerTotalSeconds = level.seconds;
            timeRemaining = level.seconds;
            UpdateTimerLabel();
            startTime = Time.time;
            timerSchedule?.Pause();
            timerSchedule = field.schedule.Execute(TickTimer).Every(100);
        }

        // 버튼 중심 위치들(영역 크기 대비 0~1 비율). 격자 없이 순수 무작위 좌표를 뽑아서, 이미 놓인
        // 버튼들과 너무 가까우면(최소 간격 미달) 다시 뽑는 방식(rejection sampling)으로 자연스럽게 흩어지게 한다.
        // 격자+흔들기 방식은 숫자가 많아지면 칸이 버튼 크기에 딱 맞아서 거의 못 흔들리고 줄 서 보이는 문제가 있었다.
        private static Vector2[] PlaceNumbers(int count, Vector2 size)
        {
            float half = ButtonSize * 0.5f;
            float minX = half;
            float maxX = Mathf.Max(half, size.x - half);
            float minY = half;
            float maxY = Mathf.Max(half, size.y - half);

            var pixelPositions = new System.Collections.Generic.List<Vector2>(count);
            float minSpacing = ButtonSize * MinSpacingRatio;

            for (int i = 0; i < count; i++)
            {
                // 완화해도 버튼 지름(ButtonSize) 밑으로는 절대 안 줄인다 — 그 밑이면 원이 겹친다.
                float currentMinSpacing = minSpacing;
                Vector2 bestCandidate = default;
                float bestDistance = -1f;
                bool placed = false;

                for (int attempt = 0; attempt < PlacementAttemptsPerNumber && !placed; attempt++)
                {
                    var candidate = new Vector2(Random.Range(minX, maxX), Random.Range(minY, maxY));
                    float distance = MinDistanceTo(candidate, pixelPositions);
                    if (distance > bestDistance)
                    {
                        bestDistance = distance;
                        bestCandidate = candidate;
                    }
                    if (distance >= currentMinSpacing)
                    {
                        placed = true;
                    }
                    else if ((attempt + 1) % 50 == 0)
                    {
                        currentMinSpacing = Mathf.Max(ButtonSize, currentMinSpacing * SpacingRelaxFactor);
                    }
                }

                // 끝까지 간격을 못 채웠으면, 시도 중 가장 여유 있었던 자리를 쓴다(완전 무작위보다 훨씬 안전).
                pixelPositions.Add(bestCandidate);
            }

            var positions = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                positions[i] = new Vector2(pixelPositions[i].x / size.x, pixelPositions[i].y / size.y);
            }
            return positions;
        }

        private static float MinDistanceTo(Vector2 candidate, System.Collections.Generic.List<Vector2> placed)
        {
            if (placed.Count == 0)
            {
                return float.MaxValue;
            }
            float min = float.MaxValue;
            foreach (Vector2 p in placed)
            {
                min = Mathf.Min(min, Vector2.Distance(candidate, p));
            }
            return min;
        }

        // 맞는 순서면 칠하고 다음 숫자로, 틀리면 흔들고 시간 패널티(패널티로 0 이하가 되면 바로 종료).
        private void OnNumberClicked(int index)
        {
            if (resolved || index < nextIndex)
            {
                return;
            }

            if (index != nextIndex)
            {
                SoundManager.Instance.PlaySFX("Wrong");
                Shake(numberButtons[index]);
                instructionLabel.text = "이 숫자가 아니에요, 다시 살펴보세요!";
                timeRemaining = Mathf.Max(0f, timeRemaining - WrongTapPenaltySeconds);
                UpdateTimerLabel();
                if (timeRemaining <= 0f)
                {
                    StartCoroutine(EndGame(false));
                }
                return;
            }

            SoundManager.Instance.PlaySFX("Correct");
            numberButtons[index].AddToClassList("done");
            nextIndex++;
            UpdateProgress();

            if (nextIndex >= numberCount)
            {
                StartCoroutine(EndGame(true));
            }
        }

        // 오답 안내: 버튼만 좌우로 가볍게 흔든다(가운데 맞춤용 translate는 그대로 두고 margin으로 흔듦).
        private static void Shake(Button button)
        {
            for (int i = 0; i < ShakeOffsets.Length; i++)
            {
                float offset = ShakeOffsets[i];
                button.schedule.Execute(() => button.style.marginLeft = offset).StartingIn(ShakeStepMs * i);
            }
        }


        private void UpdateProgress()
        {
            progressLabel.text = $"{nextIndex}개 / {numberCount}개";
            instructionLabel.text = nextIndex == 0
                ? "1부터 순서대로 눌러주세요"
                : $"잘하고 있어요! {nextIndex + 1}{EulReul(nextIndex + 1)} 찾아 눌러주세요";
        }

        // 숫자를 읽을 때(일,이,삼...) 마지막 음절에 받침이 있으면 "을", 없으면 "를".
        // 10, 20처럼 끝자리가 0이면 "십"으로 끝나 받침이 있다(영이 아니라 십).
        private static string EulReul(int number)
        {
            int lastDigit = number % 10;
            bool hasBatchim = lastDigit == 0 || lastDigit == 1 || lastDigit == 3
                || lastDigit == 6 || lastDigit == 7 || lastDigit == 8;
            return hasBatchim ? "을" : "를";
        }

        private void TickTimer()
        {
            if (resolved)
            {
                timerSchedule?.Pause();
                return;
            }

            timeRemaining -= 0.1f;
            UpdateTimerLabel();

            if (timeRemaining <= 0f)
            {
                StartCoroutine(EndGame(false));
            }
        }

        private void UpdateTimerLabel()
        {
            timerLabel.text = Mathf.Max(0f, timeRemaining).ToString("F1");
            if (timerTotalSeconds > 0f)
            {
                timerBarFill.style.width = Length.Percent(Mathf.Clamp01(timeRemaining / timerTotalSeconds) * 100f);
            }
        }

        // 난이도를 조정/저장하고 잠깐 격려 문구를 보여준 뒤 결과 화면으로.
        private IEnumerator EndGame(bool cleared)
        {
            if (resolved)
            {
                yield break;
            }
            resolved = true;
            timerSchedule?.Pause();

            instructionLabel.text = cleared ? "모두 연결했어요!" : "괜찮아요, 여기까지 잘했어요";
            AdjustDifficulty(cleared ? RaiseStep : -LowerStep);
            saveData.numberTrailDifficulty = difficulty;
            SaveManager.Save(saveData);
            Debug.Log($"숫자 {numberCount}개 중 {nextIndex}개: {(cleared ? "성공" : "시간 초과")}, 난이도 {difficulty:F2}");

            yield return new WaitForSeconds(FeedbackSeconds);
            ShowResult();
        }

        private void ShowResult()
        {
            finished = true;
            SoundManager.Instance.PlaySFX("End");
            int score = numberCount > 0 ? Mathf.RoundToInt((float)nextIndex / numberCount * 100f) : 0;
            finalScoreLabel.text = ResultMessage.FromScore(score);
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

        // ◀ 나가기: 진행을 멈추고 지금까지 기록으로 결과 화면(난이도는 건드리지 않음).
        private void OnQuit()
        {
            if (finished)
            {
                return;
            }
            StopAllCoroutines();
            resolved = true;
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
