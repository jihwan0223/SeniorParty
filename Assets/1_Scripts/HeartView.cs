using UnityEngine;
using UnityEngine.UIElements;

namespace SeniorParty.UI
{
    // 목숨 표시용 하트. 남아있으면 빨강, 잃으면 회색. 이미지 없이 직접 그린다.
    [UxmlElement]
    public partial class HeartView : VisualElement
    {
        private static readonly Color FullColor = new Color(0.86f, 0.18f, 0.22f);
        private static readonly Color LostColor = new Color(0.75f, 0.75f, 0.75f);
        private static readonly Color OutlineColor = new Color(0.15f, 0.15f, 0.15f);
        private const float OutlineWidth = 3f;

        private bool lost;

        public bool Lost
        {
            get => lost;
            set
            {
                lost = value;
                MarkDirtyRepaint();
            }
        }

        public HeartView()
        {
            generateVisualContent += OnGenerateVisualContent;
        }

        // 요소 크기(0~1 비율 좌표)에 맞춘 하트: 아래 꼭짓점에서 왼쪽/오른쪽 둥근 부분을 곡선으로 잇는다.
        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            Rect rect = contentRect;
            if (rect.width < 1f || rect.height < 1f)
            {
                return;
            }

            Vector2 P(float x, float y) => rect.position + new Vector2(x * rect.width, y * rect.height);

            Painter2D painter = ctx.painter2D;
            painter.fillColor = lost ? LostColor : FullColor;
            painter.strokeColor = OutlineColor;
            painter.lineWidth = OutlineWidth;
            painter.lineJoin = LineJoin.Round;

            painter.BeginPath();
            painter.MoveTo(P(0.5f, 0.92f));
            painter.BezierCurveTo(P(0.18f, 0.70f), P(0.03f, 0.50f), P(0.03f, 0.32f));
            painter.BezierCurveTo(P(0.03f, 0.12f), P(0.30f, 0.02f), P(0.5f, 0.22f));
            painter.BezierCurveTo(P(0.70f, 0.02f), P(0.97f, 0.12f), P(0.97f, 0.32f));
            painter.BezierCurveTo(P(0.97f, 0.50f), P(0.82f, 0.70f), P(0.5f, 0.92f));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }
    }
}
