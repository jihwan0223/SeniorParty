using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.Video;
using SeniorParty.UI;

namespace SeniorParty
{
    public class MainMenuController : MonoBehaviour
    {
        // 미니게임 씬에서 돌아올 때 어디로 갈지 알려주는 정적 상태(씬 전환 넘어서 값 유지용).
        public enum ReturnTarget { None, GameTabs }
        public static ReturnTarget PendingReturnTarget = ReturnTarget.None;

        [SerializeField] private AudioMixer audioMixer;

        // 게임 박스 이름별 정보 확인 창에 보여줄 안내 영상. 영상이 없는 게임은 비워둔다.
        [System.Serializable]
        private struct GameVideoEntry
        {
            public string GameName;
            public VideoClip Clip;
        }
        [SerializeField] private List<GameVideoEntry> gameVideoEntries = new();
        private readonly Dictionary<string, VideoClip> gameVideoByName = new();
        private VideoPlayer gameInfoVideoPlayer;
        private RenderTexture gameInfoVideoTexture;

        // GameAudioMixer에 Exposed된 파라미터 이름. 믹서 쪽 이름을 바꾸면 여기도 같이 바꿔야 한다.
        private const string MasterVolumeParam = "MasterVolume";
        private const string BgmVolumeParam = "BGMVolume";
        private const string SfxVolumeParam = "SFXVolume";

        private VisualElement root;
        private VisualElement gameTabsPanel;
        private VisualElement tabViewport;
        private VisualElement tabTrack;
        private VisualElement settingsPanel;
        private VisualElement quitPanel;
        private VisualElement creditsPanel;
        private VisualElement gameInfoPanel;
        private Label gameInfoTitle;
        private Label gameInfoDescription;
        private VisualElement gameInfoImage;
        private VisualElement gameInfoVideoSurface;
        private VisualElement gameInfoFullscreenPanel;
        private Button gameTabsBackButton;
        // 정보 확인 창에서 "시작하기"를 눌렀을 때 실제로 시작할 게임.
        private string pendingGameName;

        // 게임 박스 이름별 소개 제목/설명. 정보 확인 창에 그대로 표시한다.
        private static readonly Dictionary<string, (string Title, string Description)> GameInfoByName = new()
        {
            // 버튼 색과 맞춰서(보관=파랑, 버리기=빨강) 글자에도 같은 색을 입혀 바로 연상되게 한다.
            ["게임 1"] = ("물건 분류", "물건이 하나씩 나옵니다.\n\n보석이면 '<color=#1E5AB4>보관</color>' 버튼을,\n종이쓰레기면 '<color=#D23737>버리기</color>' 버튼을\n손가락으로 눌러주세요."),
            ["게임 2"] = ("주사위 깎기", "구멍 뚫린 모양을 잠깐 보여드립니다.\n\n그 모양을 잘 기억해서\n큐브 조각을 손가락으로 눌러\n<color=#1E5AB4>똑같이 깎아주십시오.</color>"),
            ["게임 3"] = ("순서 기억하기", "칸이 순서대로 반짝입니다.\n\n본 순서 그대로 눌러주십시오.\n<color=#1E5AB4>2번 이상</color> 맞히면 점점 어려워집니다."),
            ["게임 4"] = ("표적 찾기", "비슷한 그림들 중에서\n정해진 그림을 찾는 게임입니다.\n\n위에 적힌 <color=#1E5AB4>모양과 개수</color>를\n화면에서 찾아 눌러주십시오."),
            ["게임 5"] = ("주제와 단어", "주제에 맞게 단어를 선택하는\n게임입니다.\n\n위에 주제가 나오면,\n아래 카드에서 그 단어를\n<color=#1E5AB4>모두 찾아</color> 눌러주십시오."),
            ["게임 6"] = ("도형 회전", "보기로 나온 도형 중에서\n목표 모양과 똑같은\n도형을 찾는 게임입니다.\n\n<color=#1E5AB4>목표 모양을 돌린 도형</color>을\n찾아 눌러주십시오.\n<color=#D23737>뒤집힌 모양은 다른\n도형</color>이니 주의하세요."),
            ["게임 7"] = ("숫자 잇기", "숫자가 흩어져 있는 게임입니다.\n\n<color=#1E5AB4>1부터 순서대로</color> 숫자를 손가락으로\n눌러주세요."),
            ["게임 8"] = ("색깔 찾기", "글자에 적힌 색 이름과\n실제 글자 색깔이 다르게 나옵니다.\n\n글자 뜻은 보지 말고,\n<color=#1E5AB4>글자가 실제로 보이는 색깔과\n같은 색</color> 버튼을 눌러주세요."),
            ["게임 9"] = ("모양 맞추기", "왼쪽 모양을 목표 모양과 똑같이\n만드는 게임입니다.\n\n<color=#1E5AB4>회전 버튼</color> 또는 <color=#1E5AB4>반전 버튼</color>을 눌러\n왼쪽 모양을 돌리거나\n뒤집어서 목표 모양과\n똑같이 만들어주십시오."),
            ["게임 10"] = ("그림 맞추기", "그림의 이름을 찾는 게임입니다.\n\n아래 단어 중에서 <color=#1E5AB4>그림에 맞는 이름</color>을\n손가락으로 눌러주세요."),
        };

