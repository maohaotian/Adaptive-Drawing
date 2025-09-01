Shader "Custom/MagnifierSample"
{
    
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Center ("Center", Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Radius", Float) = 0.3
        _Scale ("Scale", Float) = 2.0
        _UVScale ("UV Scale", Vector) = (1.0, 1.0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2 _Center;
            float _Radius;
            float _Scale;
            float2 _UVScale;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 delta = -(uv - float2(0.5, 0.5));
                float dist = length(delta);
                float2 uvMagnified = _Center + (delta * _UVScale) / _Scale;

                if (dist > _Radius||uvMagnified.x < 0.0 || uvMagnified.x > 1.0 || uvMagnified.y < 0.0 || uvMagnified.y > 1.0)
                    return fixed4(0,0,0,0); // 圆外透明

                fixed4 col = tex2D(_MainTex, uvMagnified);
                return col;
            }
            ENDCG
        }
    }
}