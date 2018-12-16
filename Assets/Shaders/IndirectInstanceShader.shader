Shader "IndirectRendering/Instance" 
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
            #pragma surface surf Lambert addshadow halfasview noambient nolppv noshadow
            #pragma multi_compile_instancing
            
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                #pragma instancing_options procedural:setup
                
                #include "ShaderInclude_IndirectStructs.cginc"
                StructuredBuffer<InstanceDrawData> _InstanceDrawDataBuffer;
                Buffer<uint> _ArgsBuffer;
                uniform int _ArgsOffset;
                
                void setup()
                {
                    #if defined(SHADER_API_METAL)
                    InstanceDrawData instance = _InstanceDrawDataBuffer[unity_InstanceID];
                    #else
                    InstanceDrawData instance = _InstanceDrawDataBuffer[unity_InstanceID + _ArgsBuffer[_ArgsOffset]];
                    #endif
                    
                    unity_ObjectToWorld = instance.unity_ObjectToWorld;
                    unity_WorldToObject = instance.unity_WorldToObject;
                }
            #endif
            
            struct Input {
                float2 uv_MainTex;
            };
            
            uniform sampler2D _MainTex;
            sampler2D _BumpMap;
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
