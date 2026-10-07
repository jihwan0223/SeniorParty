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

        // 라벨 폰트 크기 = 반지름 * 이 값. 현재 점수는 "/최댓값"보다 크게 보이도록 배율을 곱한다.
        private const float LabelFontScale = 0.34f;
        private const float MinLabelFontSize = 16f;
        private const float ValueFontScale = 1.5f;
        private const float MaxValueFontScale = 0.8f;
        // 두 줄 사이 간격: 빈 줄을 이 배율의 작은 글자로 하나 끼워 넣어서 띄운다.
        private const float LineGapScale = 0.1f;
        // 이보다 작으면 그리지 않는다.
        private const float MinRenderableSize = 2f;

        // "/최댓값" 부분 기준 폰트 크기(현재 점수는 이 값 * ValueFontScale로 표시).
        private float baseFontSizePx = MinLabelFontSize;

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

        // 값이 목표치까지 쭉 채워지는 애니메이션 진행 중이면 그 예약 항목.
        private IVisualElementScheduledItem animationSchedule;
        private const int AnimationIntervalMs = 16;
        private const int DefaultAnimationDurationMs = 1333;

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

        // 현재 값에서 target까지 durationMs 동안 채워 보여준다. 끝으로 갈수록 천천히(ease-out) 증가.
        public void AnimateValue(float target, int durationMs = DefaultAnimationDurationMs)
        {
            animationSchedule?.Pause();
            float start = value;
            target = Mathf.Clamp(target, 0f, maxValue);
            float elapsedMs = 0f;

            animationSchedule = schedule.Execute(() =>
            {
                elapsedMs += AnimationIntervalMs;
                float t = durationMs <= 0 ? 1f : Mathf.Clamp01(elapsedMs / durationMs);
                // 처음 1.2초: 속도 자체가 S자(smoothstep)로 바뀐다 - 초반엔 거의 안 줄고,
                // 중반부터 서서히 줄어서, 후반 정지 속도(0.03)로 부드럽게 들어간다(양쪽 끝 다
                // 기울기가 매끈하게 이어져서 훅 꺾이는 지점이 없음). 마지막 0.8초: 그 속도 유지.
                const float m0 = 3.2633f; // 초반 속도
                const float r = 0.03f;    // 후반 유지 속도
                const float t1 = 0.6f;    // 전환 지점(1.2초)
                float eased;
                if (t < t1)
                {
                    float u = t / t1;
                    eased = m0 * t + (r - m0) * t1 * (u * u * u - 0.5f * u * u * u * u);
                }
                else
                {
                    eased = 0.988f + r * (t - t1);
                }
                Value = Mathf.Lerp(start, target, eased);
                if (t >= 1f)
                {
                    animationSchedule.Pause();
                }
            }).Every(AnimationIntervalMs);
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
                baseFontSizePx = Mathf.Max(MinLabelFontSize, radius * LabelFontScale);
                label.style.fontSize = baseFontSizePx;
                RefreshLabel();
            }
        }

        // value/maxValue가 바뀔 때 라벨 갱신 + 다시 그리기.
        private void OnValueOrMaxChanged()
        {
            RefreshLabel();
            MarkDirtyRepaint();
        }

        // 라벨 텍스트를 "값(줄바꿈)/최댓값" 형식으로 갱신한다. 현재 값은 소수점 첫째자리까지, 최댓값보다 크게 표시.
        private void RefreshLabel()
        {
            float valueFontSizePx = baseFontSizePx * ValueFontScale;
            float maxFontSizePx = baseFontSizePx * MaxValueFontScale;
            float gapFontSizePx = baseFontSizePx * LineGapScale;
            label.text = $"<size={valueFontSizePx:F0}px>{value:F1}</size>\n<size={gapFontSizePx:F0}px> </size>\n<size={maxFontSizePx:F0}px>/{Mathf.RoundToInt(maxValue)}</size>";
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
                // 100%(360도 꽉 채움)는 시작각=끝각(mod 360)이 되어 길이 0 경로로 취급되어
                // 아무것도 안 그려지는 문제가 있어서, 반원 2개로 나눠 그려 꽉 찬 원을 보장한다.
                float sweep = 360f * progressRatio;
                if (sweep >= 359.99f)
                {
                    DrawArc(painter, center, radius, progressColor, -90f, 90f);
                    DrawArc(painter, center, radius, progressColor, 90f, 270f);
                }
                else
                {
                    DrawArc(painter, center, radius, progressColor, -90f, -90f + sweep);
                }
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
