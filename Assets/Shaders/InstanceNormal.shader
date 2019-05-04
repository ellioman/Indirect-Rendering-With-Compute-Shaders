Shader "IndirectRendering/InstanceNormal" 
{
    Properties{
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _BumpMap ("Bumpmap", 2D) = "bump" {}
    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200
        Cull back
        
        CGPROGRAM
            #pragma surface surf Lambert addshadow halfasview noambient
            #pragma multi_compile_instancing
            
            struct Input {
                float2 uv_MainTex;
            };
            
            uniform sampler2D _MainTex;
            uniform sampler2D _BumpMap;
            uniform fixed4 _Color;
            
            // void surf(Input IN, inout SurfaceOutputStandard o) 
            void surf(Input IN, inout SurfaceOutput o) 
            {
                o.Albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
                o.Normal = UnpackNormal (tex2D (_BumpMap, IN.uv_MainTex));
                o.Alpha = 1.0;
            }
        ENDCG
    }
    FallBack "Diffuse"
}
