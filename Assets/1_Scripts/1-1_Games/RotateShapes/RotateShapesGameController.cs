using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using SeniorParty.UI;

namespace SeniorParty
{
    // 도형 회전 게임(멘탈 로테이션 과제, Shepard & Metzler 1971). 위에 보기 도형 1개, 아래에 선지 4개.
    // 선지 중 딱 1개만 보기와 같은 도형(회전만 다름)이고 나머지 3개는 다른 도형이다. 모든 도형은 회전해 있다.
    // 라운드마다 한 번만 고를 수 있고, 맞든 틀리든 정답을 잠깐 보여준 뒤 다음 라운드로 넘어간다.
    // 난이도 1.00~10.00 연속값(DifficultyGameController)으로 회전 각도 단위(90도 고정 -> 1도 단위 임의)와
    // 거울반전 오답 등장 확률(쉬움 0% -> 어려움 100%)을 함께 보간한다. 회전만으로는 원본과 절대 안 겹치는
    // 거울반전 도형이 가장 헷갈리는 오답이라 어려운 쪽에 몰아둔다. 라운드마다 정답/오답에 따라 조정.
    [RequireComponent(typeof(UIDocument))]
    public class RotateShapesGameController : DifficultyGameController
    {
        [SerializeField] private int roundCount = 8;

        private const int OptionCount = 4;
        // 난이도 1(쉬움): 90도 단위(0/90/180/270)만. 난이도 10(어려움): 15도 단위(90도처럼 딱 떨어지지
        // 않는 애매한 각도라 어렵지만, 그보다 더 촘촘하면 회전 차이가 눈에 안 보여서 의미가 없다).
        private const int AngleStepEasy = 90;
        private const int AngleStepHard = 15;
        // 오답 중 하나를 정답과 같은 도형의 거울반전으로 바꿀 확률(쉬움 0 -> 어려움 1).
        private const float MirrorChanceHard = 1f;
        // ShapeView.Shapes 순서(L,T,S,F,U,P,W,Y) 기준 좌우대칭 도형 — 거울반전해도 어떤 각도로 돌리면
        // 원본과 완전히 겹쳐서 오답 구실을 못 한다(운 나쁘면 정답과 똑같아 보임). 정답이 이 안에 있으면
        // 거울반전 오답은 만들지 않는다.
        private static readonly bool[] IsMirrorSymmetric = { false, true, true, false, true, false, true, false };

        // 완벽하게 수행한다고 가정하면(한 판 8라운드 전부 정답) 정확히 45세션 만에 난이도 1->10.
        private const float RaiseStep = 0.025f;
        private const float LowerStep = 0.042f;

        // 문항 하나당 제한시간(초). 기획서엔 이 게임의 난이도 변수로 시간이 없어서 고정값을 쓴다.
        private const float TimerSeconds = 20f;
        private const float FeedbackSeconds = 1.2f;
        private const int MaxWaitFrames = 60;

        private ShapeView referenceView;
        private readonly ShapeView[] optionViews = new ShapeView[OptionCount];
        private readonly Button[] optionButtons = new Button[OptionCount];
        private Label roundLabel;
        private Label timerLabel;
        private VisualElement timerBarFill;
        private VisualElement resultPanel;
        private VisualElement quitConfirmPanel;
        private Label finalScoreLabel;
        private VisualElement trialResultsContainer;

        private SaveData saveData;
        private int roundIndex;
        private int correctRounds;
        private int answerIndex;
        // 고른 뒤 결과를 보여주는 동안 또 눌리는 것 방지.
        private bool roundResolved;
        private bool finished;
        private float timeRemaining;
        // 라운드별 성공/실패(null = 나가기로 중간에 끝난 라운드). 결과창에 O/X로 보여준다.
        private readonly List<bool?> roundResults = new();
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
                Debug.LogWarning("RotateShapesGameController: UI가 끝내 준비되지 않아 초기화를 건너뜀");
                yield break;
            }

