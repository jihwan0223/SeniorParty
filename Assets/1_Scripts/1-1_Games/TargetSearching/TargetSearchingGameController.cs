using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using SeniorParty.UI;

namespace SeniorParty
{
    // 표적 찾기 게임 (Belchior et al. 2013, UFOV 훈련 기반 선택적 주의력 과제).
    // 라운드 시작 때 표적(예: "빨간 별")을 보여주고, 흩어져 있는 자극 중 표적만 제한시간 안에 모두 터치한다.
    // 표적이 아닌 걸 누르거나 시간이 끝나면 그 라운드만 실패(긍정 문구), 게임은 계속 진행. 표적을 다 찾으면 클리어.
    // 다른 난이도 게임들과 같은 1.00~10.00 연속값(DifficultyGameController)으로 자극 개수(8~20)와 표적 개수(3~5),
    // 제한시간(15~30초), 표적과 닮은 방해자극 수(1~6)를 함께 보간한다. 방해자극은 세 종류: 색만 같은 것(모양 다름),
    // 모양만 같은 것(색 다름), 둘 다 다른 것. 난이도가 오를수록 닮은 방해자극 비중이 커진다.
    // 연속 2라운드 클리어하면 올리고, 실패하면 바로 내린다. 세이브에 저장해서 다음에도 이어짐.
    [RequireComponent(typeof(UIDocument))]
    public class TargetSearchingGameController : DifficultyGameController
    {
        [SerializeField] private int roundCount = 5;

        // 난이도 1(쉬움)~10(어려움) 양 끝의 자극 구성. 라운드마다 DifficultyT로 사이 값을 보간해서 쓴다.
        private const int CountEasy = 8;
        private const int CountHard = 20;
        private const int TargetsEasy = 3;
        private const int TargetsHard = 5;
        private const float SecondsEasy = 15f;
        private const float SecondsHard = 30f;
        private const int SimilarEachEasy = 1;
        private const int SimilarEachHard = 6;

        // 2라운드 연속 클리어하면 올리고(느리게), 한 번 실패하면 바로 내린다(빠르게) — 기존 4단계 방식과 같은 비대칭.
        // 완벽하게 수행한다고 가정하면(한 판 5라운드 전부 클리어, 평균 2.5회/판 승급) 약 45세션 만에 난이도 1->10.
        private const float RaiseStep = 0.08f;
        private const float LowerStep = 0.12f;
        private const int ClearsToPromote = 2;
        private int clearStreak;

        // 지금 난이도(DifficultyT)에서 뽑은 이번 라운드 자극 구성.
        private (int count, int targets, float seconds, int similarEach) CurrentLevel()
        {
            int count = Mathf.RoundToInt(Mathf.Lerp(CountEasy, CountHard, DifficultyT));
            int targets = Mathf.RoundToInt(Mathf.Lerp(TargetsEasy, TargetsHard, DifficultyT));
            float seconds = Mathf.Lerp(SecondsEasy, SecondsHard, DifficultyT);
            int similarEach = Mathf.RoundToInt(Mathf.Lerp(SimilarEachEasy, SimilarEachHard, DifficultyT));
            int distractorCount = count - targets;
            similarEach = Mathf.Clamp(similarEach, 0, distractorCount / 2);
            return (count, targets, seconds, similarEach);
        }

        private static readonly string[] ColorNames = { "빨간", "파란", "노란", "초록" };
        private static readonly string[] ShapeNames = { "원", "네모", "세모", "별" };

