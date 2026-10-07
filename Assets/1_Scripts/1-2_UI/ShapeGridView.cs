using UnityEngine;
using UnityEngine.UIElements;

namespace SeniorParty.UI
{
    // 큰 네모 하나를 SizeXSize 칸으로 나눈 모양 표시. 색칠할 칸은 Mask의 비트로 정한다(칸 번호 = 행 * Size + 열, 왼쪽 위가 0번).
    // 칸 선을 요소 테두리 대신 직접 그려서, 화면 배율이 달라도 모든 선 굵기가 같다.
    [UxmlElement]
    public partial class ShapeGridView : VisualElement
    {
        private int size = 3;

        // 격자 한 변의 칸 수(난이도에 따라 컨트롤러가 바꿔준다).
        public int Size
        {
            get => size;
            set
            {
                size = Mathf.Max(1, value);
                MarkDirtyRepaint();
            }
        }

        private const float LineWidth = 4f;
        private const float CellInset = 6f;
        private const float CellBorderWidth = 4f;
        private const float CellCornerRadius = 14f;
        private static readonly Color FilledColor = new Color(0.96f, 0.51f, 0.12f);
        private static readonly Color CellBorderColor = new Color(0.62f, 0.32f, 0.05f);
        private static readonly Color LineColor = Color.black;

        private int mask;

        public int Mask
        {
            get => mask;
            set
            {
                mask = value;
                MarkDirtyRepaint();
            }
        }

        public ShapeGridView()
        {
            generateVisualContent += OnGenerateVisualContent;
        }

        // 칠해진 칸(둥근 사각형)을 먼저 그리고, 격자선을 그 위에 덧그린다.
        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            Rect rect = contentRect;
            float side = Mathf.Min(rect.width, rect.height);
            if (side < 1f)
            {
                return;
            }

            // 선의 가운데가 칸 경계에 오도록 하고, 바깥 선이 요소 밖으로 나가지 않게 반 굵기만큼 안쪽에서 시작.
            float half = LineWidth / 2f;
            float origin = half;
            float cell = (side - LineWidth) / Size;

            Painter2D painter = ctx.painter2D;
            painter.fillColor = FilledColor;
            painter.strokeColor = CellBorderColor;
            painter.lineWidth = CellBorderWidth;
            for (int i = 0; i < Size * Size; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    continue;
                }
                Vector2 topLeft = rect.position + new Vector2(origin + i % Size * cell, origin + i / Size * cell);
                DrawRoundedRect(painter, topLeft + Vector2.one * CellInset, cell - CellInset * 2f, CellCornerRadius);
                painter.Fill();
                painter.Stroke();
            }

            painter.strokeColor = LineColor;
            painter.lineWidth = LineWidth;
            painter.lineCap = LineCap.Butt;
            for (int k = 0; k <= Size; k++)
            {
                float offset = origin + k * cell;
                // 세로선과 가로선. 끝을 반 굵기만큼 늘려서 모서리가 비지 않게 한다.
                painter.BeginPath();
                painter.MoveTo(rect.position + new Vector2(offset, origin - half));
                painter.LineTo(rect.position + new Vector2(offset, origin + Size * cell + half));
                painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(rect.position + new Vector2(origin - half, offset));
                painter.LineTo(rect.position + new Vector2(origin + Size * cell + half, offset));
                painter.Stroke();
            }
        }

        // topLeft~(topLeft+size) 정사각형을 모서리 반지름 radius로 둥글린 사각형 경로를 그린다(채우기/선은 호출한 쪽에서).
        private static void DrawRoundedRect(Painter2D painter, Vector2 topLeft, float size, float radius)
        {
            float r = Mathf.Min(radius, size * 0.5f);
            float x = topLeft.x;
            float y = topLeft.y;
            float right = x + size;
            float bottom = y + size;

            painter.BeginPath();
            painter.MoveTo(new Vector2(x + r, y));
            painter.LineTo(new Vector2(right - r, y));
            painter.ArcTo(new Vector2(right, y), new Vector2(right, y + r), r);
            painter.LineTo(new Vector2(right, bottom - r));
            painter.ArcTo(new Vector2(right, bottom), new Vector2(right - r, bottom), r);
            painter.LineTo(new Vector2(x + r, bottom));
            painter.ArcTo(new Vector2(x, bottom), new Vector2(x, bottom - r), r);
            painter.LineTo(new Vector2(x, y + r));
            painter.ArcTo(new Vector2(x, y), new Vector2(x + r, y), r);
            painter.ClosePath();
        }
    }
}
