using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using SeniorParty.UI;

namespace SeniorParty
{
    // 도형 돌리기 게임(게임 9). 기획서 범위 밖의 게임.
    // 왼쪽 3x3 모양을 좌우반전/상하반전/왼쪽 90도/오른쪽 90도로 바꿔서 오른쪽 목표 모양과 같게 만든다.
    // 목표는 시작 모양에 조작을 몇 번 적용해 만들어서 항상 만들 수 있고, 최소 횟수는 BFS로 구한다.
    // 점수: 같은 모양이면 100점에서 최소보다 더 쓴 횟수마다 10점씩 깎음(최저 50점),
    //       다른 모양이면 겹친 칸 비율(겹친 색칠 칸 / 합친 색칠 칸) x 50점.
    // 초기화도 버튼을 누른 것으로 쳐서 횟수가 늘어난다(회전 횟수 = 사실상 버튼 누른 총 횟수).
    // 난이도 1.00~10.00 연속값(DifficultyGameController)으로 허용 클릭 수(최소 횟수 + 여유분)를 조절한다 —
    // 이 게임엔 타이머 대신 클릭 제한을 둔다. 여유분을 다 쓰면 그 자리에서 완료 처리된다.
    [RequireComponent(typeof(UIDocument))]
    public class RotateGridGameController : DifficultyGameController
    {
        private const int MinGridSize = 3;
        private const int MaxGridSize = 5;
        private const int OpCount = 4;
        // 시작 모양에 무작위 조작을 1~이 횟수만큼 적용해 목표를 만든다.
        // 4가지 조작(좌우반전/상하반전/90도 회전 2개)은 정사각형 대칭군(D4, 원소 8개)이라
        // 지름이 2 — 아무리 많이 섞어도 최소 횟수는 절대 2를 못 넘는다(격자 크기와 무관한 수학적 한계).
        // 그래서 난이도는 최소 횟수가 아니라 격자 크기(3x3~5x5)와 여유 클릭 수로 조절한다.
        private const int MaxScrambleMoves = 2;

        private const int PerfectScore = 100;
        private const int ExtraMovePenalty = 10;
        private const int MinMatchedScore = 50;
        private const int WrongMaxScore = 50;
        private const float ResultDelaySeconds = 2f;
        private const float AutoFinishDelaySeconds = 0.3f;
        private const int MaxWaitFrames = 60;

        // 최소 횟수에 더해줄 여유 클릭 수(쉬움 -> 어려움).
        private const int ClickBufferEasy = 6;
        private const int ClickBufferHard = 3;
        private const float RaiseStep = 0.2f;
        private const float LowerStep = 0.33f;

        private ShapeGridView myBoard;
        private ShapeGridView targetBoard;
        private Label moveLabel;
        private Label limitLabel;
        private Label instructionLabel;
        private VisualElement resultPanel;
        private VisualElement quitConfirmPanel;
        private Label finalScoreLabel;
        private Label finalDetailLabel;
        private ShapeGridView resultMyBoard;
        private ShapeGridView resultTargetBoard;
        private VisualElement opRow;
        private VisualElement controlsRow;

        private SaveData saveData;
        // 격자 크기(난이도에 따라 3~5). 모양은 gridSize*gridSize비트 마스크(칸 번호 = 행 * gridSize + 열, 왼쪽 위가 0번).
        private int gridSize;
        private int start;
        private int current;
        private int target;
        private int optimalMoves;
        private int moves;
        private int clickLimit;
        private bool finished;

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
                Debug.LogWarning("RotateGridGameController: UI가 끝내 준비되지 않아 초기화를 건너뜀");
                yield break;
            }

            myBoard = root.Q<ShapeGridView>("my-board");
            targetBoard = root.Q<ShapeGridView>("target-board");
            moveLabel = root.Q<Label>("move-label");
            limitLabel = root.Q<Label>("limit-label");
            instructionLabel = root.Q<Label>("instruction-label");
            resultPanel = root.Q<VisualElement>("result-panel");
            quitConfirmPanel = root.Q<VisualElement>("quit-confirm-panel");
            finalScoreLabel = root.Q<Label>("final-score-label");
            finalDetailLabel = root.Q<Label>("final-detail-label");
            resultMyBoard = root.Q<ShapeGridView>("result-my-board");
            resultTargetBoard = root.Q<ShapeGridView>("result-target-board");
            opRow = root.Q<VisualElement>("op-row");
            controlsRow = root.Q<VisualElement>("controls-row");