        // 게임 탭바(게임/정보): 인덱스로 내용/버튼을 짝지어 관리.
        private static readonly string[] TabContentNames =
            { "tab-content-game", "tab-content-info" };
        private static readonly string[] TabButtonNames =
            { "bottom-tab-game", "bottom-tab-info" };
        private VisualElement[] tabContents;
        private Button[] tabButtons;
        private int currentTabIndex;

        // 탭 화면 드래그 전환: 손가락 따라 트랙(5칸 필름스트립)이 실시간으로 같이 움직이다가,
        // 떼는 순간 가까운 쪽 탭으로 스냅한다. 처음 몇 px는 방향(가로/세로)을 판단하는 데만 쓰고,
        // 세로로 판단되면(스크롤 의도) 그 이후로는 손을 대지 않아 안쪽 ScrollView가 정상 동작한다.
        private const float DragDecisionThreshold = 12f;
        private const float DragCommitFraction = 0.25f;
        private const int TrackAnimDurationMs = 250;
        private Vector2 dragStartPos;
        private int dragPointerId = -1;
        private bool dragDecided;
        private bool dragLockedHorizontal;
        private float dragStartTrackX;
        private float trackOffsetX;
        private IVisualElementScheduledItem trackAnimSchedule;

        // 설정 초기화 시 되돌아갈 값.
        private const float DefaultVolumeSlider = 80f;
        private readonly System.Collections.Generic.List<Slider> volumeSliders = new();

        // 볼륨 설정의 실제 저장소.
        private SaveData saveData;

        // 세이브 로드 + 화면 요소 찾기 + 버튼/슬라이더 이벤트 연결.
        private void OnEnable()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            saveData = SaveManager.Load();

            gameVideoByName.Clear();
            foreach (var entry in gameVideoEntries)
            {
                if (entry.Clip != null)
                {
                    gameVideoByName[entry.GameName] = entry.Clip;
                }
            }
            gameInfoVideoPlayer = gameObject.AddComponent<VideoPlayer>();
            gameInfoVideoPlayer.playOnAwake = false;
            gameInfoVideoPlayer.isLooping = true;
            gameInfoVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            gameInfoVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            gameInfoVideoPlayer.errorReceived += (_, message) => Debug.LogError($"[MainMenuController] 영상 재생 오류: {message}");

            root = GetComponent<UIDocument>().rootVisualElement;

