// 칼 잔상(BladeTrail)과 베기 불꽃(Spark 파티클)이 같이 쓰는 빛나는 셰이더
// - 더하기 혼합: 뒤 화면 색에 빛을 더함 (겹칠수록 밝아짐)
// - 색은 "정점 색 × 머티리얼 색". 잔상은 스크립트가, 파티클은 Start Color가 정점 색을 넣어줌
// - _Round = 1 이면 파티클 한 장을 둥근 빛 방울로, 0 이면 잔상처럼 칼날 끝 쪽이 더 밝게
Shader "TimeSlasher/Glow"
{
    Properties
    {
        _Color ("색", Color) = (1, 1, 1, 1)
        [Toggle] _Round ("둥글게 (파티클용)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One      // 더하기 혼합
            ZWrite Off         // 투명 효과라 깊이는 안 씀
            Cull Off           // 잔상은 앞뒤 어느 쪽에서 봐도 보이게

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Round;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;          // 정점 색 (잔상: 스크립트, 파티클: Start Color)
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO    // VR 양쪽 눈 렌더링용
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                return OUT;
            }

            // [수학] AI 작성
            // 파티클: 가운데가 밝고 가장자리로 갈수록 0이 되는 둥근 방울
            // 잔상: uv.y(0 = 칼날 시작, 1 = 칼날 끝)가 클수록 밝게
            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float round = saturate(1 - length(IN.uv * 2 - 1));
                round = round * round;
                float edge = lerp(0.25, 1, IN.uv.y);
                float shape = (_Round > 0.5) ? round : edge;

                // 더하기 혼합이라 알파 대신 밝기에 곱해서 흐려지게 함
                half3 rgb = IN.color.rgb * _Color.rgb * IN.color.a * _Color.a * shape;
                return half4(rgb, 1);
            }
            ENDHLSL
        }
    }
}
