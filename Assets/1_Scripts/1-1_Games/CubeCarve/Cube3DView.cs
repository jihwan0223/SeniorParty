using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 작은 조각 3x3x3 = 27개로 된 큐브를 45도 돌려서(윗면/왼쪽면/오른쪽면이 보이게) 보여준다.
    // CubeView(직접 그리기)와 달리 실제 큐브 오브젝트 27개를 만들어서 전용 카메라로 RenderTexture에
    // 찍고, 그 텍스처를 UI 쪽 VisualElement 배경으로 보여준다(Bind). 터치는 그 카메라 기준으로
    // 레이캐스트해서 어떤 조각인지 판정. 조각 인덱스 = x + 3*y + 9*z (x: 오른쪽 아래, y: 위, z: 왼쪽 아래 방향).
    //
    // "어느 조각이 지금 보이는지"(PickRemoved가 쓰는 IsVisible 등)는 고정된 45도 시점을 그대로
    // 가정한 순수 좌표 계산이라 렌더링 방식과 무관함 — CubeView에 있던 로직을 그대로 가져옴.
    public class Cube3DView : MonoBehaviour
    {
        public const int GridSize = 3;
        public const int PieceCount = GridSize * GridSize * GridSize;

        private const float Sqrt3Half = 0.8660254f;

        // 렌더텍스처 해상도. CubeCarve.uss의 .cube-view(640x740, 비율 0.866:1)를 2배로.
        private const int TextureWidth = 1280;
        private const int TextureHeight = 1480;

        // 조각은 틈 없이 딱 붙여 놓는다(테두리 선은 CubePiece 셰이더가 각 면 가장자리에 그린다).
        private const float PieceSpacing = 1f;

        private static readonly Color PieceColor = new Color(0.86f, 0.78f, 0.60f);
        private static readonly Color PressedColor = new Color(0.95f, 0.45f, 0.25f);
        // 화면(.screen) 배경색과 맞춰서 렌더텍스처의 빈 부분이 자연스럽게 섞이게 한다.
        private static readonly Color BackgroundColor = new Color(0.961f, 0.941f, 0.902f);

        // 방금 지운 뒤 이 시간 안에 다시 누른 건 무시(더블클릭/손 떨림으로 뒤 조각까지 지워지는 것 방지).
        private const long RemoveCooldownMs = 300;
        private const int NoPointer = -1;

        // IsVisible/Layer 계산이 가정하는 시야 방향(보이는 코너 = x,y,z 모두 최댓값(2) 쪽).
        // 카메라를 이 방향 기준으로 배치해야 "보이는 조각" 판정이 실제 화면과 맞는다.
        // 정확한 (1,1,1) 대각선에서 아주 살짝 비대칭으로 틀어져 있다(예전 윤곽선 방식 때 정한 값을 그대로 유지).
        private static readonly Vector3 ViewDirection = new Vector3(1.06f, 1f, 0.94f).normalized;

        // 뒤에서 앞 순서(x+y+z 작은 것부터). PickRemoved/IsVisible 계산에만 쓰인다(렌더링은 3D가 알아서 함).
        private static readonly int[] DrawOrder =
            Enumerable.Range(0, PieceCount).OrderBy(Depth).ToArray();

        // 조각 재질(CubePiece 셰이더: 면 색 + 테두리 선). Shader.Find로 찾으면 씬에서 아무도 안 쓰는 셰이더라 빌드에 안 들어가서
        // 기기에서 null이 된다. 머티리얼 에셋을 씬에서 직접 참조해야 빌드에 포함된다.
        [SerializeField] private Material pieceMaterial;

        // CreatePrimitive가 붙이는 컴포넌트들. 씬 어디에도 메시 오브젝트가 없어서, 타입을 참조해 두지 않으면
        // 빌드의 엔진 코드 스트리핑으로 빠져 기기에서 CreatePrimitive가 실패할 수 있다(Unity 문서 권장 방법).
        internal static readonly System.Type[] PrimitiveComponents = { typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider) };

        private Camera cubeCamera;
        private RenderTexture renderTexture;
        private readonly GameObject[] pieces = new GameObject[PieceCount];
        private readonly Renderer[] pieceRenderers = new Renderer[PieceCount];
        private MaterialPropertyBlock propertyBlock;
        private readonly bool[] removed = new bool[PieceCount];
        // 되돌리기용: 지운 순서를 기록해서 마지막 한 조각만 되살릴 수 있게 한다.
        private readonly List<int> removedOrder = new();

        private VisualElement boundElement;

        // true면 조각을 눌러서 지울 수 있다.
        public bool Interactive { get; set; }

        // 지금 누르고 있는 조각/포인터. 한 번에 포인터 하나만 받는다.
        private int pressedPiece = -1;
        private int pressedPointerId = NoPointer;
        private bool pressInside;
        private long lastRemoveTime = -RemoveCooldownMs;

        // 지울 조각들의 배치: 붙어있게(한 덩어리로 기억돼서 쉬움) / 상관없이 / 흩어지게(따로따로 기억해야 해서 어려움).
        public enum Spread { Clustered, Mixed, Scattered }

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            BuildPieces();
            BuildCamera();
        }

        // 조각 27개를 실제 큐브 오브젝트로 만들어 틈 없이 배치한다.
        private void BuildPieces()
        {
            var material = new Material(pieceMaterial) { color = PieceColor };

            for (int piece = 0; piece < PieceCount; piece++)
            {
                int x = piece % GridSize, y = piece / GridSize % GridSize, z = piece / (GridSize * GridSize);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Piece_{piece}";
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(x - 1f, y - 1f, z - 1f) * PieceSpacing;
                go.AddComponent<CubePieceId>().Index = piece;

                var renderer = go.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                pieces[piece] = go;
                pieceRenderers[piece] = renderer;
            }
        }

        // 큐브 전체가 딱 맞게 들어오도록, 27개 조각의 바운즈를 감싸서 카메라를 자동으로 맞춘다.
        private void BuildCamera()
        {
            var cameraGo = new GameObject("CubeCamera");
            cameraGo.transform.SetParent(transform, false);
            cubeCamera = cameraGo.AddComponent<Camera>();
            cubeCamera.orthographic = true;
            cubeCamera.clearFlags = CameraClearFlags.SolidColor;
            cubeCamera.backgroundColor = BackgroundColor;
            cubeCamera.aspect = (float)TextureWidth / TextureHeight;

            // 실제 UI에서는 이 텍스처가 화면 표시 크기(640x740)로 축소돼서 보이는데, 딱 정확히 세로/가로로
            // 떨어지는 얇은 윤곽선은 축소 샘플링 때 픽셀 사이로 빠져서 안 보일 수 있다. MSAA를 켜서
            // 가장자리를 미리 부드럽게 만들어두면 축소돼도 선이 사라지지 않는다.
            // RGB565(알파 없음)로 만들어서, 깎여서 뚫린 구멍 부분의 배경색이 항상 완전 불투명하게 나오게 한다.
            // 알파가 있으면 카메라가 지운 배경의 알파가 0으로 남아서 UI 뒤(메인 카메라 하늘)가 비쳐 보일 수 있다.
            renderTexture = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.RGB565) { name = "CubeRenderTexture", antiAliasing = 4 };
            cubeCamera.targetTexture = renderTexture;

            Bounds bounds = pieceRenderers[0].bounds;
            foreach (var renderer in pieceRenderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            float distance = bounds.extents.magnitude * 3f;
            cameraGo.transform.position = bounds.center + ViewDirection * distance;
            cameraGo.transform.LookAt(bounds.center);

            // 기본 near/far(0.3~1000)는 이 작은 큐브에 비해 너무 넓어서 depth buffer 정밀도가 낭비되고,
            // 조각과 윤곽선 껍데기처럼 거리 차가 아주 작은 면끼리 z-fighting(깜빡이며 덜 그려짐)이 난다.
            // 큐브가 실제로 있는 거리 범위로 바짝 좁혀서 정밀도를 그 구간에 몰아준다.
            float depthPadding = bounds.extents.magnitude * 1.1f;
            cubeCamera.nearClipPlane = Mathf.Max(0.01f, distance - depthPadding);
            cubeCamera.farClipPlane = distance + depthPadding;

            // 바운즈의 8개 꼭짓점을 카메라 기준 가로/세로 축에 투영해서, 잘리지 않게 실제로 필요한
            // 절반 크기를 구한다(대각선 반지름만으로는 가로/세로 중 한쪽이 잘릴 수 있었음).
            Vector3 right = cameraGo.transform.right;
            Vector3 up = cameraGo.transform.up;
            float neededHalfWidth = 0f;
            float neededHalfHeight = 0f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + new Vector3(
                    (i & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                    (i & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                    (i & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                Vector3 offset = corner - bounds.center;
                neededHalfWidth = Mathf.Max(neededHalfWidth, Mathf.Abs(Vector3.Dot(offset, right)));
                neededHalfHeight = Mathf.Max(neededHalfHeight, Mathf.Abs(Vector3.Dot(offset, up)));
            }

            const float Margin = 1.15f;
            cubeCamera.orthographicSize = Mathf.Max(neededHalfHeight, neededHalfWidth / cubeCamera.aspect) * Margin;
        }

        // UI 쪽 VisualElement에 렌더텍스처를 배경으로 걸고, 그 위에서 터치 이벤트를 받는다.
        public void Bind(VisualElement element)
        {
            boundElement = element;
            element.style.backgroundImage = Background.FromRenderTexture(renderTexture);
            element.RegisterCallback<PointerDownEvent>(OnPointerDown);
            element.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            element.RegisterCallback<PointerUpEvent>(OnPointerUp);
            element.RegisterCallback<PointerCaptureOutEvent>(_ => CancelPress());
        }

        public bool[] GetRemoved() => (bool[])removed.Clone();

        public void SetRemoved(bool[] source)
        {
            System.Array.Copy(source, removed, PieceCount);
            removedOrder.Clear();
            ApplyRemoved();
        }

        public void ClearRemoved()
        {
            CancelPress();
            System.Array.Clear(removed, 0, PieceCount);
            removedOrder.Clear();
            ApplyRemoved();
        }

        // 되돌리기: 가장 마지막에 깎은 조각 하나만 되살린다. 깎은 게 없으면 아무 일도 안 함.
        public void UndoLast()
        {
            if (removedOrder.Count == 0)
            {
                return;
            }
            int last = removedOrder[removedOrder.Count - 1];
            removedOrder.RemoveAt(removedOrder.Count - 1);
            removed[last] = false;
            pieces[last].SetActive(true);
        }

        private void ApplyRemoved()
        {
            for (int i = 0; i < PieceCount; i++)
            {
                pieces[i].SetActive(!removed[i]);
            }
        }

        // UI 요소 안의 터치 좌표(왼쪽 위 기준 픽셀)를 카메라 뷰포트 좌표로 바꿔서 레이캐스트.
        private int PieceAtPointer(Vector2 localPosition)
        {
            float width = boundElement.resolvedStyle.width;
            float height = boundElement.resolvedStyle.height;
            if (width <= 0f || height <= 0f)
            {
                return -1;
            }

            float u = Mathf.Clamp01(localPosition.x / width);
            float v = Mathf.Clamp01(1f - localPosition.y / height);
            var ray = cubeCamera.ViewportPointToRay(new Vector3(u, v, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                var pieceId = hit.collider.GetComponent<CubePieceId>();
                if (pieceId != null)
                {
                    return pieceId.Index;
                }
            }
            return -1;
        }

        // 버튼처럼 동작: 누르면 그 조각만 강조, 뗄 때 아직 그 조각 위면 그때 지운다(미끄러져 나가면 취소).
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!Interactive || pressedPointerId != NoPointer || evt.timestamp - lastRemoveTime < RemoveCooldownMs)
            {
                return;
            }

            int piece = PieceAtPointer(evt.localPosition);
            if (piece < 0)
            {
                return;
            }

            pressedPiece = piece;
            pressedPointerId = evt.pointerId;
            pressInside = true;
            boundElement.CapturePointer(evt.pointerId);
            SetHighlight(piece, true);
        }

        // 누른 채로 처음 조각 밖으로 나가면 강조를 끄고, 다시 들어오면 켠다(떼는 순간 안쪽인지로 확정 판정).
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != pressedPointerId)
            {
                return;
            }

            bool inside = PieceAtPointer(evt.localPosition) == pressedPiece;
            if (inside != pressInside)
            {
                pressInside = inside;
                SetHighlight(pressedPiece, inside);
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != pressedPointerId)
            {
                return;
            }

            if (Interactive && PieceAtPointer(evt.localPosition) == pressedPiece)
            {
                removed[pressedPiece] = true;
                removedOrder.Add(pressedPiece);
                pieces[pressedPiece].SetActive(false);
                SoundManager.Instance.PlaySFX("CubeCarveClick");
                lastRemoveTime = evt.timestamp;
            }
            CancelPress();
        }

        // 누르던 걸 취소(포인터 놓침/캡처 해제 등): 강조 끄고 포인터 캡처를 풀어준다.
        private void CancelPress()
        {
            if (pressedPointerId == NoPointer)
            {
                return;
            }

            int pointerId = pressedPointerId;
            int piece = pressedPiece;
            pressedPointerId = NoPointer;
            pressedPiece = -1;
            if (piece >= 0)
            {
                SetHighlight(piece, false);
            }
            if (boundElement.HasPointerCapture(pointerId))
            {
                boundElement.ReleasePointer(pointerId);
            }
        }

        // MaterialPropertyBlock으로 그 조각 렌더러만 눌림색으로 바꾸거나 원래대로 되돌린다.
        private void SetHighlight(int piece, bool on)
        {
            var renderer = pieceRenderers[piece];
            if (on)
            {
                propertyBlock.SetColor(Shader.PropertyToID("_BaseColor"), PressedColor);
                renderer.SetPropertyBlock(propertyBlock);
            }
            else
            {
                renderer.SetPropertyBlock(null);
            }
        }

        // 실제로 깎아서 만들 수 있는 모양이 되도록, 그때그때 보이는 조각 중에서 count개를 하나씩 지운다.
        // (안 보이는 조각이 지워져 있으면 플레이어가 알 방법이 없으니까)
        // maxLayer: 0 겉면 조각만, 1 한 겹 안쪽까지, 2 제한 없음.
        // spread 조건에 맞는 후보가 없으면 조건 없이 고른다.
        public static bool[] PickRemoved(int count, int maxLayer, Spread spread)
        {
            var result = new bool[PieceCount];
            for (int n = 0; n < count; n++)
            {
                var candidates = Enumerable.Range(0, PieceCount)
                    .Where(i => !result[i] && Layer(i) <= maxLayer && IsVisible(result, i))
                    .ToList();

                if (n > 0 && spread != Spread.Mixed)
                {
                    bool wantTouching = spread == Spread.Clustered;
                    var preferred = candidates.Where(i => TouchesRemoved(result, i) == wantTouching).ToList();
                    if (preferred.Count > 0)
                    {
                        candidates = preferred;
                    }
                }

                if (candidates.Count == 0)
                {
                    break;
                }
                result[candidates[Random.Range(0, candidates.Count)]] = true;
            }
            return result;
        }

        // x+y+z 좌표 합(뒤에서 앞 순서 정렬 기준).
        private static int Depth(int piece) => piece % 3 + piece / 3 % 3 + piece / 9;

        // 몇 겹 안쪽 조각인지(0 = 겉면). 보이는 세 면이 좌표 2 쪽이라 가장 큰 좌표로 판단.
        private static int Layer(int piece) =>
            GridSize - 1 - Mathf.Max(piece % 3, Mathf.Max(piece / 3 % 3, piece / 9));

        // 이미 지운 조각과 면이 맞닿아 있는지.
        private static bool TouchesRemoved(bool[] removedPieces, int piece)
        {
            int x = piece % 3, y = piece / 3 % 3, z = piece / 9;
            return IsRemovedAt(removedPieces, x - 1, y, z) || IsRemovedAt(removedPieces, x + 1, y, z) ||
                   IsRemovedAt(removedPieces, x, y - 1, z) || IsRemovedAt(removedPieces, x, y + 1, z) ||
                   IsRemovedAt(removedPieces, x, y, z - 1) || IsRemovedAt(removedPieces, x, y, z + 1);
        }

        private static bool IsRemovedAt(bool[] removedPieces, int x, int y, int z) =>
            x >= 0 && x < GridSize && y >= 0 && y < GridSize && z >= 0 && z < GridSize &&
            removedPieces[x + GridSize * y + GridSize * GridSize * z];

        // 큐브 중심 기준, 한 변 길이 1일 때 3D 점의 화면(2D 투영) 위치. IsVisible 계산 전용.
        private static readonly Vector2 AxisX = new Vector2(Sqrt3Half, 0.5f);
        private static readonly Vector2 AxisY = new Vector2(0f, -1f);
        private static readonly Vector2 AxisZ = new Vector2(-Sqrt3Half, 0.5f);

        private static Vector2 Project(int x, int y, int z) =>
            (x - 1.5f) * AxisX + (y - 1.5f) * AxisY + (z - 1.5f) * AxisZ;

        // 조각의 보이는 면 하나(0 윗면, 1 왼쪽면, 2 오른쪽면): 시작 모서리 + 두 변.
        private static void GetFace(int piece, int face, out Vector2 origin, out Vector2 u, out Vector2 v)
        {
            int x = piece % 3, y = piece / 3 % 3, z = piece / 9;
            switch (face)
            {
                case 0: origin = Project(x, y + 1, z); u = AxisX; v = AxisZ; break;
                case 1: origin = Project(x, y, z + 1); u = AxisX; v = AxisY; break;
                default: origin = Project(x + 1, y, z); u = AxisZ; v = AxisY; break;
            }
        }

        // 점 d(면 시작 모서리 기준)가 u, v로 만든 평행사변형 안인지.
        private static bool InsideFace(Vector2 d, Vector2 u, Vector2 v)
        {
            float det = u.x * v.y - u.y * v.x;
            float s = (d.x * v.y - d.y * v.x) / det;
            float t = (u.x * d.y - u.y * d.x) / det;
            return s >= 0f && s < 1f && t >= 0f && t < 1f;
        }

        // 2D 투영 점 p(단위 좌표)에 보이는 맨 앞 조각. 없으면 -1. IsVisible 계산 전용.
        private static int PieceAt(bool[] removedPieces, Vector2 p)
        {
            for (int k = DrawOrder.Length - 1; k >= 0; k--)
            {
                int piece = DrawOrder[k];
                if (removedPieces[piece])
                {
                    continue;
                }
                for (int face = 0; face < 3; face++)
                {
                    GetFace(piece, face, out Vector2 origin, out Vector2 u, out Vector2 v);
                    if (InsideFace(p - origin, u, v))
                    {
                        return piece;
                    }
                }
            }
            return -1;
        }

        // 조각이 조금이라도 보이는지. 45도 시점에선 모든 가림 경계가 면을 반으로 가르는 대각선 위에 있어서,
        // 면마다 두 삼각형 중심점만 확인하면 정확하다.
        private static bool IsVisible(bool[] removedPieces, int piece)
        {
            for (int face = 0; face < 3; face++)
            {
                GetFace(piece, face, out Vector2 origin, out Vector2 u, out Vector2 v);
                if (PieceAt(removedPieces, origin + (2f * u + v) / 3f) == piece ||
                    PieceAt(removedPieces, origin + (u + 2f * v) / 3f) == piece)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
