using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using SeniorParty.UI;

namespace SeniorParty
{
    // 매트릭스 순서 기억 게임(Corsi 블록 과제, Toril et al. 2016). 기획서 그대로, 난이도 없음.
    // 3x3 격자에서 N칸이 1초씩 순서대로 켜진 뒤, 같은 순서로 누른다.
    // 레벨(N) 2 -> 5. 레벨당 시행 3회, 2회 이상 성공하면 다음 레벨(2회 성공하는 순간 통과).
    // 같은 레벨 3회 모두 실패하면 종료(최종 도달 레벨 기록).
    // 기획서에 없는 경우: 3회 중 1회만 성공하면 그 레벨을 3회 다시.
    // 하트 3개 = 이번 3회 중 틀린 횟수(다 회색이면 종료). 화면에는 레벨을 1부터 보여준다(칸 2개 = 레벨 1).
    [RequireComponent(typeof(UIDocument))]
    public class MatrixGameController : MonoBehaviour
    {
        private const int CellCount = 9;
        private const int MinLevel = 2;
        private const int MaxLevel = 5;
        private const int TrialsPerLevel = 3;
        private const int SuccessesToPass = 2;

        private const float BeforeSequenceSeconds = 1f;
        private const float LightSeconds = 1f;
        private const float FeedbackSeconds = 1.2f;
        private const int TapFlashMs = 300;
        // 방금 누른 칸을 손 떨림/더블탭으로 한 번 더 눌러 틀림 처리되는 것 방지(순서에 같은 칸은 두 번 안 나옴).
        private const float SameCellCooldownSeconds = 0.3f;
        private const int MaxWaitFrames = 60;

        private readonly Button[] cells = new Button[CellCount];
        private HeartView[] hearts;
        private Label levelLabel;
        private Label trialLabel;
        private Label instructionLabel;
        private VisualElement resultPanel;
        private Label finalScoreLabel;
        private Label finalTimeLabel;

        // 레벨 = 켜지는 칸 수(2~5). 화면에 보여줄 땐 DisplayLevel(1~4).
        private int level;
        // 이번 3회 중 몇 번째 시행인지, 그중 성공/실패 수.
        private int trial;
        private int successes;
        private int failures;
        // 결과 화면을 띄운 뒤엔 true(그만두기 중복 방지).
        private bool finished;
        private int[] sequence;
        private int inputIndex;
        // 누를 차례일 때만 true(점등 중이나 결과 보여주는 중에는 입력 무시).
        private bool accepting;
        private int lastTappedCell = -1;
        private float lastTapTime;
        private float startTime;

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

            levelLabel = root.Q<Label>("level-label");
            trialLabel = root.Q<Label>("trial-label");
            instructionLabel = root.Q<Label>("instruction-label");
            resultPanel = root.Q<VisualElement>("result-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            finalTimeLabel = root.Q<Label>("final-time-label");

            for (int i = 0; i < CellCount; i++)
            {
                int cell = i;
                cells[i] = root.Q<Button>($"cell-{i}");
                cells[i].clicked += () => OnCellClicked(cell);
            }
            hearts = root.Query<HeartView>().ToList().ToArray();
            root.Q<Button>("quit-button").clicked += OnQuit;
            root.Q<Button>("retry-button").clicked += OnRetry;
            root.Q<Button>("exit-button").clicked += OnExit;

            resultPanel.AddToClassList("hidden");
            level = MinLevel;
            StartLevel();
            startTime = Time.time;
            StartCoroutine(RunTrial());
        }

        // 레벨을 (새로 또는 다시) 시작할 때 시행 기록과 하트를 초기화.
        private void StartLevel()
        {
            trial = 0;
            successes = 0;
            failures = 0;
            UpdateHearts();
        }

        // 오른쪽 하트부터 실패 횟수만큼 회색.
        private void UpdateHearts()
        {
            for (int i = 0; i < hearts.Length; i++)
            {
                hearts[i].Lost = i >= hearts.Length - failures;
            }
        }

        // 한 번의 시행: 새 순서로 점등 -> 격자 초기화 -> 입력 대기.
        private IEnumerator RunTrial()
        {
            accepting = false;
            sequence = Enumerable.Range(0, CellCount).OrderBy(_ => Random.value).Take(level).ToArray();
            inputIndex = 0;
            lastTappedCell = -1;

            levelLabel.text = $"레벨 {DisplayLevel}";
            trialLabel.text = $"{TrialsPerLevel}번 중 {trial + 1}번째";
            instructionLabel.text = "켜지는 순서를 잘 보세요";
            yield return new WaitForSeconds(BeforeSequenceSeconds);

            foreach (int cell in sequence)
            {
                cells[cell].AddToClassList("lit");
                yield return new WaitForSeconds(LightSeconds);
                cells[cell].RemoveFromClassList("lit");
            }

            instructionLabel.text = "같은 순서대로 눌러주세요";
            accepting = true;
        }

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
                Flash(cell, "wrong");
                accepting = false;
                StartCoroutine(EndTrial(false));
                return;
            }

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
            cells[cell].schedule.Execute(() => cells[cell].RemoveFromClassList(className)).StartingIn(TapFlashMs);
        }

        // 시행 결과를 보여주고 다음 시행 / 다음 레벨 / 같은 레벨 다시 / 종료를 정한다.
        private IEnumerator EndTrial(bool success)
        {
            trial++;
            if (success)
            {
                successes++;
            }
            else
            {
                failures++;
                UpdateHearts();
            }
            Debug.Log($"레벨 {DisplayLevel} {trial}번째: {(success ? "성공" : "실패")}");

            bool passed = successes >= SuccessesToPass;
            bool allFailed = failures >= TrialsPerLevel;
            // 3회를 다 했는데 1회만 성공: 통과도 종료도 아니라 같은 레벨을 다시.
            bool retryLevel = !passed && !allFailed && trial == TrialsPerLevel;

            if (passed)
            {
                instructionLabel.text = level == MaxLevel ? "모두 통과!" : "통과! 다음 레벨로";
            }
            else if (retryLevel)
            {
                instructionLabel.text = "이 레벨을 한 번 더 해볼게요";
            }
            else
            {
                instructionLabel.text = success ? "잘했어요!" : "아쉬워요!";
            }
            yield return new WaitForSeconds(FeedbackSeconds);

            if (allFailed)
            {
                ShowResult(false);
                yield break;
            }

            if (passed)
            {
                if (level == MaxLevel)
                {
                    ShowResult(true);
                    yield break;
                }
                level++;
                StartLevel();
            }
            else if (retryLevel)
            {
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
            ShowResult(false);
        }

        // 최종 도달 레벨을 보여준다. 모두 통과하면 통과 문구.
        private void ShowResult(bool clearedAll)
        {
            finished = true;
            Debug.Log($"최종 도달 레벨: {DisplayLevel}{(clearedAll ? " (모두 통과)" : "")}");
            finalScoreLabel.text = clearedAll ? "모든 레벨 통과!" : $"최종 도달 레벨: {DisplayLevel}";
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

        // 다시하기: 난이도가 없는 게임이라 이 씬을 처음부터 다시 시작.
        private void OnRetry()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        // 나가기: 게임 시작 누르면 나오는 게임 탭 화면으로 이동
        private void OnExit()
        {
            MainMenuController.PendingReturnTarget = MainMenuController.ReturnTarget.GameTabs;
            SceneManager.LoadScene("MainScene");
        }
    }
}
