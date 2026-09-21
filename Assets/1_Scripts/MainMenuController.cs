using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeniorParty
{
    public class MainMenuController : MonoBehaviour
    {
        // 미니게임 씬에서 돌아올 때 어디로 갈지 알려주는 정적 상태(씬 전환 넘어서 값 유지용).
        public enum ReturnTarget { None, GameTabs, GameTabsWithDifficulty }
        public static ReturnTarget PendingReturnTarget = ReturnTarget.None;
        public static string LastDifficulty;
        public static string LastGameName;

        [SerializeField] private AudioMixer audioMixer;

        // GameAudioMixer에 Exposed된 파라미터 이름. 믹서 쪽 이름을 바꾸면 여기도 같이 바꿔야 한다.
        // 주의: 믹서에 "BGMVolune"로 노출되어 있음(오타, "BGMVolume" 아님).
        private const string MasterVolumeParam = "MasterVolume";
        private const string BgmVolumeParam = "BGMVolune";
        private const string SfxVolumeParam = "SFXVolume";

        private VisualElement gameTabsPanel;
        private VisualElement difficultyPanel;
        private VisualElement selectPanel;
        private VisualElement settingsPanel;
        private VisualElement quitPanel;

        // 게임 탭바(게임/정보/임시1/임시2/임시3): 인덱스로 내용/버튼을 짝지어 관리.
        private static readonly string[] TabContentNames =
            { "tab-content-game", "tab-content-info", "tab-content-temp1", "tab-content-temp2", "tab-content-temp3" };
        private static readonly string[] TabButtonNames =
            { "bottom-tab-game", "bottom-tab-info", "bottom-tab-temp1", "bottom-tab-temp2", "bottom-tab-temp3" };
        private VisualElement[] tabContents;
        private Button[] tabButtons;

        // 난이도 패널에서 어떤 게임을 고른 건지 기억해두는 용도.
        private string selectedGameName;

        private Button iconButton;
        private int iconIndex;

        // 설정 초기화 시 되돌아갈 값.
        private const float DefaultVolumeSlider = 100f;
        private readonly System.Collections.Generic.List<Slider> volumeSliders = new();

        // 볼륨 설정의 실제 저장소.
        private SaveData saveData;

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

            var root = GetComponent<UIDocument>().rootVisualElement;

            gameTabsPanel = root.Q<VisualElement>("game-tabs-panel");
            difficultyPanel = root.Q<VisualElement>("difficulty-panel");
            selectPanel = root.Q<VisualElement>("select-panel");
            settingsPanel = root.Q<VisualElement>("settings-panel");
            quitPanel = root.Q<VisualElement>("quit-panel");
            iconButton = root.Q<Button>("icon-button");

            root.Q<Button>("start-button").clicked += () => Show(gameTabsPanel);
            root.Q<Button>("game-tabs-back-button").clicked += () => Hide(gameTabsPanel);

            tabContents = new VisualElement[TabContentNames.Length];
            tabButtons = new Button[TabButtonNames.Length];
            for (int i = 0; i < TabContentNames.Length; i++)
            {
                tabContents[i] = root.Q<VisualElement>(TabContentNames[i]);
                tabButtons[i] = root.Q<Button>(TabButtonNames[i]);
                int tabIndex = i;
                tabButtons[i].clicked += () => SetGameTab(tabIndex);
            }
            SetGameTab(0);

            for (int i = 1; i <= 9; i++)
            {
                string gameName = $"게임 {i}";
                root.Q<Button>($"game-box-{i}").clicked += () => OnGameBoxClicked(gameName);
            }

            root.Q<Button>("difficulty-back-button").clicked += () => Hide(difficultyPanel);
            root.Q<Button>("difficulty-easy-button").clicked += () => OnDifficultyClicked("하");
            root.Q<Button>("difficulty-normal-button").clicked += () => OnDifficultyClicked("중");
            root.Q<Button>("difficulty-hard-button").clicked += () => OnDifficultyClicked("상");

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

            Hide(gameTabsPanel);
            Hide(difficultyPanel);
            Hide(selectPanel);
            Hide(settingsPanel);
            Hide(quitPanel);
            ApplyIcon();
            ApplyPendingReturn();
        }

        // 하단 탭(게임/정보/임시1/임시2/임시3)을 바꾼다: 해당 내용만 보이고 그 탭 버튼만 강조.
        private void SetGameTab(int index)
        {
            for (int i = 0; i < tabContents.Length; i++)
            {
                bool active = i == index;
                if (active)
                {
                    Show(tabContents[i]);
                }
                else
                {
                    Hide(tabContents[i]);
                }
                SetTabActive(tabButtons[i], active);
            }
        }

        private static void SetTabActive(Button tab, bool active)
        {
            if (active)
            {
                tab.AddToClassList("tab-active");
            }
            else
            {
                tab.RemoveFromClassList("tab-active");
            }
        }

        // 게임 박스를 눌렀을 때: 난이도 패널을 연다. 게임 3(매트릭스)은 기획상 난이도가 없어서 바로 시작.
        private void OnGameBoxClicked(string gameName)
        {
            selectedGameName = gameName;
            if (gameName == "게임 3")
            {
                StartGame(null);
                return;
            }
            Show(difficultyPanel);
        }

        private void OnDifficultyClicked(string difficulty)
        {
            Debug.Log($"{selectedGameName} - 난이도 {difficulty} 선택됨");
            StartGame(difficulty);
        }

        // 구현된 게임만 씬으로 이동, 나머지는 아무것도 안 함.
        private void StartGame(string difficulty)
        {
            string sceneName = selectedGameName switch
            {
                "게임 1" => "Game1_ItemSort",
                "게임 2" => "Game2_CubeCarve",
                "게임 3" => "Game3_Matrix",
                _ => null,
            };
            if (sceneName == null)
            {
                return;
            }

            LastGameName = selectedGameName;
            LastDifficulty = difficulty;
            SceneManager.LoadScene(sceneName);
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

        // 설정/게임탭/난이도/아이콘 패널을 모두 닫고 메인 메뉴로 돌아간다.
        private void GoToMenu()
        {
            Hide(settingsPanel);
            Hide(gameTabsPanel);
            Hide(difficultyPanel);
            Hide(selectPanel);
        }

        // 미니게임에서 돌아왔을 때 원래 있던 게임 탭/난이도 화면을 다시 열어준다.
        private void ApplyPendingReturn()
        {
            if (PendingReturnTarget == ReturnTarget.None)
            {
                return;
            }

            SetGameTab(0);
            Show(gameTabsPanel);

            if (PendingReturnTarget == ReturnTarget.GameTabsWithDifficulty)
            {
                // 씬이 새로 로드돼서 어떤 게임이었는지 잊어버렸으니 되살려야 난이도 눌렀을 때 그 게임으로 감.
                selectedGameName = LastGameName;
                Show(difficultyPanel);
            }

            PendingReturnTarget = ReturnTarget.None;
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