            // 버튼 하나하나에 재생 코드를 안 붙이고, 루트에서 클릭 버블링을 가로채 어떤 버튼이든 누르면 재생.
            root.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button)
                {
                    SoundManager.Instance.PlaySFX("UIClick");
                }
            });

            gameTabsPanel = root.Q<VisualElement>("game-tabs-panel");
            tabViewport = root.Q<VisualElement>("tab-viewport");
            tabTrack = root.Q<VisualElement>("tab-track");
            settingsPanel = root.Q<VisualElement>("settings-panel");
            quitPanel = root.Q<VisualElement>("quit-panel");
            creditsPanel = root.Q<VisualElement>("credits-panel");
            gameInfoPanel = root.Q<VisualElement>("game-info-panel");
            gameInfoTitle = root.Q<Label>("game-info-title");
            gameInfoDescription = root.Q<Label>("game-info-description");
            gameInfoImage = root.Q<VisualElement>("game-info-image");
            gameInfoVideoSurface = root.Q<VisualElement>("game-info-video-surface");
            gameInfoFullscreenPanel = root.Q<VisualElement>("game-info-fullscreen-panel");

            root.Q<Button>("game-info-start-button").clicked += () =>
            {
                Hide(gameInfoPanel);
                StopGameInfoVideo();
                StartGame(pendingGameName);
            };
            root.Q<Button>("game-info-back-button").clicked += () =>
            {
                Hide(gameInfoPanel);
                StopGameInfoVideo();
            };
            root.Q<Button>("game-info-fullscreen-button").clicked += () =>
            {
                // 같은 RenderTexture를 두 요소에 따로 바인딩하면 한쪽이 깨지므로, 영상이 실제로
                // 그려지는 요소 자체를 전체화면 패널로 옮긴다(맨 아래, 닫기 버튼 뒤로).
                gameInfoVideoSurface.RemoveFromClassList("game-info-video-surface");
                gameInfoVideoSurface.AddToClassList("game-info-fullscreen-image");
                gameInfoFullscreenPanel.Insert(0, gameInfoVideoSurface);
                Show(gameInfoFullscreenPanel);
            };
            root.Q<Button>("game-info-fullscreen-close-button").clicked += () =>
            {
                Hide(gameInfoFullscreenPanel);
                gameInfoVideoSurface.RemoveFromClassList("game-info-fullscreen-image");
                gameInfoVideoSurface.AddToClassList("game-info-video-surface");
                gameInfoImage.Insert(0, gameInfoVideoSurface);
            };
            Hide(gameInfoFullscreenPanel);

            gameTabsBackButton = root.Q<Button>("game-tabs-back-button");
            // 탭 내용(게임 박스 등)이 겹쳐서 클릭을 가로채는 걸 막으려고 보일 때마다 맨 위로 올림
            // (숨겨진 상태에서 미리 BringToFront해두는 것만으론 적용이 안 됨 - 보인 다음에 해야 함).
            root.Q<Button>("start-button").clicked += () =>
            {
                Show(gameTabsPanel);
                gameTabsBackButton.BringToFront();
            };
            gameTabsBackButton.clicked += () => Hide(gameTabsPanel);

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

            // 캡처 단계(TrickleDown)에서 먼저 보고, 가로 드래그로 판단될 때만 포인터를 잡아서
            // 트랙을 직접 움직인다. 세로로 판단되면 아무것도 안 해서 안쪽 ScrollView가 정상 동작한다.
            tabViewport.RegisterCallback<PointerDownEvent>(OnTabDragStart, TrickleDown.TrickleDown);
            tabViewport.RegisterCallback<PointerMoveEvent>(OnTabDragMove, TrickleDown.TrickleDown);
            tabViewport.RegisterCallback<PointerUpEvent>(OnTabDragEnd, TrickleDown.TrickleDown);

            for (int i = 1; i <= 10; i++)
            {
                string gameName = $"게임 {i}";
                root.Q<Button>($"game-box-{i}").clicked += () => OnGameBoxClicked(gameName);
            }

            SetupGameBoxScrollbarStyle(root);

            root.Q<Button>("settings-button").clicked += () => Show(settingsPanel);
            root.Q<Button>("menu-exit-button").clicked += GoToMenu;
            // 크레딧은 설정 창을 잠깐 숨기고 띄운다(겹쳐 두면 뒤의 설정 창이 위아래로 삐져나와 보임). 닫으면 설정으로 돌아간다.
            root.Q<Button>("settings-credits-button").clicked += () =>
            {
                Hide(settingsPanel);
                Show(creditsPanel);
            };
            root.Q<Button>("credits-close-button").clicked += CloseCredits;
            // 설정처럼 딤 처리된 바깥 영역을 눌러도 닫힌다.
            creditsPanel.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == creditsPanel)
                {
                    CloseCredits();
                }
            });
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
            Hide(settingsPanel);
            Hide(quitPanel);
            Hide(creditsPanel);
            ApplyPendingReturn();
        }

        // 정보 탭 인덱스(TabContentNames/TabButtonNames와 순서 맞춤).
        private const int InfoTabIndex = 1;

        // 하단 탭(게임/정보)을 바꾼다: 트랙을 그 칸 위치로 애니메이션시키고
        // 그 탭 버튼만 강조한다. 정보 탭으로 들어올 때마다 점수 그래프를 0에서부터 다시 채워 보여준다.
        private void SetGameTab(int index)
        {
            currentTabIndex = index;
            for (int i = 0; i < tabButtons.Length; i++)
            {
                SetTabActive(tabButtons[i], i == index);
            }
            AnimateTrackTo(index);

            if (index == InfoTabIndex)
            {
                ApplyInfoScores(root);
            }
        }

        // 레이아웃이 아직 한 번도 계산되기 전이면 resolvedStyle.width가 NaN일 수 있다(0*NaN=NaN이라
        // 그대로 곱하면 트랙 위치가 영구히 NaN으로 오염됨) - 그런 경우 0으로 취급.
        private float GetViewportWidth()
        {
            float w = tabViewport.resolvedStyle.width;
            return float.IsNaN(w) ? 0f : w;
        }

        // 트랙의 x 위치를 직접 지정(드래그로 손가락 따라 실시간으로 움직일 때 사용, 애니메이션 없음).
        private void SetTrackOffset(float x)
        {
            trackOffsetX = x;
            tabTrack.style.translate = new Translate(x, 0);
        }

        // 지금 위치에서 해당 탭 칸 위치까지 부드럽게(ease-out) 애니메이션.
        private void AnimateTrackTo(int index)
        {
            trackAnimSchedule?.Pause();
            float viewportWidth = GetViewportWidth();
            float startX = trackOffsetX;
            float targetX = -index * viewportWidth;
            float elapsedMs = 0f;

            trackAnimSchedule = tabTrack.schedule.Execute(() =>
            {
                elapsedMs += 16f;
                float t = Mathf.Clamp01(elapsedMs / TrackAnimDurationMs);
                float eased = 1f - (1f - t) * (1f - t);
                SetTrackOffset(Mathf.Lerp(startX, targetX, eased));
                if (t >= 1f)
                {
                    trackAnimSchedule.Pause();
                }
            }).Every(16);
        }

        // 드래그 시작: 포인터 하나만 추적하고, 방향 판단 전 상태로 초기화.
        private void OnTabDragStart(PointerDownEvent evt)
        {
            if (dragPointerId != -1)
            {
                return;
            }
            dragPointerId = evt.pointerId;
            dragStartPos = evt.position;
            dragDecided = false;
            dragLockedHorizontal = false;
            trackAnimSchedule?.Pause();
            dragStartTrackX = trackOffsetX;
        }

        // 임계거리 넘으면 가로/세로 중 어느 쪽 의도인지 한 번만 판단하고, 가로면 트랙을 손가락에 맞춰 끈다.
        private void OnTabDragMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != dragPointerId)
            {
                return;
            }

            Vector2 delta = (Vector2)evt.position - dragStartPos;
            if (!dragDecided)
            {
                if (delta.magnitude < DragDecisionThreshold)
                {
                    return;
                }
                dragDecided = true;
                dragLockedHorizontal = Mathf.Abs(delta.x) > Mathf.Abs(delta.y);
                if (dragLockedHorizontal)
                {
                    // 세로 스크롤(ScrollView)이 같이 반응하지 않도록 이후 포인터 이벤트를 독점.
                    tabViewport.CapturePointer(dragPointerId);
                }
            }

            if (!dragLockedHorizontal)
            {
                return;
            }

            float viewportWidth = GetViewportWidth();
            float minX = -(tabContents.Length - 1) * viewportWidth;
            float newX = Mathf.Clamp(dragStartTrackX + delta.x, minX, 0f);
            SetTrackOffset(newX);
        }

        // 놓은 위치가 다음/이전 탭 쪽으로 DragCommitFraction 이상 넘어갔으면 탭을 전환, 아니면 제자리로.
        private void OnTabDragEnd(PointerUpEvent evt)
        {
            if (evt.pointerId != dragPointerId)
            {
                return;
            }
            bool wasHorizontal = dragLockedHorizontal;
            if (wasHorizontal && tabViewport.HasPointerCapture(dragPointerId))
            {
                tabViewport.ReleasePointer(dragPointerId);
            }
            dragPointerId = -1;

            if (!wasHorizontal)
            {
                return;
            }

            float viewportWidth = GetViewportWidth();
            int newIndex = currentTabIndex;
            if (viewportWidth > 0f)
            {
                float movedPages = (currentTabIndex * -viewportWidth - trackOffsetX) / viewportWidth;
                if (movedPages >= DragCommitFraction)
                {
                    newIndex = currentTabIndex + 1;
                }
                else if (movedPages <= -DragCommitFraction)
                {
                    newIndex = currentTabIndex - 1;
                }
                newIndex = Mathf.Clamp(newIndex, 0, tabContents.Length - 1);
            }
            SetGameTab(newIndex);
        }

        // 정보 탭: 게임별 점수가 아니라 인지영역 5개(기억력/주의력/실행기능/언어능력/시공간능력) 점수.
        // 기획서의 영역-게임 대응: 물건분류=실행기능, 큐브깎기=기억력+시공간(복합), 매트릭스=기억력,
        // 카테고리단어·그림이름=언어능력, 숫자잇기=주의력, 글자색깔=실행기능.
        // 표적찾기(주의력)·회전도형(시공간)은 1~10 난이도를 안 써서 아직 반영 안 됨.
        private const float MinDifficulty = 1f;
        private const float MaxDifficulty = 10f;

        // 선형이 아니라 로그 곡선: 초반 난이도 향상이 점수에 더 크게 반영되고(격려 효과),
        // 고난도로 갈수록 점수 상승폭은 점점 완만해진다. t=0->0점, t=1->100점이 되도록
        // 밑을 (e-1)로 잡아서 자연로그 한 구간(1~e)에 정확히 맞춘다.
        private static float GameScore(float gameDifficulty)
        {
            float t = Mathf.Clamp01((gameDifficulty - MinDifficulty) / (MaxDifficulty - MinDifficulty));
            float score = Mathf.Log(1f + t * (Mathf.Exp(1f) - 1f)) * 100f;
            return Mathf.Clamp(score, 0f, 100f);
        }

        private void ApplyInfoScores(VisualElement root)
        {
            float itemSortScore = GameScore(saveData.itemSortDifficulty);
            float cubeCarveScore = GameScore(saveData.cubeCarveDifficulty);
            float categoryWordScore = GameScore(saveData.categoryWordDifficulty);
            float matrixScore = GameScore(saveData.matrixDifficulty);
            float targetSearchingScore = GameScore(saveData.targetSearchingDifficulty);
            float rotateShapesScore = GameScore(saveData.rotateShapesDifficulty);
            float numberTrailScore = GameScore(saveData.numberTrailDifficulty);
            float colorWordScore = GameScore(saveData.colorWordDifficulty);
            float pictureWordScore = GameScore(saveData.pictureWordDifficulty);

            // 영역 이름을 보고 떠오르는 색: 기억력=보라, 주의력=노랑(경고/집중), 실행기능=초록(실행/Go),
            // 언어능력=파랑(대화), 시공간능력=청록(공간/지도).
            // 추가 3종(숫자 잇기/색깔 찾기/그림 맞추기)도 기획서의 해당 영역에 포함해서 평균낸다.
            SetInfoRing(root, "info-memory-progress", (cubeCarveScore + matrixScore) / 2f, new Color(0.471f, 0.314f, 0.745f));
            SetInfoRing(root, "info-attention-progress", (targetSearchingScore + numberTrailScore) / 2f, new Color(0.824f, 0.627f, 0.078f));
            SetInfoRing(root, "info-executive-progress", (itemSortScore + colorWordScore) / 2f, new Color(0.196f, 0.588f, 0.294f));
            SetInfoRing(root, "info-language-progress", (categoryWordScore + pictureWordScore) / 2f, new Color(0.157f, 0.431f, 0.765f));
            SetInfoRing(root, "info-spatial-progress", (cubeCarveScore + rotateShapesScore) / 2f, new Color(0.039f, 0.549f, 0.569f));
        }

        private static void SetInfoRing(VisualElement root, string ringName, float score, Color progressColor)
        {
            var ring = root.Q<CircularProgress>(ringName);
            if (ring == null)
            {
                return;
            }
            ring.ProgressColor = progressColor;
            ring.MaxValue = 100f;
            ring.Value = 0f;
            ring.AnimateValue(score);
        }

        // 유니티 기본 스크롤바(Scroller)는 스크롤이 필요 없어지면 내부적으로 매 프레임 스스로
        // 비활성화하면서 테두리/배경/visibility를 지워버려서 USS나 코드로 못 이긴다(계속 씌워도
        // 유니티가 그 다음 프레임에 다시 지움). 그래서 기본 스크롤바는 완전히 숨기고(USS에서
        // display:none), 우리가 직접 그리는 장식용 트랙/손잡이(custom-scroll-track/dragger)를
        // 스크롤 위치에 맞춰 갱신한다. 실제 터치 스크롤은 ScrollView가 그대로 처리한다.
        private void SetupGameBoxScrollbarStyle(VisualElement root)
        {
            var scrollView = root.Q<ScrollView>(className: "game-box-scroll");
            var track = root.Q<VisualElement>("custom-scroll-track");
            var dragger = root.Q<VisualElement>("custom-scroll-dragger");
            if (scrollView == null || track == null || dragger == null)
            {
                return;
            }

            // 드래그 중 스크롤량 계산에 같이 써야 해서 마지막으로 계산한 값을 기억해둔다.
            float lastDraggerHeight = 0f;
            float lastMaxScroll = 0f;
            // 손잡이가 트랙 위/아래 끝에 딱 붙거나 튀어나오지 않게 살짝 띄운다.
            const float DraggerEdgeInset = 2f;

            void UpdateDragger()
            {
                float viewportHeight = scrollView.contentViewport.layout.height;
                float contentHeight = scrollView.contentContainer.layout.height;
                float trackHeight = track.layout.height;
                if (float.IsNaN(viewportHeight) || float.IsNaN(contentHeight) || float.IsNaN(trackHeight)
                    || viewportHeight <= 0f || trackHeight <= 0f)
                {
                    return;
                }

                // top/height는 트랙의 패딩 박스(테두리 안쪽) 기준으로 계산되니, 트랙 테두리 두께를
                // 빼줘야 실제 안쪽 높이가 나온다(안 빼면 그만큼 바닥에서 튀어나온다).
                float trackInnerHeight = trackHeight - track.resolvedStyle.borderTopWidth - track.resolvedStyle.borderBottomWidth;
                float travelHeight = Mathf.Max(0f, trackInnerHeight - DraggerEdgeInset * 2f);

                if (contentHeight <= viewportHeight)
                {
                    dragger.style.top = DraggerEdgeInset;
                    dragger.style.height = travelHeight;
                    lastDraggerHeight = travelHeight;
                    lastMaxScroll = 0f;
                    return;
                }

                float draggerHeight = Mathf.Max(travelHeight * (viewportHeight / contentHeight), 40f);
                float maxScroll = contentHeight - viewportHeight;
                float scrollRatio = Mathf.Clamp01(scrollView.scrollOffset.y / maxScroll);
                float draggerY = DraggerEdgeInset + (travelHeight - draggerHeight) * scrollRatio;
                dragger.style.top = draggerY;
                dragger.style.height = draggerHeight;
                lastDraggerHeight = draggerHeight;
                lastMaxScroll = maxScroll;
            }

            scrollView.verticalScroller.slider.RegisterValueChangedCallback(_ => UpdateDragger());
            scrollView.RegisterCallback<GeometryChangedEvent>(_ => UpdateDragger());
            scrollView.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => UpdateDragger());
            track.RegisterCallback<GeometryChangedEvent>(_ => UpdateDragger());
            UpdateDragger();

            // 손잡이를 손가락으로 직접 끌어서 스크롤 조절 가능하게 한다.
            int dragPointerId = -1;
            float dragStartPointerY = 0f;
            float dragStartScrollY = 0f;

            void OnDraggerPointerDown(PointerDownEvent evt)
            {
                if (lastMaxScroll <= 0f)
                {
                    return;
                }
                dragPointerId = evt.pointerId;
                dragStartPointerY = evt.position.y;
                dragStartScrollY = scrollView.scrollOffset.y;
                dragger.CapturePointer(dragPointerId);
                evt.StopPropagation();
            }

            void OnDraggerPointerMove(PointerMoveEvent evt)
            {
                if (dragPointerId != evt.pointerId || !dragger.HasPointerCapture(dragPointerId))
                {
                    return;
                }
                float trackInnerHeight = track.layout.height - track.resolvedStyle.borderTopWidth - track.resolvedStyle.borderBottomWidth;
                float travelHeight = Mathf.Max(0f, trackInnerHeight - DraggerEdgeInset * 2f);
                float travel = Mathf.Max(1f, travelHeight - lastDraggerHeight);
                float deltaY = evt.position.y - dragStartPointerY;
                float newScrollY = dragStartScrollY + deltaY * (lastMaxScroll / travel);
                scrollView.scrollOffset = new Vector2(scrollView.scrollOffset.x, Mathf.Clamp(newScrollY, 0f, lastMaxScroll));
                evt.StopPropagation();
            }

            void OnDraggerPointerUp(PointerUpEvent evt)
            {
                if (dragPointerId != evt.pointerId)
                {
                    return;
                }
                dragger.ReleasePointer(dragPointerId);
                dragPointerId = -1;
                evt.StopPropagation();
            }

            dragger.RegisterCallback<PointerDownEvent>(OnDraggerPointerDown);
            dragger.RegisterCallback<PointerMoveEvent>(OnDraggerPointerMove);
            dragger.RegisterCallback<PointerUpEvent>(OnDraggerPointerUp);
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

        // 게임 박스를 눌렀을 때: 바로 시작하지 않고 정보 확인 창을 먼저 보여준다.
        // 난이도는 게임마다 플레이하면서 자동으로 조절된다.
        private void OnGameBoxClicked(string gameName)
        {
            pendingGameName = gameName;
            (string Title, string Description) info = GameInfoByName.TryGetValue(gameName, out var found) ? found : (gameName, "");
            gameInfoTitle.text = info.Title;
            gameInfoDescription.text = info.Description;
            if (gameVideoByName.TryGetValue(gameName, out VideoClip clip))
            {
                PlayGameInfoVideo(clip);
            }
            else
            {
                StopGameInfoVideo();
            }
            Show(gameInfoPanel);
        }

        // 선택한 게임의 안내 영상을 game-info-video-surface 배경으로 재생(크기가 다르면 RenderTexture를 새로 만든다).
        private void PlayGameInfoVideo(VideoClip clip)
        {
            if (gameInfoVideoTexture == null || gameInfoVideoTexture.width != (int)clip.width || gameInfoVideoTexture.height != (int)clip.height)
            {
                if (gameInfoVideoTexture != null)
                {
                    gameInfoVideoPlayer.targetTexture = null;
                    gameInfoVideoTexture.Release();
                }
                gameInfoVideoTexture = new RenderTexture((int)clip.width, (int)clip.height, 0);
                gameInfoVideoTexture.Create();
                gameInfoVideoPlayer.targetTexture = gameInfoVideoTexture;
            }
            // StopGameInfoVideo가 배경을 비워두므로, 텍스처를 새로 안 만든 경우(같은 크기 영상 재시청)에도
            // 매번 다시 연결해야 박스가 빈 채로 안 남는다.
            gameInfoVideoSurface.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(gameInfoVideoTexture));
            gameInfoVideoPlayer.clip = clip;
            gameInfoVideoPlayer.Play();
        }

        // 영상을 멈추고 배경을 비워 다음에 영상 없는 게임 정보를 열었을 때 이전 영상이 안 남게 한다.
        private void StopGameInfoVideo()
        {
            gameInfoVideoPlayer.Stop();
            gameInfoVideoSurface.style.backgroundImage = StyleKeyword.Null;
        }

        // 구현된 게임만 씬으로 이동, 나머지는 아무것도 안 함.
        private void StartGame(string gameName)
        {
            string sceneName = gameName switch
            {
                "게임 1" => "Game1_ItemSort",
                "게임 2" => "Game2_CubeCarve",
                "게임 3" => "Game3_Matrix",
                "게임 4" => "Game4_TargetSearching",
                "게임 5" => "Game5_CategoryWord",
                "게임 9" => "Game9_RotateGrid",
                "게임 6" => "Game6_RotateShape",
                "게임 7" => "Game7_NumberTrail",
                "게임 8" => "Game8_ColorWord",
                "게임 10" => "Game10_PictureWord",
                _ => null,
            };
            if (sceneName == null)
            {
                return;
            }

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

        // 기본값(DefaultVolumeSlider, 80)에서 0dB(기준 음량)가 되도록 고정하고, 80~100은 기준보다 더 크게 들을 수 있는 부스트 구간으로 둔다.
        private const float MinDb = -80f;
        private const float MaxBoostDb = 10f;

        // 슬라이더 값(0~100) -> dB.
        // 0~DefaultVolumeSlider: 진폭 비율 로그 변환(중간값도 또렷하게 들리도록), DefaultVolumeSlider~100: 0dB 위로 선형 부스트.
        private static float SliderToDecibel(float sliderValue)
        {
            if (sliderValue <= 0f)
            {
                return MinDb;
            }
            if (sliderValue >= DefaultVolumeSlider)
            {
                return (sliderValue - DefaultVolumeSlider) / (100f - DefaultVolumeSlider) * MaxBoostDb;
            }
            return Mathf.Clamp(20f * Mathf.Log10(sliderValue / DefaultVolumeSlider), MinDb, 0f);
        }

        // 모든 볼륨 슬라이더를 기본값으로 되돌린다(값 대입만으로 라벨/믹서/저장까지 콜백이 알아서 처리).
        private void ResetVolumesToDefault()
        {
            foreach (var slider in volumeSliders)
                slider.value = DefaultVolumeSlider;
        }

        // 설정/게임탭 패널을 모두 닫고 메인 메뉴로 돌아간다.
        private void GoToMenu()
        {
            Hide(settingsPanel);
            Hide(gameTabsPanel);
        }

        // 미니게임에서 돌아왔을 때 원래 있던 게임 탭 화면을 다시 열어준다.
        private void ApplyPendingReturn()
        {
            if (PendingReturnTarget == ReturnTarget.None)
            {
                return;
            }

            SetGameTab(0);
            Show(gameTabsPanel);
            gameTabsBackButton.BringToFront();
            PendingReturnTarget = ReturnTarget.None;
        }

        private void CloseCredits()
        {
            Hide(creditsPanel);
            Show(settingsPanel);
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