            roundLabel = root.Q<Label>("round-label");
            timerLabel = root.Q<Label>("timer-label");
            timerBarFill = root.Q<VisualElement>("timer-bar-fill");
            referenceView = root.Q<ShapeView>("reference-shape");
            resultPanel = root.Q<VisualElement>("result-panel");
            quitConfirmPanel = root.Q<VisualElement>("quit-confirm-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            trialResultsContainer = root.Q<VisualElement>("trial-results");

            for (int i = 0; i < OptionCount; i++)
            {
                int option = i;
                optionButtons[i] = root.Q<Button>($"option-{i}");
                optionViews[i] = root.Q<ShapeView>($"option-shape-{i}");
                optionButtons[i].clicked += () => OnOptionClicked(option);
            }

            root.Q<Button>("quit-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.RemoveFromClassList("hidden"); };
            root.Q<Button>("quit-confirm-button").clicked += () => { quitConfirmPanel.AddToClassList("hidden"); OnQuit(); };
            root.Q<Button>("quit-cancel-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.AddToClassList("hidden"); };
            root.Q<Button>("retry-button").clicked += OnRetry;
            root.Q<Button>("exit-button").clicked += OnExit;

            resultPanel.AddToClassList("hidden");
            quitConfirmPanel.AddToClassList("hidden");
            saveData = SaveManager.Load();
            LoadDifficulty(saveData.rotateShapesDifficulty);
            roundIndex = 0;
            correctRounds = 0;
            roundResults.Clear();
            StartRound();
        }

        // 새 라운드: 도형 4종을 뽑아 첫 번째를 보기/정답으로, 나머지 3개를 오답으로 쓴다. 정답 위치는 무작위.
        // 난이도가 높을수록 회전 각도가 더 촘촘해지고(90도 단위 -> 1도 단위), 오답 중 하나가 정답의 거울반전으로 바뀔 확률도 커진다.
        private void StartRound()
        {
            roundResolved = false;
            timerSchedule?.Pause();

            int angleStep = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(AngleStepEasy, AngleStepHard, DifficultyT)));
            int[] shapes = Enumerable.Range(0, ShapeView.ShapeCount)
                .OrderBy(_ => Random.value)
                .Take(OptionCount)
                .ToArray();
            // 쉬운 난이도(90도 단위)는 0 아닌 배수가 3개(90/180/270)뿐이라 5개(보기+선지4)를 겹치지 않게 못 뽑는다.
            // 그래서 후보 각도 중에서 복원추출로 뽑는다 — 각도가 겹쳐도 도형 자체가 달라서 구분엔 문제없다.
            float[] angleCandidates = Enumerable.Range(1, 360 / angleStep - 1)
                .Select(i => (float)(i * angleStep))
                .ToArray();
            float RandomAngle() => angleCandidates[Random.Range(0, angleCandidates.Length)];

            float referenceAngle = RandomAngle();
            referenceView.Set(shapes[0], referenceAngle);
            answerIndex = Random.Range(0, OptionCount);

            // 정답 선택지는 기준과 반드시 다른 각도로 뽑는다 — 안 그러면 회전 없이 그대로 보여서 "회전시켜서
            // 알아보기"라는 게임 목적 자체가 사라진다(오답들은 겹쳐도 상관없다).
            float[] angles = new float[OptionCount + 1];
            angles[0] = referenceAngle;
            for (int i = 0; i < OptionCount; i++)
            {
                float a = RandomAngle();
                if (i == answerIndex)
                {
                    while (a == referenceAngle && angleCandidates.Length > 1)
                    {
                        a = RandomAngle();
                    }
                }
                angles[i + 1] = a;
            }

            // 오답 슬롯 중 하나를 정답 도형의 거울반전으로 바꿀지 결정. 정답 도형이 좌우대칭이면
            // 거울반전이 회전만으로 원본과 겹쳐버려서 오답 구실을 못 하므로 건너뛴다.
            float mirrorChance = Mathf.Lerp(0f, MirrorChanceHard, DifficultyT);
            int mirrorSlot = !IsMirrorSymmetric[shapes[0]] && Random.value < mirrorChance
                ? Enumerable.Range(0, OptionCount).Where(i => i != answerIndex).OrderBy(_ => Random.value).First()
                : -1;

            int distractor = 1;
            for (int i = 0; i < OptionCount; i++)
            {
                bool isAnswer = i == answerIndex;
                int shape = isAnswer ? shapes[0] : (i == mirrorSlot ? shapes[0] : shapes[distractor++]);
                optionViews[i].Set(shape, angles[i + 1], mirrored: i == mirrorSlot);
                Debug.Log($"option {i}: shape={shape} angle={angles[i + 1]} answer={isAnswer} mirrored={i == mirrorSlot}");
                optionButtons[i].RemoveFromClassList("correct");
                optionButtons[i].RemoveFromClassList("wrong");
            }

            roundLabel.text = $"{roundIndex + 1}회차 / {roundCount}회";

            timeRemaining = TimerSeconds;
            UpdateTimerLabel();
            timerSchedule = referenceView.schedule.Execute(TickTimer).Every(100);
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
                roundResolved = true;
                optionButtons[answerIndex].AddToClassList("correct");
                Debug.Log($"라운드 {roundIndex + 1}: 시간 초과");
                StartCoroutine(EndRound(false));
            }
        }

        private void UpdateTimerLabel()
        {
            timerLabel.text = Mathf.Max(0f, timeRemaining).ToString("F1");
            timerBarFill.style.width = Length.Percent(Mathf.Clamp01(timeRemaining / TimerSeconds) * 100f);
        }

        // 선택지를 고르면 정답을 표시하고(오답이면 고른 것도 빨갛게) 라운드를 마감한다.
        private void OnOptionClicked(int option)
        {
            if (roundResolved)
            {
                return;
            }
            roundResolved = true;
            timerSchedule?.Pause();

            bool correct = option == answerIndex;
            optionButtons[answerIndex].AddToClassList("correct");
            if (!correct)
            {
                optionButtons[option].AddToClassList("wrong");
            }
            Debug.Log($"라운드 {roundIndex + 1}: {(correct ? "맞음" : "틀림")}");
            StartCoroutine(EndRound(correct));
        }

        // 난이도를 조정하고 잠깐 결과를 보여준 뒤 다음 라운드(또는 결과 화면)로.
        private IEnumerator EndRound(bool correct)
        {
            roundResults.Add(correct);
            if (correct)
            {
                SoundManager.Instance.PlaySFX("Correct");
                correctRounds++;
                AdjustDifficulty(RaiseStep);
            }
            else
            {
                SoundManager.Instance.PlaySFX("Wrong");
                AdjustDifficulty(-LowerStep);
            }
            saveData.rotateShapesDifficulty = difficulty;
            SaveManager.Save(saveData);

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

        // 다시하기: 이 씬을 처음부터 다시 시작.
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
