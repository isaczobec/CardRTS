// Single-image world-space health bar shader — replaces the old two-Image (Background +
// Image.fillAmount Fill) masking approach entirely. The fill itself is computed here from
// _CurrentHealthNormalized (set per-frame-of-change by HealthBarPrefab.SetHealth); _MaxHealth
// is exposed alongside it and used for absolute-HP-spaced segment tick marks (denser ticks on
// a higher max-health bar, at the same tick spacing in HP terms) rather than left unused.
// Boilerplate (stencil/clip-rect/alpha-clip) mirrors Unity's own default UI shader, so this
// still behaves correctly if ever placed under a Mask/RectMask2D.
Shader "UI/HealthBar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Background Tint", Color) = (0.15, 0.15, 0.15, 1)
        _FillColor ("Fill Color", Color) = (0.2, 0.9, 0.2, 1)
        _CurrentHealthNormalized ("Current Health (Normalized)", Range(0, 1)) = 1
        _MaxHealth ("Max Health", Float) = 100

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _FillColor;
            float _CurrentHealthNormalized;
            float _MaxHealth;

            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            // Cosmetic-only: a thin darker line every _HealthPerSegment HP, mapped from
            // absolute HP into this bar's own normalized [0,1] space via _MaxHealth — the
            // one place _MaxHealth actually feeds into the visual, rather than sitting
            // unused alongside _CurrentHealthNormalized. Remove this block entirely (and the
            // two consts below) if segment ticks aren't wanted.
            static const float _HealthPerSegment = 50.0;
            static const float _SegmentLineWidth = 0.004;

            fixed4 frag(v2f IN) : SV_Target
            {
                half filled = step(IN.texcoord.x, _CurrentHealthNormalized);
                fixed4 barColor = lerp(_Color, _FillColor, filled);

                if (_MaxHealth > 0.0)
                {
                    float segmentSpacing = _HealthPerSegment / _MaxHealth;
                    if (segmentSpacing > 0.0001)
                    {
                        float distanceToTick = abs(frac(IN.texcoord.x / segmentSpacing + 0.5) - 0.5) * segmentSpacing;
                        if (distanceToTick < _SegmentLineWidth)
                            barColor.rgb *= 0.6;
                    }
                }

                fixed4 color = barColor * tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                color.a *= IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
