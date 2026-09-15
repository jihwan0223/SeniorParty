using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UIElements;
using SeniorParty.UI;

namespace SeniorParty
{
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private AudioMixer audioMixer;

        // GameAudioMixer에 Exposed된 파라미터 이름. 믹서 쪽 이름을 바꾸면 여기도 같이 바꿔야 한다.
        // 주의: 믹서에 "BGMVolune"로 노출되어 있음(오타, "BGMVolume" 아님).
        private const string MasterVolumeParam = "MasterVolume";
        private const string BgmVolumeParam = "BGMVolune";
        private const string SfxVolumeParam = "SFXVolume";

        private VisualElement domainPanel;
        private VisualElement selectPanel;
        private VisualElement settingsPanel;
        private VisualElement quitPanel;

        private Button iconButton;
        private int iconIndex;

        private CircularProgress domainRing;
        private Label domainNameLabel;
        private int domainIndex;

        // 설정 초기화 시 되돌아갈 값.
        private const float DefaultVolumeSlider = 100f;
        private readonly System.Collections.Generic.List<Slider> volumeSliders = new();

        // 볼륨/영역 점수의 실제 저장소.
        private SaveData saveData;

        // 미니게임이 아직 없어서 실제 플레이 기록이 쌓이기 전까지 쓸 기본 점수.
        private static readonly float[] DefaultDomainScores = { 73f, 40f, 88f, 55f, 60f };

        // 영역 이름 + 색. 점수는 saveData.domainScores에서 같은 순서로 가져온다.
        private static readonly (string label, Color color)[] Domains =
        {
            ("기억력", new Color(0.18f, 0.47f, 0.86f)),
            ("주의력", new Color(0.25f, 0.66f, 0.35f)),
            ("언어능력", new Color(0.88f, 0.63f, 0.13f)),
            ("시공간 능력", new Color(0.54f, 0.37f, 0.78f)),
            ("실행 기능", new Color(0.82f, 0.40f, 0.23f)),
        };

        // 임시 아이콘: 색깔 원.
        private static readonly (string name, Color color)[] Icons =
        {
            ("빨강", new Color(0.86f, 0.31f, 0.31f)),
            ("초록", new Color(0.30f, 0.70f, 0.40f)),
            ("파랑", new Color(0.20f, 0.50f, 0.85f)),
            ("노랑", new Color(0.95f, 0.78f, 0.25f)),
        };

        // 세이브 로드 + 화면 요소 찾기 + 버튼/슬라이더 이벤트 연결.
        private void OnEnable()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            saveData = SaveManager.Load();
            if (saveData.domainScores == null || saveData.domainScores.Length != Domains.Length)
            {
                saveData.domainScores = (float[])DefaultDomainScores.Clone();
            }

            var root = GetComponent<UIDocument>().rootVisualElement;

            domainPanel = root.Q<VisualElement>("domain-panel");
            selectPanel = root.Q<VisualElement>("select-panel");
            settingsPanel = root.Q<VisualElement>("settings-panel");
            quitPanel = root.Q<VisualElement>("quit-panel");
            iconButton = root.Q<Button>("icon-button");

            domainRing = root.Q<CircularProgress>("domain-ring");
            domainNameLabel = root.Q<Label>("domain-name-label");

            root.Q<Button>("start-button").clicked += () => Show(domainPanel);
            root.Q<Button>("domain-back-button").clicked += () => Hide(domainPanel);
            root.Q<Button>("domain-prev-button").clicked += () => CycleDomain(-1);
            root.Q<Button>("domain-next-button").clicked += () => CycleDomain(1);
            root.Q<Button>("domain-card").clicked += OnDomainCardClicked;
            ApplyDomain();

            root.Q<Button>("select-settings-button").clicked += () => Show(settingsPanel);
            root.Q<Button>("prev-button").clicked += () => CycleIcon(-1);
            root.Q<Button>("next-button").clicked += () => CycleIcon(1);
            iconButton.clicked += OnIconClicked;

            root.Q<Button>("settings-button").clicked += () => Show(settingsPanel);
            root.Q<Button>("settings-close-button").clicked += () => Hide(settingsPanel);
            root.Q<Button>("menu-exit-button").clicked += GoToMenu;
            root.Q<Button>("settings-quit-button").clicked += () => Show(quitPanel);
            SetupVolumeSlider(root, "master-volume-slider", "master-volume-value", MasterVolumeParam,
                saveData.masterVolume, v => saveData.masterVolume = v);
            SetupVolumeSlider(root, "bgm-volume-slider", "bgm-volume-value", BgmVolumeParam,
                saveData.bgmVolume, v => saveData.bgmVolume = v);
            SetupVolumeSlider(root, "sfx-volume-slider", "sfx-volume-value", SfxVolumeParam,
                saveData.sfxVolume, v => saveData.sfxVolume = v);
            root.Q<Button>("settings-reset-button").clicked += ResetVolumesToDefault;