        // 자극이 흩어지는 영역(TargetSearching.uss의 stimulus-field 크기와 같아야 함)과 자극 기본 크기(.stimulus 크기).
        private const float FieldWidth = 1000f;
        private const float FieldHeight = 640f;
        private const float StimulusSize = 142f;
        // 도형이 전부 똑같은 방향으로만 놓이지 않게 완전히 무작위로 돌린다.
        private const float MaxRotationDeg = 180f;
        // 회전처럼 크기도 무작위로 살짝 다르게 그려서 한눈에 훑어보기 어렵게 한다.
        // 터치 영역(버튼 크기)도 그려지는 도형 크기에 맞춰 같이 작아진다.
        private const float MinSizeScale = 0.65f;
        private const float MaxSizeScale = 1f;
        // 격자 없이 순수 무작위 배치(rejection sampling). 숫자 잇기(NumberTrail)와 같은 방식 —
        // 자극끼리 최소 간격(반지름 합의 배율) 이상 떨어졌을 때만 배치하고, 자리가 안 나면 간격을 줄여 재시도한다.
        private const float MinSpacingRatio = 1.1f;
        private const int PlacementAttemptsPerStimulus = 300;
        private const float SpacingRelaxFactor = 0.92f;

        private const float FeedbackSeconds = 1.2f;
        private const int MaxWaitFrames = 60;

        private Label roundLabel;
        private Label timerLabel;
        private VisualElement timerBarFill;
        private Label instructionLabel;
        private VisualElement field;
        private VisualElement resultPanel;
        private VisualElement quitConfirmPanel;
        private Label finalScoreLabel;
        private VisualElement trialResultsContainer;

        private SaveData saveData;

        private bool[] isTarget;
        private bool[] found;
        private string targetName;
        private int remainingToFind;

        private int roundIndex;
        private int correctRounds;
        private float timeRemaining;
        private float timerTotalSeconds;
        private bool roundResolved;
        private bool finished;
        private IVisualElementScheduledItem timerSchedule;
        // 라운드별 성공/실패(null = 나가기로 중간에 끝난 라운드). 결과창에 O/X로 보여준다.
        private readonly List<bool?> roundResults = new();

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
                Debug.LogWarning("TargetSearchingGameController: UI가 끝내 준비되지 않아 초기화를 건너뜀");
                yield break;
            }

