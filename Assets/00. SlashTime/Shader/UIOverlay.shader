// HUD 이미지(하트, 게이지 막대, 플래시)를 항상 맨 앞에 그리는 UI 셰이더
// - 유니티 기본 UI 셰이더(UI/Default)와 같고, 깊이 비교만 끔 (ZTest Always)
// - 칼이 HUD(1m)보다 가까이 와도 HUD가 가려지지 않음
// - 글자는 TextMeshPro에 있는 "TextMeshPro/Mobile/Distance Field Overlay"를 씀
Shader "TimeSlasher/UIOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        // Mask 컴포넌트가 쓰는 값 (기본 UI 셰이더와 같게 둬야 Mask가 동작함)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"              // 다른 물체를 다 그린 뒤에 그림
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always                         // 깊이 무시: 항상 맨 앞
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO       // VR 양쪽 눈 렌더링용
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;            // 폰트처럼 알파만 있는 텍스처 보정 (UI 시스템이 넣어줌)

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;    // Image의 Color
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                return (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
            }
            ENDCG
        }
    }
}
