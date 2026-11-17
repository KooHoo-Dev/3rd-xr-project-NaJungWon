// 블레이드 타임 중 과일 표면에 그어지는 빛나는 절단선
// - 과일 MeshRenderer의 "두 번째 머티리얼"로 붙어서 과일 메시를 한 번 더 그림 (기존 사과 머티리얼은 그대로)
// - 자른 면(평면)과 가까운 픽셀만 밝게 칠함 → 칼이 지나간 자리에만 선이 생김
// - 갤러리 "디졸브"의 가장자리 발광을 변형: 노이즈 값 대신 "자른 면까지의 거리"를 씀
// - 자른 면 정보(_SlashPlanes, _SlashCount)는 Fruit.cs가 MaterialPropertyBlock으로 과일마다 넣어줌
Shader "TimeSlasher/SlashLine"
{
    Properties
    {
        [HDR] _Color ("선 색", Color) = (1.5, 2.5, 3, 1)
        _Width ("선 두께 (m)", Float) = 0.003
        _Glow ("번짐 두께 (m)", Float) = 0.012
        _GlowStrength ("번짐 세기", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SlashLine"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One      // 더하기 혼합: 과일 색 위에 빛을 더함
            ZWrite Off         // 투명 효과라 깊이는 안 씀
            ZTest LEqual       // 같은 메시를 한 번 더 그리므로 같은 깊이도 통과
            Offset -1, -1      // 과일 표면과 깜빡이며 겹치지 않게 살짝 앞으로
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 한 과일 최대 절단 횟수(SliceSystem.maxCutCount = 3)와 같게
            #define MAX_SLASH 3

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float _Glow;
                float _GlowStrength;
            CBUFFER_END

            // 과일마다 다른 값 (Fruit.cs가 넣어줌)
            // xyz = 자른 면의 방향(법선, 과일 기준 좌표), w = 원점에서 면까지 거리
            float4 _SlashPlanes[MAX_SLASH];
            float _SlashCount;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;   // 과일 기준 좌표 (과일이 돌아도 선이 붙어 다님)
                UNITY_VERTEX_OUTPUT_STEREO       // VR 양쪽 눈 렌더링용
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionOS = IN.positionOS.xyz;
                return OUT;
            }

            // [수학] AI 작성
            // 자른 면마다 "이 픽셀이 면에서 얼마나 떨어졌나"를 재서,
            // 아주 가까우면 선(core), 조금 가까우면 은은한 번짐(glow)으로 밝기를 정함
            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // 과일 크기(스케일)를 곱해 거리를 미터 단위로 맞춤
                float3 axisX = float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20);
                float scale = length(axisX);

                float bright = 0;

                [unroll]
                for (int i = 0; i < MAX_SLASH; i++)
                {
                    float4 plane = _SlashPlanes[i];
                    float dist = abs(dot(plane.xyz, IN.positionOS) - plane.w) * scale;

                    float aa = fwidth(dist);                                   // 계단 현상 방지용 폭
                    float core = 1 - smoothstep(_Width, _Width + aa, dist);    // 가운데 선
                    float glow = (1 - smoothstep(0, _Glow, dist)) * _GlowStrength;   // 주변 번짐

                    float use = (i < _SlashCount) ? 1 : 0;   // 그어진 개수만큼만
                    bright = max(bright, max(core, glow) * use);
                }

                return half4(_Color.rgb * bright, 1);
            }
            ENDHLSL
        }
    }
}
