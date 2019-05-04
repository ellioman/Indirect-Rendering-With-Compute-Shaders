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
            #pragma surface surf Lambert addshadow halfasview noambient noshadow
            #pragma multi_compile_instancing
            #pragma multi_compile ___ INDIRECT_DEBUG_LOD
            
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                #pragma instancing_options procedural:setup
                
                #include "ShaderInclude_IndirectStructs.cginc"
                uniform uint _ArgsOffset;
                StructuredBuffer<uint> _ArgsBuffer;
                StructuredBuffer<Indirect2x2Matrix> _InstancesDrawMatrixRows01;
                StructuredBuffer<Indirect2x2Matrix> _InstancesDrawMatrixRows23;
                StructuredBuffer<Indirect2x2Matrix> _InstancesDrawMatrixRows45;
                
                void setup()
                {
                    #if defined(SHADER_API_METAL)
                        uint index = unity_InstanceID;
                    #else
                        uint index = unity_InstanceID + _ArgsBuffer[_ArgsOffset];
                    #endif
                    
                    Indirect2x2Matrix rows01 = _InstancesDrawMatrixRows01[index];
                    Indirect2x2Matrix rows23 = _InstancesDrawMatrixRows23[index];
                    Indirect2x2Matrix rows45 = _InstancesDrawMatrixRows45[index];
                    
                    unity_ObjectToWorld = float4x4(rows01.row0, rows01.row1, rows23.row0, float4(0, 0, 0, 1));
                    unity_WorldToObject = float4x4(rows23.row1, rows45.row0, rows45.row1, float4(0, 0, 0, 1));
                }
            #endif
            
            struct Input {
                float2 uv_MainTex;
            };
            
            uniform sampler2D _MainTex;
            uniform sampler2D _BumpMap;
            uniform fixed4 _Color;
            
            // void surf(Input IN, inout SurfaceOutputStandard o) 
            void surf(Input IN, inout SurfaceOutput o) 
            {
                float3 color = _Color;
                #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    #if defined(INDIRECT_DEBUG_LOD)
                        uint off = _ArgsOffset % 15;
                        color = (off == 14) ? float3(0.4, 0.7, 1.0) : ((off == 9) ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0));
                    #endif
                #endif
                o.Albedo = tex2D(_MainTex, IN.uv_MainTex) * color;
                o.Normal = UnpackNormal (tex2D (_BumpMap, IN.uv_MainTex));
                o.Alpha = 1.0;
            }
        ENDCG
    }
    FallBack "Diffuse"
}
