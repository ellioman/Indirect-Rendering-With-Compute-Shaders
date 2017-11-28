// Upgrade NOTE: replaced 'UNITY_INSTANCE_ID' with 'UNITY_VERTEX_INPUT_INSTANCE_ID'

Shader "HierarchicalZOcclusion/Object" 
{
	Properties{
		_Color("Color", Color) = (1,1,1,1)
		_MainTex("Albedo (RGB)", 2D) = "white" {}
		_NormalMap("Normal Map", 2D) = "bump" {}
		_NormalScale("Normal Scale", Range(-3,3)) = 1.0
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_Metallic("Metallic", Range(0,1)) = 0.0
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		CGPROGRAM
		#pragma surface surf Standard addshadow fullforwardshadows
		#pragma multi_compile_instancing
		#pragma instancing_options procedural:setup

		#if SHADER_TARGET >= 35 && (defined(SHADER_API_D3D11) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_XBOXONE) || defined(SHADER_API_PSSL) || defined(SHADER_API_SWITCH) || defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && defined(UNITY_COMPILER_HLSLCC)))
            #define SUPPORT_STRUCTUREDBUFFER
        #endif

        #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(SUPPORT_STRUCTUREDBUFFER)
            #define ENABLE_INSTANCING
        #endif

        half _Glossiness;
		half _Metallic;
		fixed4 _Color;
		fixed _NormalScale;
		sampler2D _MainTex;
		sampler2D _NormalMap;

		struct Input
		{
			float2 uv_MainTex;
			float2 uv_NormalMap;
			uint id;
		};

		#if defined(ENABLE_INSTANCING)
			StructuredBuffer<float4> positionBuffer;
			StructuredBuffer<float4> colorBuffer;
		#endif

		void setup()
		{
			// Positions are calculated in the compute shader.
			// Here we just use them.
			#if defined(ENABLE_INSTANCING)
				float4 position = positionBuffer[unity_InstanceID];
				float scale = position.w; 

				unity_ObjectToWorld._11_21_31_41 = float4(scale, 0, 0, 0);
				unity_ObjectToWorld._12_22_32_42 = float4(0, scale, 0, 0);
				unity_ObjectToWorld._13_23_33_43 = float4(0, 0, scale, 0);
				unity_ObjectToWorld._14_24_34_44 = float4(position.xyz, 1);
				unity_WorldToObject = unity_ObjectToWorld;
				unity_WorldToObject._14_24_34 *= -1;
				unity_WorldToObject._11_22_33 = 1.0f / unity_WorldToObject._11_22_33;
			#endif
		}

		void surf(Input IN, inout SurfaceOutputStandard o) 
		{
		    float4 col = _Color;
            #if defined(ENABLE_INSTANCING)
                col.rgb *= saturate(colorBuffer[unity_InstanceID].rgb);
            #endif
        
			o.Albedo = tex2D(_MainTex, IN.uv_MainTex) * col;
			o.Normal = UnpackScaleNormal(tex2D(_NormalMap, IN.uv_NormalMap), _NormalScale);
			o.Metallic = _Metallic;
			o.Smoothness = _Glossiness;
			o.Alpha = 1.0;
		}
	ENDCG
	}
	FallBack "Diffuse"
}
