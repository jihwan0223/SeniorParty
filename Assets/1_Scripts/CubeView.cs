using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeniorParty.UI
{
    // 작은 조각 3x3x3 = 27개로 된 큐브를 45도 돌려서(윗면/왼쪽면/오른쪽면이 보이게) 그린다.
    // 앞 조각을 지우면 그 뒤 조각이 보인다. Interactive일 때 누르면 보이는 맨 앞 조각을 지운다(되돌리기 없음).
    // 조각 인덱스 = x + 3*y + 9*z (x: 오른쪽 아래, y: 위, z: 왼쪽 아래 방향).
    [UxmlElement]
    public partial class CubeView : VisualElement
    {
        public const int GridSize = 3;
        public const int PieceCount = GridSize * GridSize * GridSize;

        private const float Sqrt3Half = 0.8660254f;
        // 요소 크기 대비 큐브가 차지하는 비율(테두리 선이 잘리지 않게 여백).
        private const float FitRatio = 0.92f;
        private const float LineWidth = 3f;
        // 가장 깊숙한 조각의 밝기(맨 앞 조각 = 1). 파인 곳이 더 어둡게 보여서 깊이가 읽힌다.
        private const float DeepestShade = 0.6f;

        // 한 변 길이 1일 때 3D 축이 화면에서 향하는 방향.
        private static readonly Vector2 AxisX = new Vector2(Sqrt3Half, 0.5f);
        private static readonly Vector2 AxisY = new Vector2(0f, -1f);
        private static readonly Vector2 AxisZ = new Vector2(-Sqrt3Half, 0.5f);

        // 면 색: 윗면 밝게, 왼쪽 중간, 오른쪽 어둡게 해서 입체감.
        private static readonly Color[] FaceColors =
        {
            new Color(0.93f, 0.88f, 0.78f),
            new Color(0.80f, 0.72f, 0.58f),
            new Color(0.66f, 0.58f, 0.46f),
        };
        private static readonly Color LineColor = new Color(0.15f, 0.15f, 0.15f);
        // 누르고 있는 조각 강조색(면 색에 섞음).
        private static readonly Color PressedTint = new Color(0.95f, 0.45f, 0.25f);
        private const float PressedTintAmount = 0.6f;
        // 방금 지운 뒤 이 시간 안에 다시 누른 건 무시(더블클릭/손 떨림으로 뒤 조각까지 지워지는 것 방지).
        private const long RemoveCooldownMs = 300;
        private const int NoPointer = -1;

        // 뒤에서 앞 순서(x+y+z 작은 것부터). 이 순서로 그리면 앞 조각이 뒤 조각을 덮는다.
        private static readonly int[] DrawOrder =
            Enumerable.Range(0, PieceCount).OrderBy(Depth).ToArray();

        private readonly bool[] removed = new bool[PieceCount];

        // 지금 누르고 있는 조각/포인터. 한 번에 포인터 하나만 받는다.
        private int pressedPiece = -1;
        private int pressedPointerId = NoPointer;
        // 누른 채로 아직 그 조각 위에 있는지(벗어나면 강조 끄고, 떼도 안 지움).
        private bool pressInside;
        private long lastRemoveTime = -RemoveCooldownMs;

        // true면 조각을 눌러서 지울 수 있다.
        public bool Interactive { get; set; }

        public CubeView()
        {
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => CancelPress());
        }

        public bool[] GetRemoved() => (bool[])removed.Clone();

        public void SetRemoved(bool[] source)
        {
            System.Array.Copy(source, removed, PieceCount);
            MarkDirtyRepaint();
        }

        public void ClearRemoved()
        {
            CancelPress();
            System.Array.Clear(removed, 0, PieceCount);
            MarkDirtyRepaint();
        }

        // 지울 조각들의 배치: 붙어있게(한 덩어리로 기억돼서 쉬움) / 상관없이 / 흩어지게(따로따로 기억해야 해서 어려움).
        public enum Spread { Clustered, Mixed, Scattered }

        // 실제로 깎아서 만들 수 있는 모양이 되도록, 그때그때 보이는 조각 중에서 count개를 하나씩 지운다.
        // (안 보이는 조각이 지워져 있으면 플레이어가 알 방법이 없으니까)
        // maxLayer: 0 겉면 조각만, 1 한 겹 안쪽까지, 2 제한 없음.
        // spread 조건에 맞는 후보가 없으면 조건 없이 고른다.
        public static bool[] PickRemoved(int count, int maxLayer, Spread spread)
        {
            var result = new bool[PieceCount];
            for (int n = 0; n < count; n++)
            {
                var candidates = Enumerable.Range(0, PieceCount)
                    .Where(i => !result[i] && Layer(i) <= maxLayer && IsVisible(result, i))
                    .ToList();

                if (n > 0 && spread != Spread.Mixed)
                {
                    bool wantTouching = spread == Spread.Clustered;
                    var preferred = candidates.Where(i => TouchesRemoved(result, i) == wantTouching).ToList();
                    if (preferred.Count > 0)
                    {
                        candidates = preferred;
                    }
                }

                if (candidates.Count == 0)
                {
                    break;
                }
                result[candidates[Random.Range(0, candidates.Count)]] = true;
            }
            return result;
        }

        private static int Depth(int piece) => piece % 3 + piece / 3 % 3 + piece / 9;

        // 몇 겹 안쪽 조각인지(0 = 겉면). 보이는 세 면이 좌표 2 쪽이라 가장 큰 좌표로 판단.
        private static int Layer(int piece) =>
            GridSize - 1 - Mathf.Max(piece % 3, Mathf.Max(piece / 3 % 3, piece / 9));

        // 이미 지운 조각과 면이 맞닿아 있는지.
        private static bool TouchesRemoved(bool[] removedPieces, int piece)
        {
            int x = piece % 3, y = piece / 3 % 3, z = piece / 9;
            return IsRemovedAt(removedPieces, x - 1, y, z) || IsRemovedAt(removedPieces, x + 1, y, z) ||
                   IsRemovedAt(removedPieces, x, y - 1, z) || IsRemovedAt(removedPieces, x, y + 1, z) ||
                   IsRemovedAt(removedPieces, x, y, z - 1) || IsRemovedAt(removedPieces, x, y, z + 1);
        }

        private static bool IsRemovedAt(bool[] removedPieces, int x, int y, int z) =>
            x >= 0 && x < GridSize && y >= 0 && y < GridSize && z >= 0 && z < GridSize &&
            removedPieces[x + GridSize * y + GridSize * GridSize * z];

        // 큐브 중심 기준, 한 변 길이 1일 때 3D 점의 화면 위치.
        private static Vector2 Project(int x, int y, int z) =>
            (x - 1.5f) * AxisX + (y - 1.5f) * AxisY + (z - 1.5f) * AxisZ;

        // 조각의 보이는 면 하나(0 윗면, 1 왼쪽면, 2 오른쪽면): 시작 모서리 + 두 변.
        private static void GetFace(int piece, int face, out Vector2 origin, out Vector2 u, out Vector2 v)
        {
            int x = piece % 3, y = piece / 3 % 3, z = piece / 9;
            switch (face)
            {
                case 0: origin = Project(x, y + 1, z); u = AxisX; v = AxisZ; break;
                case 1: origin = Project(x, y, z + 1); u = AxisX; v = AxisY; break;
                default: origin = Project(x + 1, y, z); u = AxisZ; v = AxisY; break;
            }
        }

        // 점 d(면 시작 모서리 기준)가 u, v로 만든 평행사변형 안인지.
        private static bool InsideFace(Vector2 d, Vector2 u, Vector2 v)
        {
            float det = u.x * v.y - u.y * v.x;
            float s = (d.x * v.y - d.y * v.x) / det;
            float t = (u.x * d.y - u.y * d.x) / det;
            return s >= 0f && s < 1f && t >= 0f && t < 1f;
        }

        // 화면 점 p(단위 좌표)에 보이는 맨 앞 조각. 없으면 -1.
        private static int PieceAt(bool[] removedPieces, Vector2 p)
        {
            for (int k = DrawOrder.Length - 1; k >= 0; k--)
            {
                int piece = DrawOrder[k];
                if (removedPieces[piece])
                {
                    continue;
                }
                for (int face = 0; face < 3; face++)
                {
                    GetFace(piece, face, out Vector2 origin, out Vector2 u, out Vector2 v);
                    if (InsideFace(p - origin, u, v))
                    {
                        return piece;
                    }
                }
            }
            return -1;
        }

        // 조각이 조금이라도 보이는지. 45도 시점에선 모든 가림 경계가 면을 반으로 가르는 대각선 위에 있어서,
        // 면마다 두 삼각형 중심점만 확인하면 정확하다.
        private static bool IsVisible(bool[] removedPieces, int piece)
        {
            for (int face = 0; face < 3; face++)
            {
                GetFace(piece, face, out Vector2 origin, out Vector2 u, out Vector2 v);
                if (PieceAt(removedPieces, origin + (2f * u + v) / 3f) == piece ||
                    PieceAt(removedPieces, origin + (u + 2f * v) / 3f) == piece)
                {
                    return true;
                }
            }
            return false;
        }

        // 한 변 길이(픽셀). 큐브 전체 가로 = 6*0.866*a, 세로 = 6*a.
        private float EdgeLength()
        {
            Rect rect = contentRect;
            return Mathf.Min(rect.width / (2f * GridSize * Sqrt3Half), rect.height / (2f * GridSize)) * FitRatio;
        }

        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            float a = EdgeLength();
            if (a < 1f)
            {
                return;
            }

            Vector2 center = contentRect.center;
            Painter2D painter = ctx.painter2D;
            painter.lineJoin = LineJoin.Round;
            painter.lineWidth = LineWidth;
            painter.strokeColor = LineColor;

            foreach (int piece in DrawOrder)
            {
                if (removed[piece])
                {
                    continue;
                }

                float shade = Mathf.Lerp(DeepestShade, 1f, Depth(piece) / 6f);
                bool pressed = piece == pressedPiece && pressInside;
                for (int face = 0; face < 3; face++)
                {
                    GetFace(piece, face, out Vector2 origin, out Vector2 u, out Vector2 v);
                    Vector2 p = center + origin * a;
                    Color c = pressed ? Color.Lerp(FaceColors[face], PressedTint, PressedTintAmount) : FaceColors[face];
                    painter.fillColor = new Color(c.r * shade, c.g * shade, c.b * shade);

                    painter.BeginPath();
                    painter.MoveTo(p);
                    painter.LineTo(p + u * a);
                    painter.LineTo(p + (u + v) * a);
                    painter.LineTo(p + v * a);
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                }
            }
        }

        // 누른 위치에 보이는 조각. 없으면 -1.
        private int PieceAtPointer(Vector2 panelPosition)
        {
            float a = EdgeLength();
            if (a < 1f)
            {
                return -1;
            }
            return PieceAt(removed, (this.WorldToLocal(panelPosition) - contentRect.center) / a);
        }

        // 버튼처럼 동작: 누르면 그 조각만 강조, 뗄 때 아직 그 조각 위면 그때 지운다(미끄러져 나가면 취소).
        // 이미 누르는 중이면 다른 포인터(두 번째 손가락, 같은 탭이 중복으로 들어온 입력)는 무시 → 한 번 탭 = 조각 하나.
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!Interactive || pressedPointerId != NoPointer || evt.timestamp - lastRemoveTime < RemoveCooldownMs)
            {
                return;
            }

            int piece = PieceAtPointer(evt.position);
            if (piece < 0)
            {
                return;
            }

            pressedPiece = piece;
            pressedPointerId = evt.pointerId;
            pressInside = true;
            this.CapturePointer(evt.pointerId);
            MarkDirtyRepaint();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != pressedPointerId)
            {
                return;
            }

            bool inside = PieceAtPointer(evt.position) == pressedPiece;
            if (inside != pressInside)
            {
                pressInside = inside;
                MarkDirtyRepaint();
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != pressedPointerId)
            {
                return;
            }

            if (Interactive && PieceAtPointer(evt.position) == pressedPiece)
            {
                removed[pressedPiece] = true;
                lastRemoveTime = evt.timestamp;
            }
            CancelPress();
        }

        private void CancelPress()
        {
            if (pressedPointerId == NoPointer)
            {
                return;
            }

            // 캡처 해제가 다시 이 함수를 부르므로 상태부터 비운다.
            int pointerId = pressedPointerId;
            pressedPointerId = NoPointer;
            pressedPiece = -1;
            if (this.HasPointerCapture(pointerId))
            {
                this.ReleasePointer(pointerId);
            }
            MarkDirtyRepaint();
        }
    }
}
