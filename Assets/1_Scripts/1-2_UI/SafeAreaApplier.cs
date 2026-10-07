using UnityEngine;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 노치/펀치홀 등 세이프 에어리어 밖으로 UI가 가려지지 않도록, UI 문서 맨 바깥(루트)에 여백을 준다.
    // 루트에 주기 때문에 화면(.screen) 전체와 그 안의 절대 위치 요소(뒤로 버튼, 결과창)까지 세이프 에어리어 안으로 들어가고,
    // 각 화면 USS에 적힌 패딩은 그대로 유지된다. 여백 부분은 첫 화면의 배경색으로 칠한다.
    [RequireComponent(typeof(UIDocument))]
    public class SafeAreaApplier : MonoBehaviour
    {
        private VisualElement documentRoot;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

        // 씬이 다시 로드돼도 Update가 처음부터 UI 준비를 기다리도록 초기화.
        private void OnEnable()
        {
            documentRoot = null;
        }

        // UI가 준비되면(런타임 씬 전환 직후엔 몇 프레임 늦게 붙는다) 적용하고,
        // 이후엔 세이프 에어리어나 해상도가 바뀔 때마다 다시 적용한다.
        private void Update()
        {
            if (documentRoot == null)
            {
                var root = GetComponent<UIDocument>().rootVisualElement;
                if (root == null || root.childCount == 0 || root.panel == null || float.IsNaN(root.layout.width))
                {
                    return;
                }
                documentRoot = root;
                documentRoot.style.backgroundColor = documentRoot[0].resolvedStyle.backgroundColor;
                Apply();
                return;
            }

            if (Screen.safeArea != lastSafeArea || Screen.width != lastScreenSize.x || Screen.height != lastScreenSize.y)
            {
                Apply();
            }
        }

        // 세이프 에어리어 밖 영역만큼 루트에 패딩을 준다.
        private void Apply()
        {
            lastSafeArea = Screen.safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            var panel = documentRoot.panel;

            // 스크린 좌표 두 점의 패널 좌표 차이 = 레퍼런스 해상도 배율까지 반영된 정확한 간격.
            Vector2 Delta(Vector2 fromScreen, Vector2 toScreen) =>
                RuntimePanelUtils.ScreenToPanel(panel, toScreen) - RuntimePanelUtils.ScreenToPanel(panel, fromScreen);

            float left = Delta(Vector2.zero, new Vector2(lastSafeArea.xMin, 0f)).x;
            float right = Delta(new Vector2(lastSafeArea.xMax, 0f), new Vector2(lastScreenSize.x, 0f)).x;
            // UI Toolkit의 y축은 위가 0, Screen.safeArea의 y축은 아래가 0이라 방향이 반대다.
            float top = Delta(new Vector2(0f, lastSafeArea.yMax), new Vector2(0f, lastScreenSize.y)).y;
            float bottom = Delta(Vector2.zero, new Vector2(0f, lastSafeArea.yMin)).y;

            documentRoot.style.paddingLeft = Mathf.Max(0f, left);
            documentRoot.style.paddingRight = Mathf.Max(0f, right);
            documentRoot.style.paddingTop = Mathf.Max(0f, top);
            documentRoot.style.paddingBottom = Mathf.Max(0f, bottom);

            // "safe-area-bleed-bottom" 클래스가 붙은 요소는 세이프 에어리어 패딩을 무시하고
            // 화면 맨 아래까지 배경/테두리를 채운다(하단 탭바처럼 가려져도 상관없는 장식 영역용).
            float bleedBottom = -Mathf.Max(0f, bottom);
            documentRoot.Query(className: "safe-area-bleed-bottom").ForEach(e => e.style.marginBottom = bleedBottom);

            // "safe-area-bleed" 클래스가 붙은 요소는 네 방향 모두 세이프 에어리어 패딩을 무시하고
            // 화면 전체를 꽉 채운다(전체화면 영상 보기처럼 노치 영역까지 상관없는 곳에 사용).
            documentRoot.Query(className: "safe-area-bleed").ForEach(e =>
            {
                e.style.marginLeft = -Mathf.Max(0f, left);
                e.style.marginRight = -Mathf.Max(0f, right);
                e.style.marginTop = -Mathf.Max(0f, top);
                e.style.marginBottom = bleedBottom;
            });
        }
    }
}
