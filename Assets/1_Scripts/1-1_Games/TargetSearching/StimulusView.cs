using UnityEngine;
using UnityEngine.UIElements;

namespace SeniorParty.UI
{
    // 표적 찾기 게임의 자극 하나(색 + 모양)를 이미지 없이 직접 그린다.
    [UxmlElement]
    public partial class StimulusView : VisualElement
    {
        public const int ColorCount = 4;
        public const int ShapeCount = 4;

        // 0 빨강, 1 파랑, 2 노랑, 3 초록.
        private static readonly Color[] Colors =
        {
            new Color(0.86f, 0.15f, 0.15f),
            new Color(0.15f, 0.35f, 0.85f),
            new Color(0.98f, 0.85f, 0.10f),
            new Color(0.10f, 0.65f, 0.25f),
        };
        private static readonly Color LineColor = new Color(0.15f, 0.15f, 0.15f);
        private const float LineWidth = 4f;
        // 요소 크기 대비 도형 크기(테두리 선이 잘리지 않게 여백).
        private const float FitRatio = 0.85f;
        private const float StarInnerRatio = 0.45f;

        private int color;
        private int shape;
        private float rotationDeg;
        private float sizeScale = 1f;
        private bool hasStimulus;

        public StimulusView()
        {
            generateVisualContent += OnGenerateVisualContent;
        }

        // shape: 0 원, 1 네모, 2 세모, 3 별. rotationDegrees: 도형을 살짝 기울여서 다 똑같은 방향으로만
        // 놓여있지 않게 한다(원은 회전해도 모양이 그대로라 영향 없음). scale: 요소 크기 대비 도형이 차지하는
        // 비율(1이면 꽉 참) — 터치 영역(버튼 크기)은 그대로 두고 그려지는 도형만 작아 보이게 한다.
        public void Set(int colorIndex, int shapeIndex, float rotationDegrees = 0f, float scale = 1f)
        {
            color = colorIndex;
            shape = shapeIndex;
            rotationDeg = rotationDegrees;
            sizeScale = scale;
            hasStimulus = true;
            MarkDirtyRepaint();
        }

        // shape 값에 따라 원/네모/세모/별 중 하나의 경로를 만들어 채우고 테두리를 그린다.
        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            Rect rect = contentRect;
            if (!hasStimulus || rect.width < 1f || rect.height < 1f)
            {
                return;
            }

            float r = Mathf.Min(rect.width, rect.height) * 0.5f * FitRatio * sizeScale;
            Vector2 c = rect.center;

            float rad = rotationDeg * Mathf.Deg2Rad;
            float cosR = Mathf.Cos(rad);
            float sinR = Mathf.Sin(rad);
            Vector2 Rotated(Vector2 offset) => new Vector2(offset.x * cosR - offset.y * sinR, offset.x * sinR + offset.y * cosR);

            Painter2D painter = ctx.painter2D;
            painter.fillColor = Colors[color];
            painter.strokeColor = LineColor;
            painter.lineWidth = LineWidth;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();

            switch (shape)
            {
                case 0:
                    painter.Arc(c, r, 0f, 360f);
                    break;
                case 1:
                    // 네모는 대각선(모서리)이 다른 도형의 반지름 r과 같아지도록 변의 절반을 r/√2로 잡는다.
                    // 그래야 회전해도 다른 도형과 같은 반지름 원 안에 들어가서 옆 자극과 안 겹친다.
                    float half = r * 0.70710678f;
                    painter.MoveTo(c + Rotated(new Vector2(-half, -half)));
                    painter.LineTo(c + Rotated(new Vector2(half, -half)));
                    painter.LineTo(c + Rotated(new Vector2(half, half)));
                    painter.LineTo(c + Rotated(new Vector2(-half, half)));
                    break;
                case 2:
                    painter.MoveTo(c + Rotated(new Vector2(0f, -r)));
                    painter.LineTo(c + Rotated(new Vector2(r * 0.87f, r * 0.5f)));
                    painter.LineTo(c + Rotated(new Vector2(-r * 0.87f, r * 0.5f)));
                    break;
                default:
                    for (int i = 0; i < 10; i++)
                    {
                        float radius = i % 2 == 0 ? r : r * StarInnerRatio;
                        float a = (i * 36f - 90f) * Mathf.Deg2Rad;
                        Vector2 p = c + Rotated(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                        if (i == 0)
                        {
                            painter.MoveTo(p);
                        }
                        else
                        {
                            painter.LineTo(p);
                        }
                    }
                    break;
            }

            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }
    }
}
