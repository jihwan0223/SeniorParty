using UnityEngine;
using UnityEngine.UIElements;

namespace SeniorParty.UI
{
    // 원형 진행률 링. 가운데에 "73/100" 식으로 값을 표시한다.
    [UxmlElement]
    public partial class CircularProgress : VisualElement
    {
        public static readonly string UssClassName = "circular-progress";
        public static readonly string LabelUssClassName = "circular-progress__label";

        // 라벨 폰트 크기 = 반지름 * 이 값.
        private const float LabelFontScale = 0.34f;
        private const float MinLabelFontSize = 16f;
        // 이보다 작으면 그리지 않는다.
        private const float MinRenderableSize = 2f;

        // 가운데 값 텍스트.
        private readonly Label label;

        // 현재 값.
        private float value;
        // 최댓값.
        private float maxValue = 100f;
        // 링 두께.
        private float thickness = 20f;
        // 배경 트랙 색.
        private Color trackColor = new Color(0.85f, 0.83f, 0.78f);
        // 진행률 색.
        private Color progressColor = new Color(0.20f, 0.50f, 0.85f);
        // 가운데 텍스트 표시 여부.
        private bool showLabel = true;

        // 현재 값. 0~maxValue로 클램프된다.
        [UxmlAttribute]
        public float Value
        {
            get => value;
            set
            {
                this.value = Mathf.Clamp(value, 0f, maxValue);
                OnValueOrMaxChanged();
            }
        }

        // 최댓값.
        [UxmlAttribute("max-value")]
        public float MaxValue
        {
            get => maxValue;
            set
            {
                maxValue = Mathf.Max(1f, value);
                this.value = Mathf.Clamp(this.value, 0f, maxValue);
                OnValueOrMaxChanged();
            }
        }

        // 링 두께.
        [UxmlAttribute]
        public float Thickness
        {
            get => thickness;
            set { thickness = Mathf.Max(1f, value); MarkDirtyRepaint(); }
        }

        // 배경 트랙 색.
        [UxmlAttribute("track-color")]
        public Color TrackColor
        {
            get => trackColor;
            set { trackColor = value; MarkDirtyRepaint(); }
        }

        // 진행률 색.
        [UxmlAttribute("progress-color")]
        public Color ProgressColor
        {
            get => progressColor;
            set { progressColor = value; MarkDirtyRepaint(); }
        }

        // 가운데 "값/최댓값" 텍스트 표시 여부.
        [UxmlAttribute("show-label")]
        public bool ShowLabel
        {
            get => showLabel;
            set
            {
                showLabel = value;
                label.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // 그리기 콜백과 라벨을 준비한다.
        public CircularProgress()
        {
            AddToClassList(UssClassName);
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(LabelUssClassName);
            Add(label);

            RefreshLabel();
        }

        // 크기가 바뀔 때 라벨 폰트 크기를 다시 계산한다(그리기 콜백 안에서는 다른 엘리먼트 스타일을 못 바꿔서 분리함).
        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            float radius = Mathf.Min(evt.newRect.width, evt.newRect.height) / 2f - thickness / 2f;
            if (radius > 0f)
            {
                label.style.fontSize = Mathf.Max(MinLabelFontSize, radius * LabelFontScale);
            }
        }

        // value/maxValue가 바뀔 때 라벨 갱신 + 다시 그리기.
        private void OnValueOrMaxChanged()
        {
            RefreshLabel();
            MarkDirtyRepaint();
        }

        // 라벨 텍스트를 "값/최댓값" 형식으로 갱신한다.
        private void RefreshLabel()
        {
            label.text = $"{Mathf.RoundToInt(value)}/{Mathf.RoundToInt(maxValue)}";
        }

        // 배경 트랙 + 진행률 호를 그린다.
        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            Rect rect = contentRect;
            if (rect.width < MinRenderableSize || rect.height < MinRenderableSize)
            {
                return;
            }

            float radius = Mathf.Min(rect.width, rect.height) / 2f - thickness / 2f;
            if (radius <= 0f)
            {
                return;
            }

            Vector2 center = new Vector2(rect.width / 2f, rect.height / 2f);

            Painter2D painter = ctx.painter2D;
            painter.lineWidth = thickness;
            painter.lineCap = LineCap.Round;

            // 배경 트랙 (전체 원)
            DrawArc(painter, center, radius, trackColor, 0f, 360f);

            float progressRatio = maxValue > 0f ? Mathf.Clamp01(value / maxValue) : 0f;
            if (progressRatio > 0f)
            {
                // 12시 방향(-90도)에서 시계방향으로 진행률만큼 그린다.
                DrawArc(painter, center, radius, progressColor, -90f, -90f + 360f * progressRatio);
            }
        }

        // 지정한 각도 구간의 호 하나를 그린다.
        private static void DrawArc(Painter2D painter, Vector2 center, float radius, Color color, float startDegrees, float endDegrees)
        {
            painter.strokeColor = color;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(startDegrees), Angle.Degrees(endDegrees));
            painter.Stroke();
        }
    }
}
