Shader "Local/BirchBattleScenery"
{
    Properties
    {
        _MainTex ("Foreground", 2D) = "white" {}
        _Softness ("Foreground softness in texels", Float) = 2.2
        _Mist ("Mist opacity", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-80" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Softness;
            float _Mist;

            fixed4 PremultipliedSample(float2 uv)
            {
                fixed4 sampleColor = tex2D(_MainTex, uv);
                sampleColor.rgb *= sampleColor.a;
                return sampleColor;
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                if (_Mist > 0)
                {
                    float drift = _Time.y * .23;
                    float center = .5 + .026 * sin(uv.x * 8 + drift) + .019 * sin(uv.x * 16 - drift);
                    float band = 1 - smoothstep(.015, .13, abs(uv.y - center));
                    float variation = .63 + .20 * sin(uv.x * 12 - drift) + .12 * sin(uv.x * 30 + uv.y * 7 + drift);
                    fixed alpha = band * variation * _Mist;
                    return fixed4(fixed3(.63, .75, .77) * alpha, alpha);
                }

                float2 offset = _MainTex_TexelSize.xy * _Softness;
                return PremultipliedSample(uv) * .4
                    + .15 * (PremultipliedSample(uv + float2(offset.x, 0)) + PremultipliedSample(uv - float2(offset.x, 0))
                    + PremultipliedSample(uv + float2(0, offset.y)) + PremultipliedSample(uv - float2(0, offset.y)));
            }
            ENDCG
        }
    }
}
