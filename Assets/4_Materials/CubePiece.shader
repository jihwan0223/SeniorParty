// 큐브 조각 전용. 조각끼리 틈 없이 붙여 놓고, 테두리 선은 각 면의 가장자리(UV 기준)에 직접 그린다.
// 조각마다 따로 윤곽선 껍데기를 씌우던 방식은 조각 사이에 틈이 생겨 선이 어긋나 보였다.
// 조명은 쓰지 않고 면 방향(윗면/왼쪽면/오른쪽면)마다 정해진 밝기로 칠해서, 기기와 에디터에서 똑같이 보인다.
Shader "SeniorParty/CubePiece"
{
    Properties
    {
        [MainColor] _BaseColor ("Color", Color) = (0.86, 0.78, 0.60, 1)
        _LineColor ("Line Color", Color) = (0, 0, 0, 1)
        // 면 한 변을 1로 봤을 때 테두리 선 두께. 맞닿은 두 면의 선이 합쳐져 화면에선 이 두 배로 보인다.
        _LineWidth ("Line Width", Range(0, 0.2)) = 0.025
        // x: 오른쪽 면, y: 윗면, z: 왼쪽 면의 밝기.
        _FaceShade ("Face Shade (x right, y top, z left)", Vector) = (0.74, 1, 0.87, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Piece"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half shade : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _LineColor;
                half4 _FaceShade;
                half _LineWidth;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.shade = dot(abs(IN.normalOS), _FaceShade.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 가장 가까운 면 가장자리까지의 거리. fwidth로 한 픽셀 폭만큼 부드럽게 섞어서 선이 계단지지 않게 한다.
                float2 edge = min(IN.uv, 1.0 - IN.uv);
                float2 aa = fwidth(edge);
                float2 inside = smoothstep(_LineWidth - aa, _LineWidth + aa, edge);
                half fill = min(inside.x, inside.y);
                half3 color = lerp(_LineColor.rgb, _BaseColor.rgb * IN.shade, fill);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