            root.Q<Button>("flip-h-button").clicked += () => OnOperation(0);
            root.Q<Button>("flip-v-button").clicked += () => OnOperation(1);
            root.Q<Button>("rotate-left-button").clicked += () => OnOperation(2);
            root.Q<Button>("rotate-right-button").clicked += () => OnOperation(3);
            root.Q<Button>("reset-button").clicked += OnReset;
            root.Q<Button>("done-button").clicked += OnDone;
            // ◀ 나가기: 게임 4처럼 지금 모양으로 결과 화면(완료와 같음).
            root.Q<Button>("quit-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.RemoveFromClassList("hidden"); };
            root.Q<Button>("quit-confirm-button").clicked += () => { quitConfirmPanel.AddToClassList("hidden"); OnDone(); };
            root.Q<Button>("quit-cancel-button").clicked += () => { SoundManager.Instance.PlaySFX("UIClick"); quitConfirmPanel.AddToClassList("hidden"); };
            root.Q<Button>("retry-button").clicked += OnRetry;
            root.Q<Button>("exit-button").clicked += OnExit;

            resultPanel.AddToClassList("hidden");
            quitConfirmPanel.AddToClassList("hidden");
            saveData = SaveManager.Load();
            LoadDifficulty(saveData.rotateGridDifficulty);
            NewPuzzle();
        }

        // 무작위 시작 모양과, 거기서 조작 몇 번으로 만든 (시작과 다른) 목표 모양을 정한다.
        private void NewPuzzle()
        {
            gridSize = Mathf.RoundToInt(Mathf.Lerp(MinGridSize, MaxGridSize, DifficultyT));
            int cellCount = gridSize * gridSize;
            int minFilled = Mathf.Max(3, Mathf.RoundToInt(cellCount * 0.33f));
            int maxFilled = Mathf.Max(minFilled + 1, Mathf.RoundToInt(cellCount * 0.45f));

            do
            {
                start = RandomConnectedShape(Random.Range(minFilled, maxFilled + 1), gridSize);
                target = start;
                int scrambleMoves = Random.Range(1, MaxScrambleMoves + 1);
                for (int i = 0; i < scrambleMoves; i++)
                {
                    target = Transform(target, Random.Range(0, OpCount), gridSize);
                }
            } while (target == start);

            optimalMoves = MinMoves(start, target, gridSize);
            int buffer = Mathf.RoundToInt(Mathf.Lerp(ClickBufferEasy, ClickBufferHard, DifficultyT));
            clickLimit = optimalMoves + Mathf.Max(1, buffer);
            Debug.Log($"격자 {gridSize}x{gridSize}, 목표까지 최소 {optimalMoves}번, 허용 클릭 {clickLimit}번");
            myBoard.Size = gridSize;
            targetBoard.Size = gridSize;
            targetBoard.Mask = target;
            // 설명 아래에 최소 횟수를 미리 알려준다(문제는 씬당 한 번만 만들어져서 덧붙여도 안 쌓인다).
            instructionLabel.text += $"\n{optimalMoves}번만 돌리면 만들 수 있어요";
            finished = false;
            current = start;
            moves = 0;
            Refresh();
        }

        // 서로 붙어 있는 칸 filled개로 된 모양(흩어진 모양보다 알아보기 쉽다).
        private static int RandomConnectedShape(int filled, int size)
        {
            int cellCount = size * size;
            int mask = 1 << Random.Range(0, cellCount);
            while (CountCells(mask) < filled)
            {
                int cell = Random.Range(0, cellCount);
                if ((mask & (1 << cell)) == 0 && HasFilledNeighbor(mask, cell, size))
                {
                    mask |= 1 << cell;
                }
            }
            return mask;
        }

        private static bool HasFilledNeighbor(int mask, int cell, int size)
        {
            int row = cell / size;
            int col = cell % size;
            return (row > 0 && (mask & (1 << (cell - size))) != 0)
                || (row < size - 1 && (mask & (1 << (cell + size))) != 0)
                || (col > 0 && (mask & (1 << (cell - 1))) != 0)
                || (col < size - 1 && (mask & (1 << (cell + 1))) != 0);
        }

        // 조작 op(0 좌우반전, 1 상하반전, 2 왼쪽 90도, 3 오른쪽 90도)를 모양에 적용.
        private int Transform(int mask, int op) => Transform(mask, op, gridSize);