            roundLabel = root.Q<Label>("round-label");
            timerLabel = root.Q<Label>("timer-label");
            timerBarFill = root.Q<VisualElement>("timer-bar-fill");
            instructionLabel = root.Q<Label>("instruction-label");
            field = root.Q<VisualElement>("stimulus-field");
            resultPanel = root.Q<VisualElement>("result-panel");
            quitConfirmPanel = root.Q<VisualElement>("quit-confirm-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            trialResultsContainer = root.Q<VisualElement>("trial-results");

            root.Q<Button>("quit-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.RemoveFromClassList("hidden"); };
            root.Q<Button>("quit-confirm-button").clicked += () => { quitConfirmPanel.AddToClassList("hidden"); OnQuit(); };
            root.Q<Button>("quit-cancel-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.AddToClassList("hidden"); };
            root.Q<Button>("retry-button").clicked += OnRetry;
            root.Q<Button>("exit-button").clicked += OnExit;

            resultPanel.AddToClassList("hidden");
            quitConfirmPanel.AddToClassList("hidden");

            saveData = SaveManager.Load();
            LoadDifficulty(saveData.targetSearchingDifficulty);
            clearStreak = 0;

            roundIndex = 0;
            correctRounds = 0;
            roundResults.Clear();
            StartRound();
        }

        // 새 라운드: 표적을 정하고, 표적 + 방해자극을 섞어서 영역에 흩어 놓고 타이머를 시작한다.
        private void StartRound()
        {
            roundResolved = false;
            var level = CurrentLevel();

            int targetColor = Random.Range(0, StimulusView.ColorCount);
            int targetShape = Random.Range(0, StimulusView.ShapeCount);
            targetName = $"{ColorNames[targetColor]} {ShapeNames[targetShape]}";

            var stimuli = new List<(int color, int shape, bool target)>();
            for (int i = 0; i < level.targets; i++)
            {
                stimuli.Add((targetColor, targetShape, true));
            }
            int distractorCount = level.count - level.targets;
            for (int i = 0; i < distractorCount; i++)
            {
                // 앞쪽부터 색만 같은 것, 모양만 같은 것, 둘 다 다른 것 순서로 채운다.
                (int color, int shape) distractor;
                if (i < level.similarEach)
                {
                    distractor = (targetColor, OtherThan(targetShape, StimulusView.ShapeCount));
                }
                else if (i < level.similarEach * 2)
                {
                    distractor = (OtherThan(targetColor, StimulusView.ColorCount), targetShape);
                }
                else
                {
                    distractor = (OtherThan(targetColor, StimulusView.ColorCount),
                        OtherThan(targetShape, StimulusView.ShapeCount));
                }
                stimuli.Add((distractor.color, distractor.shape, false));
            }

            isTarget = stimuli.Select(s => s.target).ToArray();
            found = new bool[stimuli.Count];
            remainingToFind = level.targets;

            // 각 자극 크기를 먼저 정하고(표적부터 순서대로), 격자 없이 무작위 좌표 중 서로 안 겹치는
            // 자리를 뽑아 배치한다(숫자 잇기와 같은 rejection sampling 방식). 표적을 먼저 배치하기 때문에
            // 그 시점엔 다른 표적들만 있어서 자연스럽게 서로 멀리 떨어진다.
            var scales = new float[stimuli.Count];
            var rotations = new float[stimuli.Count];
            for (int i = 0; i < stimuli.Count; i++)
            {
                scales[i] = Random.Range(MinSizeScale, MaxSizeScale);
                rotations[i] = Random.Range(-MaxRotationDeg, MaxRotationDeg);
            }
            Vector2[] centers = PlaceStimuli(scales, new Vector2(FieldWidth, FieldHeight));

            field.Clear();
            for (int i = 0; i < stimuli.Count; i++)
            {
                float size = StimulusSize * scales[i];
                var view = new StimulusView { pickingMode = PickingMode.Ignore };
                view.AddToClassList("stimulus-shape");
                view.Set(stimuli[i].color, stimuli[i].shape, rotations[i], scales[i]);

                var button = new Button();
                button.AddToClassList("stimulus");
                button.style.width = size;
                button.style.height = size;
                button.style.left = centers[i].x - size * 0.5f;
                button.style.top = centers[i].y - size * 0.5f;
                button.Add(view);
                int index = i;
                button.clicked += () => OnStimulusClicked(index, button);
                field.Add(button);
            }

            roundLabel.text = $"{roundIndex + 1}회차 / {roundCount}회";
            UpdateInstruction();

            timerTotalSeconds = level.seconds;
            timeRemaining = level.seconds;
            UpdateTimerLabel();
            timerSchedule?.Pause();
            timerSchedule = field.schedule.Execute(TickTimer).Every(100);
        }

        // 자극 중심 좌표들을 순수 무작위로 뽑는다(격자 없음). scales[i]로 그 자극의 반지름을 구해서,
        // 이미 놓인 자극들과 반지름 합(x spacingFactor) 이상 떨어졌을 때만 배치하고, 자리가 안 나면
        // 간격 배율을 점점 줄여서라도(겹치지만 않는 선까지) 배치한다(숫자 잇기 PlaceNumbers와 같은 알고리즘).
        private static Vector2[] PlaceStimuli(float[] scales, Vector2 fieldSize)
        {
            int count = scales.Length;
            var centers = new Vector2[count];
            var placedCenters = new System.Collections.Generic.List<Vector2>(count);
            var placedRadii = new System.Collections.Generic.List<float>(count);

            for (int i = 0; i < count; i++)
            {
                float radius = StimulusSize * scales[i] * 0.5f;
                float minX = radius;
                float maxX = Mathf.Max(radius, fieldSize.x - radius);
                float minY = radius;
                float maxY = Mathf.Max(radius, fieldSize.y - radius);

                // 완화해도 반지름 합(스쳐 닿는 정도) 밑으로는 절대 안 줄인다 — 그 밑이면 도형이 겹친다.
                float spacingFactor = MinSpacingRatio;
                Vector2 bestCandidate = default;
                float bestRawGap = float.NegativeInfinity;
                bool placed = false;

                for (int attempt = 0; attempt < PlacementAttemptsPerStimulus && !placed; attempt++)
                {
                    var candidate = new Vector2(Random.Range(minX, maxX), Random.Range(minY, maxY));
                    float rawGap = MinGap(candidate, radius, placedCenters, placedRadii, 1f);
                    if (rawGap > bestRawGap)
                    {
                        bestRawGap = rawGap;
                        bestCandidate = candidate;
                    }
                    if (MinGap(candidate, radius, placedCenters, placedRadii, spacingFactor) >= 0f)
                    {
                        placed = true;
                        bestCandidate = candidate;
                    }
                    else if ((attempt + 1) % 50 == 0)
                    {
                        spacingFactor = Mathf.Max(1f, spacingFactor * SpacingRelaxFactor);
                    }
                }

                centers[i] = bestCandidate;
                placedCenters.Add(bestCandidate);
                placedRadii.Add(radius);
            }
            return centers;
        }

        // candidate가 이미 놓인 자극들과 얼마나 여유 있게 떨어져 있는지(최소값, 요구 간격 기준).
        // 0 이상이면 간격 조건을 만족. spacingFactor 1 = 반지름 합(스쳐 닿는 정도)까지만 요구.
        private static float MinGap(Vector2 candidate, float radius,
            System.Collections.Generic.List<Vector2> placedCenters, System.Collections.Generic.List<float> placedRadii,
            float spacingFactor)
        {
            if (placedCenters.Count == 0)
            {
                return float.MaxValue;
            }
            float min = float.MaxValue;
            for (int j = 0; j < placedCenters.Count; j++)
            {
                float required = (radius + placedRadii[j]) * spacingFactor;
                float gap = Vector2.Distance(candidate, placedCenters[j]) - required;
                min = Mathf.Min(min, gap);
            }
            return min;
        }

        private static int OtherThan(int value, int count)
        {
            return (value + Random.Range(1, count)) % count;
        }

        private void UpdateInstruction()
        {
            instructionLabel.text = $"'{targetName}' {remainingToFind}개를 더 찾아 터치하세요";
        }

        // 표적을 누르면 찾음 처리(전부 찾으면 라운드 클리어), 표적이 아니면 바로 라운드 실패.
        private void OnStimulusClicked(int index, Button button)
        {
            if (roundResolved || found[index])
            {
                return;
            }

            // 표적이 아닌 걸 누르면 즉시 실패.
            if (!isTarget[index])
            {
                button.AddToClassList("wrong");
                StartCoroutine(EndRound(false));
                return;
            }

            SoundManager.Instance.PlaySFX("Correct");
            found[index] = true;
            button.AddToClassList("found");
            remainingToFind--;

            if (remainingToFind > 0)
            {
                UpdateInstruction();
                return;
            }

            StartCoroutine(EndRound(true));
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
                StartCoroutine(EndRound(false));
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

        // 라운드 결과를 잠깐 보여주고 다음 라운드로(또는 마지막이면 결과 화면으로).
        private IEnumerator EndRound(bool cleared)
        {
            if (roundResolved)
            {
                yield break;
            }
            roundResolved = true;
            timerSchedule?.Pause();
            roundResults.Add(cleared);

            if (cleared)
            {
                correctRounds++;
                instructionLabel.text = "잘했어요!";
                clearStreak++;
                if (clearStreak >= ClearsToPromote)
                {
                    clearStreak = 0;
                    AdjustDifficulty(RaiseStep);
                }
            }
            else
            {
                SoundManager.Instance.PlaySFX("Wrong");
                instructionLabel.text = "괜찮아요, 다음 회차로 가볼게요";
                clearStreak = 0;
                AdjustDifficulty(-LowerStep);
            }
            saveData.targetSearchingDifficulty = difficulty;
            SaveManager.Save(saveData);
            Debug.Log($"라운드 {roundIndex + 1}: {(cleared ? "성공" : "실패")}, 난이도 {difficulty:F2}");

            yield return new WaitForSeconds(FeedbackSeconds);

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
