Shader "Hidden/VLCUnity/AndroidSRGBToLinear"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _FlipX ("Flip X", Float) = 0
        _FlipY ("Flip Y", Float) = 0
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _FlipX;
            float _FlipY;

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                if (_FlipX > 0.5)
                    uv.x = 1.0 - uv.x;
                if (_FlipY > 0.5)
                    uv.y = 1.0 - uv.y;

                fixed4 color = tex2D(_MainTex, uv);
                color.rgb = GammaToLinearSpace(color.rgb);
                return color;
            }
            ENDCG
        }
    }

    Fallback Off
}