            // 딤 처리된 바깥 영역 클릭하면 설정 닫기.
            settingsPanel.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == settingsPanel)
                {
                    Hide(settingsPanel);
                }
            });
            root.Q<Button>("quit-button").clicked += () => Show(quitPanel);
            root.Q<Button>("quit-cancel-button").clicked += () => Hide(quitPanel);
            root.Q<Button>("quit-confirm-button").clicked += Quit;

            Hide(domainPanel);
            Hide(selectPanel);
            Hide(settingsPanel);
            Hide(quitPanel);
            ApplyIcon();
        }

        // 영역 카드를 이전/다음으로 넘긴다.
        private void CycleDomain(int step)
        {
            domainIndex = (domainIndex + step + Domains.Length) % Domains.Length;
            ApplyDomain();
        }

        // 현재 선택된 영역의 점수/색/이름을 화면에 반영한다.
        private void ApplyDomain()
        {
            var domain = Domains[domainIndex];
            domainRing.Value = saveData.domainScores[domainIndex];
            domainRing.ProgressColor = domain.color;
            domainNameLabel.text = domain.label;
        }

        // 영역 카드를 눌렀을 때 (나중에 해당 영역 미니게임으로 이동하도록 확장 예정).
        private void OnDomainCardClicked()
        {
            var domain = Domains[domainIndex];
            Debug.Log($"{domain.label} 영역 선택됨 (점수 {saveData.domainScores[domainIndex]}/100)");
        }

        // 볼륨 슬라이더 하나를 초기화한다. initialValue/onValueChanged는 saveData 필드를 읽고 쓰기 위한 것.
        // 오디오믹서 값은 앱을 껐다 켜면 초기화되므로(영구 저장 안 됨) saveData가 진짜 저장소이고,
        // 믹서는 그 값을 실제 소리에 반영하는 용도로만 쓴다.
        private void SetupVolumeSlider(VisualElement root, string sliderName, string valueLabelName, string mixerParam,
            float initialValue, System.Action<float> onValueChanged)
        {
            var slider = root.Q<Slider>(sliderName);
            var valueLabel = root.Q<Label>(valueLabelName);
            if (slider == null || audioMixer == null)
            {
                return;
            }

            volumeSliders.Add(slider);

            // 트랙 안에 값만큼 채워 보여줄 막대. tracker의 맨 앞에 넣어서 배경 뒤, 손잡이 앞에 그려지게 한다.
            var tracker = slider.Q<VisualElement>(className: "unity-base-slider__tracker");
            VisualElement fill = null;
            if (tracker != null)
            {
                fill = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("setting-slider-fill");
                tracker.Insert(0, fill);
            }

            // 채워지는 막대 너비를 현재 값 비율(%)로 갱신한다.
            void UpdateFill(float value)
            {
                if (fill == null)
                {
                    return;
                }
                float ratio = Mathf.InverseLerp(slider.lowValue, slider.highValue, value);
                fill.style.width = Length.Percent(ratio * 100f);
            }

            slider.SetValueWithoutNotify(initialValue);
            audioMixer.SetFloat(mixerParam, SliderToDecibel(initialValue));
            if (valueLabel != null)
            {
                valueLabel.text = Mathf.RoundToInt(slider.value).ToString();
            }
            UpdateFill(slider.value);

            slider.RegisterValueChangedCallback(evt =>
            {
                audioMixer.SetFloat(mixerParam, SliderToDecibel(evt.newValue));
                if (valueLabel != null)
                {
                    valueLabel.text = Mathf.RoundToInt(evt.newValue).ToString();
                }
                UpdateFill(evt.newValue);
                onValueChanged(evt.newValue);
                SaveManager.Save(saveData);
            });
        }

        // 슬라이더 0~100을 dB -80~+20 구간에 하나의 기울기로 선형 매핑한다(구간을 안 나눠서 조절감이 일정함).
        private const float MinDb = -80f;
        private const float MaxDb = 20f;

        // 슬라이더 값(0~100) -> dB.
        private static float SliderToDecibel(float sliderValue)
        {
            return MinDb + sliderValue / 100f * (MaxDb - MinDb);
        }

        // dB -> 슬라이더 값(0~100).
        private static float DecibelToSlider(float decibel)
        {
            return Mathf.Clamp((decibel - MinDb) / (MaxDb - MinDb) * 100f, 0f, 100f);
        }

        // 모든 볼륨 슬라이더를 기본값으로 되돌린다(값 대입만으로 라벨/믹서/저장까지 콜백이 알아서 처리).
        private void ResetVolumesToDefault()
        {
            foreach (var slider in volumeSliders)
                slider.value = DefaultVolumeSlider;
        }

        // 아이콘을 이전/다음으로 넘긴다.
        private void CycleIcon(int step)
        {
            iconIndex = (iconIndex + step + Icons.Length) % Icons.Length;
            ApplyIcon();
        }

        // 현재 선택된 아이콘 색을 버튼에 반영한다.
        private void ApplyIcon()
        {
            iconButton.style.backgroundColor = Icons[iconIndex].color;
        }

        // 아이콘 버튼을 눌렀을 때(선택 확정용, 추후 확장 예정).
        private void OnIconClicked()
        {
            Debug.Log($"아이콘 선택됨: {Icons[iconIndex].name} ({iconIndex})");
        }

        // 설정/영역/아이콘 패널을 모두 닫고 메인 메뉴로 돌아간다.
        private void GoToMenu()
        {
            Hide(settingsPanel);
            Hide(domainPanel);
            Hide(selectPanel);
        }

        private static void Show(VisualElement panel) => panel?.RemoveFromClassList("hidden");
        private static void Hide(VisualElement panel) => panel?.AddToClassList("hidden");

        // 게임을 종료한다(에디터에서는 플레이 모드만 멈춤).
        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
