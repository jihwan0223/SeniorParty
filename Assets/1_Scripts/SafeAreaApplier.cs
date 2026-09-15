using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 노치/펀치홀 등 세이프 에어리어 밖으로 UI가 가려지지 않도록 화면 전체 패널들에 여백을 준다.
    [RequireComponent(typeof(UIDocument))]
    public class SafeAreaApplier : MonoBehaviour
    {
        // 화면 전체를 덮는 패널들(메인 메뉴, 설정 등). 각각에 패딩을 준다.
        private VisualElement[] panels;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

        // 화면 전체 패널 목록을 모으고 첫 여백을 적용한다.
        private void OnEnable()
        {
            var documentRoot = GetComponent<UIDocument>().rootVisualElement;
            panels = documentRoot.Children().ToArray();
            Apply();
        }

        // 세이프 에어리어나 해상도가 바뀌면 다시 적용한다.
        private void Update()
        {
            if (Screen.safeArea != lastSafeArea || Screen.width != lastScreenSize.x || Screen.height != lastScreenSize.y)
            {
                Apply();
            }
        }

        // 세이프 에어리어 밖 영역만큼 모든 패널에 패딩을 준다.
        private void Apply()
        {
            lastSafeArea = Screen.safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);

            if (panels.Length == 0 || panels[0].panel == null)
            {
                return;
            }
            var panel = panels[0].panel;

            // 스크린 좌표 두 점의 패널 좌표 차이 = 레퍼런스 해상도 배율까지 반영된 정확한 간격.
            Vector2 Delta(Vector2 fromScreen, Vector2 toScreen) =>
                RuntimePanelUtils.ScreenToPanel(panel, toScreen) - RuntimePanelUtils.ScreenToPanel(panel, fromScreen);

            float left = Delta(Vector2.zero, new Vector2(lastSafeArea.xMin, 0f)).x;
            float right = Delta(new Vector2(lastSafeArea.xMax, 0f), new Vector2(lastScreenSize.x, 0f)).x;
            // UI Toolkit의 y축은 위가 0, Screen.safeArea의 y축은 아래가 0이라 방향이 반대다.
            float top = Delta(new Vector2(0f, lastSafeArea.yMax), new Vector2(0f, lastScreenSize.y)).y;
            float bottom = Delta(Vector2.zero, new Vector2(0f, lastSafeArea.yMin)).y;

            left = Mathf.Max(0f, left);
            right = Mathf.Max(0f, right);
            top = Mathf.Max(0f, top);
            bottom = Mathf.Max(0f, bottom);

            foreach (var panelElement in panels)
            {
                panelElement.style.paddingLeft = left;
                panelElement.style.paddingRight = right;
                panelElement.style.paddingTop = top;
                panelElement.style.paddingBottom = bottom;
            }
        }
    }
}
