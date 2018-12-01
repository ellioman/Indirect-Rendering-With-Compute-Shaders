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
		// #pragma surface surf Standard addshadow halfasview noambient
		#pragma surface surf Lambert addshadow halfasview noambient nolppv noshadow
        #pragma multi_compile_instancing

		#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) \
			&& SHADER_TARGET >= 35 \
			&& (defined(SHADER_API_D3D11) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_XBOXONE) || defined(SHADER_API_PSSL) || defined(SHADER_API_SWITCH) || defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && defined(UNITY_COMPILER_HLSLCC)))

			#include "ShaderInclude_IndirectStructs.cginc"
        	#pragma instancing_options procedural:setup
			StructuredBuffer<InstanceDrawData> _InstanceDrawDataBuffer;
			StructuredBuffer<uint> _ArgsBuffer;
			uniform int _ArgsOffset;

			void setup()
			{
				uint id = unity_InstanceID;

				#if defined(SHADER_API_D3D11)
				id += _ArgsBuffer[_ArgsOffset];
				#endif

				InstanceDrawData instance = _InstanceDrawDataBuffer[id];
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
