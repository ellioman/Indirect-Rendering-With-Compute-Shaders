Shader "Unlit/RenderShadowMatrixToTextureShader"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
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

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float _Cascades;

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				// float4x4 mat1 = unity_WorldToShadow[0];
				// float4x4 mat2 = unity_WorldToShadow[1];
				// float4x4 mat3 = unity_WorldToShadow[2];
				// float4x4 mat4 = unity_WorldToShadow[3];

				const float step = 0.25;
				float4x4 mat = unity_WorldToShadow[_Cascades - 1];

				for (int c = 1; c <= 4; c++)
				{
					if (i.uv.x < step*c)
					{
						return mat[c - 1];
					}
				}

				return 0; 
			}
			ENDCG
		}
	}
}
