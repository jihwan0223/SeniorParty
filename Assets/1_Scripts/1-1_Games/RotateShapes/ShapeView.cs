using UnityEngine;
using UnityEngine.UIElements;

namespace SeniorParty.UI
{
    // 도형 회전 게임용 도형 하나를 이미지 없이 직접 그린다. 도형 번호 + 회전 각도(도, 시계 방향)를 정해서 쓴다.
    // 모든 도형이 같은 축척으로 그려져서 크기로는 구별이 안 된다.
    [UxmlElement]
    public partial class ShapeView : VisualElement
    {
        // 격자 단위 좌표의 외곽선(시계 방향). 서로 어떻게 돌려도 겹치지 않는 도형들.
        private static readonly Vector2[][] Shapes =
        {
            // L
            new[] { V(0, 0), V(1, 0), V(1, 3), V(3, 3), V(3, 4), V(0, 4) },
            // T
            new[] { V(0, 0), V(3, 0), V(3, 1), V(2, 1), V(2, 3), V(1, 3), V(1, 1), V(0, 1) },
            // S
            new[] { V(0, 0), V(2, 0), V(2, 1), V(3, 1), V(3, 3), V(1, 3), V(1, 2), V(0, 2) },
            // F
            new[] { V(1, 0), V(3, 0), V(3, 1), V(2, 1), V(2, 3), V(1, 3), V(1, 2), V(0, 2), V(0, 1), V(1, 1) },
            // U
            new[] { V(0, 0), V(1, 0), V(1, 2), V(2, 2), V(2, 0), V(3, 0), V(3, 3), V(0, 3) },
            // P
            new[] { V(0, 0), V(2, 0), V(2, 2), V(1, 2), V(1, 3), V(0, 3) },
            // W
            new[] { V(0, 0), V(1, 0), V(1, 1), V(2, 1), V(2, 2), V(3, 2), V(3, 3), V(1, 3), V(1, 2), V(0, 2) },
            // Y
            new[] { V(1, 0), V(2, 0), V(2, 4), V(1, 4), V(1, 2), V(0, 2), V(0, 1), V(1, 1) },
        };

        public static int ShapeCount => Shapes.Length;

        // 모든 도형의 (중심에서 가장 먼 꼭짓점까지 거리) 중 최댓값. 어떤 각도로 돌려도 요소 안에 들어오게 하는 기준.
        private const float MaxRadius = 2.6f;
        private const float FitRatio = 0.9f;
        private const float LineWidth = 4f;

        private static readonly Color FillColor = new Color(0.27f, 0.47f, 0.80f);
        private static readonly Color LineColor = new Color(0.15f, 0.15f, 0.15f);

        private int shape;
        private float angle;
        private bool mirrored;
        private bool hasShape;

        public ShapeView()
        {
            generateVisualContent += OnGenerateVisualContent;
        }

        // mirrored: 좌우로 뒤집은 도형(회전만으로는 절대 원본과 겹치지 않는, 가장 헷갈리는 오답용).
        public void Set(int shapeIndex, float angleDegrees, bool mirrored = false)
        {
            shape = shapeIndex;
            angle = angleDegrees;
            this.mirrored = mirrored;
            hasShape = true;
            MarkDirtyRepaint();
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        // 도형 외곽선을 중심 기준으로 회전/반전시킨 뒤 요소 크기에 맞게 그린다.
        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            Rect rect = contentRect;
            if (!hasShape || rect.width < 1f || rect.height < 1f)
            {
                return;
            }

            Vector2[] points = Shapes[shape];
            Vector2 min = points[0];
            Vector2 max = points[0];
            foreach (Vector2 p in points)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            Vector2 pivot = (min + max) * 0.5f;

            float scale = Mathf.Min(rect.width, rect.height) * 0.5f / MaxRadius * FitRatio;
            float rad = angle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            Painter2D painter = ctx.painter2D;
            painter.fillColor = FillColor;
            painter.strokeColor = LineColor;
            painter.lineWidth = LineWidth;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 d = points[i] - pivot;
                if (mirrored)
                {
                    d.x = -d.x;
                }
                // 화면 좌표(y가 아래로 증가)에서 양의 각도 = 시계 방향.
                Vector2 rotated = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
                Vector2 screen = rect.center + rotated * scale;
                if (i == 0)
                {
                    painter.MoveTo(screen);
                }
                else
                {
                    painter.LineTo(screen);
                }
            }
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }
    }
}
