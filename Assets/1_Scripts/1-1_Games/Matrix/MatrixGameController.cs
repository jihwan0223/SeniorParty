using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 매트릭스 순서 기억 게임(Corsi 블록 과제, Toril et al. 2016). 기획서의 난이도 변수(칸 수 2~5)를
    // 1.00~10.00 저장 난이도에서 뽑은 시작 레벨로 삼는다. 3x3 격자에서 N칸이 1초씩 순서대로 켜진 뒤,
    // 같은 순서로 누른다. 레벨당 시행 3회, 2회 이상 성공하면 다음 레벨(2회 성공하는 순간 통과).
    // 레벨당 3회를 다 썼는데 2회 이상 성공 못 하면(0회든 1회든) 그 자리에서 종료(최종 도달 레벨 기록).
    // 틀려도 목숨(하트) 같은 감점 표시 없이 긍정 문구만 보여준다(게임 4 표적 찾기와 같은 방식).
    // 화면에는 레벨을 1부터 보여준다(칸 2개 = 레벨 1).
    [RequireComponent(typeof(UIDocument))]
    public class MatrixGameController : DifficultyGameController
    {
        private const int CellCount = 9;
        private const int MinLevel = 2;
        private const int MaxLevel = 7;
        private const int TrialsPerLevel = 3;
        private const int SuccessesToPass = 2;

        // 자동 난이도: 다른 게임들처럼 회차(시행)마다 조정한다. 세션 하나(레벨 3개 × 최소 2회차 = 약 6회차)를
        // 다 성공하면 세션당 0.20씩 오르게 잡아서, 1.00에서 전부 성공만 한다고 가정하면 약 45세션 만에 10.00.
        private const float RaiseStep = 0.03f;
        private const float LowerStep = 0.04f;

        private const float BeforeSequenceSeconds = 1f;
        private const float LightSeconds = 1f;
        private const float FeedbackSeconds = 1.2f;
        private const int TapFlashMs = 400;
        // 방금 누른 칸을 손 떨림/더블탭으로 한 번 더 눌러 틀림 처리되는 것 방지(순서에 같은 칸은 두 번 안 나옴).
        private const float SameCellCooldownSeconds = 0.3f;
        // 점등될 때마다 피치를 이만큼씩 올려서(1.0부터 시작) 순서가 길어질수록 음이 올라가는 느낌을 준다.
        private const float LightPitchStep = 0.08f;
        private const int MaxWaitFrames = 60;
        private const float MaxGridSize = 840f;
        // 화면 전체 대비 격자가 차지할 최대 비율. 세로(Height)는 위쪽 텍스트 + 아래 여백까지 고려해서
        // 넉넉히 남긴다(텍스트 블록 전체를 .screen이 화면 중앙에 맞출 수 있게).
        private const float GridWidthRatio = 0.9f;
        private const float GridHeightRatio = 0.55f;

        private readonly Button[] cells = new Button[CellCount];
        private Label roundLabel;
        private Label instructionLabel;
        private VisualElement resultPanel;
        private VisualElement quitConfirmPanel;
        private Label finalScoreLabel;
        private VisualElement trialResultsContainer;

        // 한 판 안에서 시작 레벨보다 몇 단계까지 더 올라갈 수 있는지. 난이도가 낮아서 시작 레벨이 낮으면
        // 한 판 만에 최고 레벨(7)까지 쭉 가는 게 아니라 그보다 살짝 위까지만 가게 막는다.
        private const int SessionLevelHeadroom = 2;

        private SaveData saveData;
        private int startLevel;
        private int sessionMaxLevel;
        // 레벨 = 켜지는 칸 수(2~5). 화면에 보여줄 땐 DisplayLevel(1~4).
        private int level;
        // 이번 3회 중 몇 번째 시행인지, 그중 성공 수.
        private int trial;
        private int successes;
        // 결과 화면을 띄운 뒤엔 true(그만두기 중복 방지).
        private bool finished;
        private int[] sequence;
        private int inputIndex;
        // 누를 차례일 때만 true(점등 중이나 결과 보여주는 중에는 입력 무시).
        private bool accepting;
        private int lastTappedCell = -1;
        private float lastTapTime;
        // 이번 판에서 플레이한 레벨(같은 레벨 다시 하기 포함) 전부의 시행 기록: (레벨, 그 레벨에서 몇 번째
        // 시행인지, 성공/실패/중간에 나가서 모름). 결과창에 레벨별로 그대로 보여준다.
        private readonly List<(int Level, int TrialNumber, bool? Success)> trialResults = new();
        // 지금 막 시작했지만 아직 성공/실패가 기록 안 된 시행이 있는지(나가기 눌렀을 때 "?"로 남기기 위함).
        private bool trialPending;

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
                Debug.LogWarning("MatrixGameController: UI가 끝내 준비되지 않아 초기화를 건너뜀");
                yield break;
            }

            roundLabel = root.Q<Label>("round-label");
            instructionLabel = root.Q<Label>("instruction-label");
            resultPanel = root.Q<VisualElement>("result-panel");
            quitConfirmPanel = root.Q<VisualElement>("quit-confirm-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            trialResultsContainer = root.Q<VisualElement>("trial-results");

            for (int i = 0; i < CellCount; i++)
            {
                int cell = i;
                cells[i] = root.Q<Button>($"cell-{i}");
                cells[i].clicked += () => OnCellClicked(cell);
            }

            // 격자 크기는 화면 전체 크기에서 비율로 정한다(최대 MaxGridSize). grid-area가 남는 공간을
            // 전부 차지하게 하면(예전 방식) 텍스트+격자 전체를 화면 중앙에 맞출 수가 없어서,
            // 대신 화면 가로/세로에 비례한 크기로 직접 정하고 전체 덩어리를 .screen이 가운데로 맞춘다.
            var grid = root.Q<VisualElement>("grid");
            root.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float side = Mathf.Min(MaxGridSize, root.contentRect.width * GridWidthRatio, root.contentRect.height * GridHeightRatio);
                grid.style.width = side;
                grid.style.height = side;
            });

            root.Q<Button>("quit-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.RemoveFromClassList("hidden"); };
            root.Q<Button>("quit-confirm-button").clicked += () => { quitConfirmPanel.AddToClassList("hidden"); OnQuit(); };
            root.Q<Button>("quit-cancel-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.AddToClassList("hidden"); };
            root.Q<Button>("retry-button").clicked += OnRetry;
            root.Q<Button>("exit-button").clicked += OnExit;

            resultPanel.AddToClassList("hidden");
            quitConfirmPanel.AddToClassList("hidden");
            saveData = SaveManager.Load();
            LoadDifficulty(saveData.matrixDifficulty);
            startLevel = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(MinLevel, MaxLevel, DifficultyT)), MinLevel, MaxLevel);
            sessionMaxLevel = Mathf.Min(startLevel + SessionLevelHeadroom, MaxLevel);
            level = startLevel;
            trialResults.Clear();
            StartLevel();
            StartCoroutine(RunTrial());
        }

        // 레벨을 (새로 또는 다시) 시작할 때 이번 레벨의 시행 횟수만 초기화(전체 trialResults 기록은 유지).
        private void StartLevel()
        {
            trial = 0;
            successes = 0;
        }

        // 한 번의 시행: 새 순서로 점등 -> 격자 초기화 -> 입력 대기.
        private IEnumerator RunTrial()
        {
            accepting = false;
            trialPending = true;
            sequence = Enumerable.Range(0, CellCount).OrderBy(_ => Random.value).Take(level).ToArray();
            inputIndex = 0;
            lastTappedCell = -1;

            roundLabel.text = $"레벨 {DisplayLevel} · {trial + 1}회차 / {TrialsPerLevel}회";
            instructionLabel.text = "켜지는 순서를 잘 보세요";
            yield return new WaitForSeconds(BeforeSequenceSeconds);

            for (int i = 0; i < sequence.Length; i++)
            {
                int cell = sequence[i];
                SoundManager.Instance.PlaySFX("MatrixLightOn", 1f + i * LightPitchStep);
                cells[cell].AddToClassList("lit");
                yield return new WaitForSeconds(LightSeconds);
                cells[cell].RemoveFromClassList("lit");
            }

            instructionLabel.text = "같은 순서대로 눌러주세요";
            accepting = true;
        }

        // 누른 칸이 순서와 맞는지 바로 판정하고, 순서를 다 맞히면 시행 성공으로 끝낸다.
        private void OnCellClicked(int cell)
        {
            if (!accepting || (cell == lastTappedCell && Time.time - lastTapTime < SameCellCooldownSeconds))
            {
                return;
            }
            lastTappedCell = cell;
            lastTapTime = Time.time;

            // 순서·칸이 하나라도 다르면 그 자리에서 이번 시행 실패.
            if (cell != sequence[inputIndex])
            {
                SoundManager.Instance.PlaySFX("Wrong");
                Flash(cell, "wrong");
                accepting = false;
                StartCoroutine(EndTrial(false));
                return;
            }

            SoundManager.Instance.PlaySFX("Correct");
            Flash(cell, "correct");
            inputIndex++;
            if (inputIndex == sequence.Length)
            {
                accepting = false;
                StartCoroutine(EndTrial(true));
            }
        }

        private void Flash(int cell, string className)
        {
            cells[cell].AddToClassList(className);
            cells[cell].text = className == "correct" ? "O" : "X";
            cells[cell].schedule.Execute(() =>
            {
                cells[cell].RemoveFromClassList(className);
                cells[cell].text = "";
            }).StartingIn(TapFlashMs);
        }

        // 시행 결과를 보여주고 다음 시행 / 다음 레벨 / 같은 레벨 다시 / 종료를 정한다.
        private IEnumerator EndTrial(bool success)
        {
            trial++;
            trialResults.Add((level, trial, success));
            trialPending = false;
            if (success)
            {
                successes++;
            }
            // 회차마다 바로 조정(나가기로 중간에 끝난 시행은 EndTrial까지 안 와서 자동으로 제외됨).
            AdjustDifficulty(success ? RaiseStep : -LowerStep);
            saveData.matrixDifficulty = difficulty;
            SaveManager.Save(saveData);
            Debug.Log($"레벨 {DisplayLevel} {trial}번째: {(success ? "성공" : "실패")}, 난이도 {difficulty:F2}");

            bool passed = successes >= SuccessesToPass;
            // 3회를 다 했는데 2회 이상 성공 못 하면(0회든 1회든) 그 자리에서 게임 종료.
            bool levelFailed = !passed && trial == TrialsPerLevel;

            if (passed)
            {
                instructionLabel.text = level == sessionMaxLevel ? "모두 통과!" : "통과! 다음 레벨로";
            }
            else if (levelFailed)
            {
                instructionLabel.text = "수고했어요! 결과를 볼게요";
            }
            else
            {
                instructionLabel.text = success ? "잘했어요!" : "괜찮아요, 다시 해봐요!";
            }
            yield return new WaitForSeconds(FeedbackSeconds);

            if (levelFailed)
            {
                ShowResult(false);
                yield break;
            }

            if (passed)
            {
                if (level == sessionMaxLevel)
                {
                    ShowResult(true);
                    yield break;
                }
                level++;
                StartLevel();
            }
            StartCoroutine(RunTrial());
        }

        private int DisplayLevel => level - MinLevel + 1;

        // ◀ 나가기: 진행 중인 점등/피드백을 멈추고 지금 레벨로 결과 화면.
        private void OnQuit()
        {
            if (finished)
            {
                return;
            }

            StopAllCoroutines();
            accepting = false;
            foreach (var cell in cells)
            {
                cell.RemoveFromClassList("lit");
            }
            if (trialPending)
            {
                trialResults.Add((level, trial + 1, null));
                trialPending = false;
            }
            ShowResult(false);
        }

        // 최종 도달 레벨을 보여준다. 모두 통과하면 통과 문구.
        private void ShowResult(bool clearedAll)
        {
            finished = true;
            SoundManager.Instance.PlaySFX("End");
            Debug.Log($"최종 도달 레벨: {DisplayLevel}{(clearedAll ? " (모두 통과)" : "")}");
            // 끝까지 통과 = 최고, 시작 레벨보다 올라감 = 잘함, 시작 레벨을 못 넘음 = 수고(실패해도 긍정적으로).
            finalScoreLabel.text = clearedAll ? "최고예요!" : level > startLevel ? "잘했어요!" : "수고했어요!";
            // 이번 판에서 플레이한 레벨 전부(같은 레벨 다시 하기 포함)를 레벨별로 적어서 보여준다.
            var trialOutcomes = trialResults.Select(t => t.Success).ToList();
            var trialLabels = trialResults.Select(t => $"레벨 {t.Level - MinLevel + 1}\n{t.TrialNumber}회차").ToList();
            TrialResultsView.Build(trialResultsContainer, trialOutcomes, trialLabels);
            resultPanel.RemoveFromClassList("hidden");
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
