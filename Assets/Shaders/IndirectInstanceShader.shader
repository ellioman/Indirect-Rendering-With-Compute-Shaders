Shader "IndirectRendering/Instance" 
{
	Properties{
		_Color ("Color", Color) = (1,1,1,1)
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		_Glossiness ("Smoothness", Range(0,1)) = 0.5
		_Metallic ("Metallic", Range(0,1)) = 0.0
	}
	
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		CGPROGRAM
		#pragma surface surf Standard addshadow
        #pragma multi_compile_instancing

		#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) \
			&& SHADER_TARGET >= 35 \
			&& (defined(SHADER_API_D3D11) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_XBOXONE) || defined(SHADER_API_PSSL) || defined(SHADER_API_SWITCH) || defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && defined(UNITY_COMPILER_HLSLCC)))

			#include "ShaderInclude_IndirectStructs.cginc"
        	#pragma instancing_options procedural:setup
			StructuredBuffer<OutputData> positionBuffer;

			float4x4 rotationMatrix(float3 axis, float angle)
			{
				axis = normalize(axis);
				float s = sin(angle);
				float c = cos(angle);
				float oc = 1.0 - c;

				return float4x4(
					oc * axis.x * axis.x + c, oc * axis.x * axis.y - axis.z * s, oc * axis.z * axis.x + axis.y * s, 0.0,
					oc * axis.x * axis.y + axis.z * s, oc * axis.y * axis.y + c, oc * axis.y * axis.z - axis.x * s, 0.0,
					oc * axis.z * axis.x - axis.y * s, oc * axis.y * axis.z + axis.x * s, oc * axis.z * axis.z + c, 0.0,
					0, 0, 0,          1.0);
			}

			// https://forum.unity.com/threads/incorrect-normals-on-after-rotating-instances-graphics-drawmeshinstancedindirect.503232/#post-3277479
			float4x4 inverse(float4x4 input)
			{
				#define minor(a,b,c) determinant(float3x3(input.a, input.b, input.c))
				
					float4x4 cofactors = float4x4(
						minor(_22_23_24, _32_33_34, _42_43_44),
						-minor(_21_23_24, _31_33_34, _41_43_44),
						minor(_21_22_24, _31_32_34, _41_42_44),
						-minor(_21_22_23, _31_32_33, _41_42_43),
				
						-minor(_12_13_14, _32_33_34, _42_43_44),
						minor(_11_13_14, _31_33_34, _41_43_44),
						-minor(_11_12_14, _31_32_34, _41_42_44),
						minor(_11_12_13, _31_32_33, _41_42_43),
				
						minor(_12_13_14, _22_23_24, _42_43_44),
						-minor(_11_13_14, _21_23_24, _41_43_44),
						minor(_11_12_14, _21_22_24, _41_42_44),
						-minor(_11_12_13, _21_22_23, _41_42_43),
				
						-minor(_12_13_14, _22_23_24, _32_33_34),
						minor(_11_13_14, _21_23_24, _31_33_34),
						-minor(_11_12_14, _21_22_24, _31_32_34),
						minor(_11_12_13, _21_22_23, _31_32_33)
						);
				#undef minor
				return transpose(cofactors) / determinant(input);
			}

			void setup()
			{
				OutputData instance = positionBuffer[unity_InstanceID];
				float3 position = instance.position;
				float scale = instance.uniformScale;

				float4x4 xRotationMatrix = rotationMatrix(float3(1, 0, 0), radians(instance.rotation.x));
				float4x4 yRotationMatrix = rotationMatrix(float3(0, 1, 0), radians(instance.rotation.y));
				float4x4 zRotationMatrix = rotationMatrix(float3(0, 0, 1), radians(instance.rotation.z));
				float4x4 rotMatrix = mul(zRotationMatrix, mul(yRotationMatrix, xRotationMatrix));

				float4x4 translation = {
					scale, 0, 0, position.x,
					0, scale, 0, position.y,
					0, 0, scale, position.z,
					0, 0, 0, 1
				};

				unity_ObjectToWorld = mul(translation, rotMatrix);
				//unity_WorldToObject = inverse(unity_ObjectToWorld);
			}
        #endif



		struct Input {
			float2 uv_MainTex;
		};

		sampler2D _MainTex;
		half _Glossiness;
		half _Metallic;
		fixed4 _Color;
		
		void surf(Input IN, inout SurfaceOutputStandard o) 
		{
			// Albedo comes from a texture tinted by color
			fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = c.rgb;

			// Metallic and smoothness come from slider variables
			o.Metallic = _Metallic;
			o.Smoothness = _Glossiness;
			o.Alpha = c.a;
		}
	ENDCG
	}
	FallBack "Diffuse"
}