        private static int Transform(int mask, int op, int size)
        {
            int result = 0;
            int cellCount = size * size;
            for (int i = 0; i < cellCount; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    continue;
                }
                int row = i / size;
                int col = i % size;
                int dest = op switch
                {
                    0 => row * size + (size - 1 - col),
                    1 => (size - 1 - row) * size + col,
                    2 => (size - 1 - col) * size + row,
                    _ => col * size + (size - 1 - row),
                };
                result |= 1 << dest;
            }
            return result;
        }

        // 시작 모양에서 목표 모양까지 가는 최소 조작 횟수(너비 우선 탐색).
        private static int MinMoves(int from, int to, int size)
        {
            var distance = new Dictionary<int, int> { [from] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int shape = queue.Dequeue();
                if (shape == to)
                {
                    return distance[shape];
                }
                for (int op = 0; op < OpCount; op++)
                {
                    int next = Transform(shape, op, size);
                    if (distance.ContainsKey(next))
                    {
                        continue;
                    }
                    distance[next] = distance[shape] + 1;
                    queue.Enqueue(next);
                }
            }
            return -1;
        }

        // 켜진 비트 개수(mask &= mask-1은 매번 가장 낮은 켜진 비트 하나를 끄는 트릭).
        private static int CountCells(int mask)
        {
            int count = 0;
            for (; mask != 0; mask &= mask - 1)
            {
                count++;
            }
            return count;
        }

        // 내 모양 보드와 회전 횟수/남은 클릭 수 라벨을 지금 상태로 다시 그린다.
        private void Refresh()
        {
            myBoard.Mask = current;
            moveLabel.text = $"회전 횟수: {moves}번";
            int remaining = Mathf.Max(0, clickLimit - moves);
            limitLabel.text = $"남은 클릭 횟수 : <color=#C82828>{remaining}</color>";
        }

        // 회전/반전 버튼 공통 처리: 모양을 바꾸고 클릭 수를 늘린 뒤 한도 초과 여부를 확인한다.
        private void OnOperation(int op)
        {
            if (finished)
            {
                return;
            }
            current = Transform(current, op);
            moves++;
            SoundManager.Instance.PlaySFX("RotateGridRotate");
            Refresh();
            CheckClickLimit();
        }

        // 초기화: 모양만 처음 상태로 되돌리고, 이것도 버튼을 누른 것이니 횟수는 오히려 늘어난다.
        private void OnReset()
        {
            if (finished)
            {
                return;
            }
            current = start;
            moves++;
            SoundManager.Instance.PlaySFX("RotateGridReset");
            Refresh();
            CheckClickLimit();
        }

        // 허용 클릭 수를 다 썼으면 더 조작 못 하고 그 자리에서 완료 처리(타이머 초과와 같은 역할).
        private void CheckClickLimit()
        {
            if (moves >= clickLimit)
            {
                // 방금 누른 회전/초기화 효과음이 바로 묻히지 않도록 완료 판정을 살짝 늦춘다.
                StartCoroutine(AutoFinishAfterDelay());
            }
        }

        private IEnumerator AutoFinishAfterDelay()
        {
            yield return new WaitForSeconds(AutoFinishDelaySeconds);
            OnDone();
        }

        // 완료/나가기 공통 처리: 지금 모양을 채점하고 난이도를 조정한 뒤 결과창을 띄운다.
        private void OnDone()
        {
            if (finished)
            {
                return;
            }
            finished = true;

            bool matched = current == target;
            int score = matched
                ? Mathf.Max(MinMatchedScore, PerfectScore - ExtraMovePenalty * (moves - optimalMoves))
                : Mathf.RoundToInt(WrongMaxScore * CountCells(current & target) / (float)CountCells(current | target));
            Debug.Log($"완료: {(matched ? "같은 모양" : "다른 모양")}, 최소 {optimalMoves}번 / 내 횟수 {moves}번, 점수 {score}");

            if (matched)
            {
                SoundManager.Instance.PlaySFX("Correct");
                instructionLabel.text = "잘했어요!";
                AdjustDifficulty(RaiseStep);
            }
            else
            {
                SoundManager.Instance.PlaySFX("Wrong");
                instructionLabel.text = "괜찮아요, 거의 다 왔어요!";
                AdjustDifficulty(-LowerStep);
            }
            saveData.rotateGridDifficulty = difficulty;
            SaveManager.Save(saveData);

            finalScoreLabel.text = ResultMessage.FromScore(score);
            finalDetailLabel.text = matched
                ? $"모양을 똑같이 만들었어요!\n{moves}번 돌려서 완성했어요"
                : $"거의 다 왔어요!\n{optimalMoves}번만 돌리면 만들 수 있어요";
            resultMyBoard.Size = gridSize;
            resultMyBoard.Mask = current;
            resultTargetBoard.Size = gridSize;
            resultTargetBoard.Mask = target;
            opRow.visible = false;
            controlsRow.visible = false;
            StartCoroutine(ShowResultAfterDelay());
        }

        // 완료 판정 직후 바로 결과창이 튀어나오지 않게 2초 멈췄다가 보여준다.
        private IEnumerator ShowResultAfterDelay()
        {
            yield return new WaitForSeconds(ResultDelaySeconds);
            SoundManager.Instance.PlaySFX("End");
            resultPanel.RemoveFromClassList("hidden");
        }

        // 다시하기: 난이도가 없는 게임이라 이 씬을 처음부터 다시 시작(새 문제).
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
