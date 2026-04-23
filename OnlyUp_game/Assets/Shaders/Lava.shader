Shader "Custom/Lava"
{
    Properties
    {
        _LavaTex ("Lava Texture", 2D) = "white" {}
        _RockTex ("Rock Texture", 2D) = "gray" {}
        _NoiseTex ("Noise Texture", 2D) = "gray" {}

        _Speed1 ("Speed 1", Float) = 0.5
        _Speed2 ("Speed 2", Float) = -0.3

        _Distortion ("Distortion Strength", Float) = 0.1
        _Emission ("Emission Strength", Float) = 2.0
        _MaskBoost ("Lava Visibility", Range(0, 1)) = 0.35

        _Tiling ("Tiling", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _LavaTex;
            sampler2D _RockTex;
            sampler2D _NoiseTex;

            float _Speed1;
            float _Speed2;
            float _Distortion;
            float _Emission;
            float _MaskBoost;
            float _Tiling;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                // Aplicar tiling
                o.uv = v.uv * _Tiling;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float time = _Time.y;

                // Scroll en dos direcciones
                float2 uv1 = i.uv + float2(time * _Speed1, time * _Speed1);
                float2 uv2 = i.uv + float2(time * _Speed2, time * _Speed2);

                // Noise para distorsión
                float noise = tex2D(_NoiseTex, i.uv * 2).r;

                float2 distortion = (noise - 0.5) * _Distortion;

                // Aplicar distorsión
                uv1 += distortion;
                uv2 += distortion;

                // Samplear texturas
                fixed4 lava1 = tex2D(_LavaTex, uv1);
                fixed4 lava2 = tex2D(_LavaTex, uv2);

                // Mezcla de dos flujos
                fixed4 lava = (lava1 + lava2) * 0.5;

                fixed4 rock = tex2D(_RockTex, i.uv);

                // Máscara basada en noise con refuerzo para evitar que la lava desaparezca.
                float mask = smoothstep(0.2, 0.7, noise + _MaskBoost);

                // Mezclar roca y lava
                fixed4 finalColor = lerp(rock, lava, mask);

                // Emission (brillo)
                finalColor.rgb *= _Emission;

                return finalColor;
            }
            ENDCG
        }
    }
}
