// Upgrade NOTE: replaced 'UNITY_INSTANCE_ID' with 'UNITY_VERTEX_INPUT_INSTANCE_ID'

Shader "HiZ/Objects" 
{
	Properties{
		_Color("Color", Color) = (1,1,1,1)
		_MainTex("Albedo (RGB)", 2D) = "white" {}
		_MainTexSide("Side/Bottom Texture", 2D) = "white" {}
		_NormalMap("Normal Map", 2D) = "bump" {}
		_NormalScale("Normal Scale", Range(-3,3)) = 1.0
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_Metallic("Metallic", Range(0,1)) = 0.0

		_Scale("Top Scale", Range(-2,2)) = 1
        _SideScale("Side Scale", Range(-2,2)) = 1
        _NoiseScale("Noise Scale", Range(-2,2)) = 1
        _TopSpread("TopSpread", Range(-2,2)) = 1
        _EdgeWidth("EdgeWidth", Range(0,0.5)) = 1
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		CGPROGRAM
		//#pragma surface surf Standard addshadow fullforwardshadows
		#pragma surface surf Standard addshadow
        #pragma multi_compile_instancing
        #pragma instancing_options procedural:setup

		#if SHADER_TARGET >= 35 && (defined(SHADER_API_D3D11) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_XBOXONE) || defined(SHADER_API_PSSL) || defined(SHADER_API_SWITCH) || defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && defined(UNITY_COMPILER_HLSLCC)))
            #define SUPPORT_STRUCTUREDBUFFER
        #endif

        #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(SUPPORT_STRUCTUREDBUFFER)
            #define ENABLE_INSTANCING
        #endif

        uniform half _Glossiness;
		uniform half _Metallic;
		uniform fixed _NormalScale;
		uniform float  _TopSpread;
		uniform float  _EdgeWidth;
		uniform float _NoiseScale;
		uniform float _Scale;
		uniform float _SideScale;
		uniform fixed4 _Color;
		uniform sampler2D _MainTex;
		uniform sampler2D _MainTexSide;
		uniform sampler2D _NormalMap;

		struct Input
		{
			float2 uv_MainTex;
			float2 uv_NormalMap;
			float3 worldPos; // world position built-in value
        	float3 worldNormal; // world normal built-in value
			// uint id;
		};

		#if defined(ENABLE_INSTANCING)
			StructuredBuffer<float4> positionBuffer;
		#endif
		
		void setup()
		{
			// Positions are calculated in the compute shader.
			// Here we just use them.
			#if defined(ENABLE_INSTANCING)
				
				uint index = unity_InstanceID;
				float4 position = positionBuffer[index];
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
			// fixed4 c = tex2D (_MainTex, IN.uv_MainTex);
            // o.Albedo = c.rgb;
            // o.Metallic = _Metallic;
            // o.Smoothness = _Glossiness;
            // o.Alpha = c.a;
			
			// o.Albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
			// o.Normal = UnpackScaleNormal(tex2D(_NormalMap, IN.uv_NormalMap), _NormalScale);
			// // o.Albedo *= IN.uv_NormalMap.r + 1;//lerp(1.0, pnoise(IN.uv_NormalMap, o.Albedo.rg).r, _Test);
			// // o.Albedo = _Color;//float3(IN.uv_MainTex, 0.0);//1 + pnoise(IN.worldPos * 0.01, float2(0.2,0.3));//o.Normal;
			// o.Metallic = _Metallic;
			// o.Smoothness = _Glossiness;
			// o.Alpha = 1.0;

			// clamp (saturate) and increase(pow) the worldnormal value to use as a blend between the projected textures
			float3 blendNormal = saturate(pow(IN.worldNormal * 1.4,4));

			// normal noise triplanar for x, y, z sides
			float3 xn = tex2D(_NormalMap, IN.worldPos.zy * _NoiseScale);
			float3 yn = tex2D(_NormalMap, IN.worldPos.zx * _NoiseScale);
			float3 zn = tex2D(_NormalMap, IN.worldPos.xy * _NoiseScale);

			// lerped together all sides for noise texture
			float3 noisetexture = zn;
			noisetexture = lerp(noisetexture, xn, blendNormal.x);
			noisetexture = lerp(noisetexture, yn, blendNormal.y);

			// triplanar for top texture for x, y, z sides
			float3 xm = tex2D(_MainTex, IN.worldPos.zy * _Scale);
			float3 zm = tex2D(_MainTex, IN.worldPos.xy * _Scale);
			float3 ym = tex2D(_MainTex, IN.worldPos.zx * _Scale);

			// lerped together all sides for top texture
			float3 toptexture = zm;
			toptexture = lerp(toptexture, xm, blendNormal.x);
			toptexture = lerp(toptexture, ym, blendNormal.y);

			// triplanar for side and bottom texture, x,y,z sides
			float3 x = tex2D(_MainTexSide, IN.worldPos.zy * _SideScale);
			float3 y = tex2D(_MainTexSide, IN.worldPos.zx * _SideScale);
			float3 z = tex2D(_MainTexSide, IN.worldPos.xy * _SideScale);

			// lerped together all sides for side bottom texture
			float3 sidetexture = z;
			sidetexture = lerp(sidetexture, x, blendNormal.x);
			sidetexture = lerp(sidetexture, y, blendNormal.y);

			// dot product of world normal and surface normal + noise
			float worldNormalDotNoise = dot(o.Normal + (noisetexture.y + (noisetexture * 0.5)), IN.worldNormal.y);

			// if dot product is higher than the top spread slider, multiplied by triplanar mapped top texture
			// step is replacing an if statement to avoid branching :
			// if (worldNormalDotNoise > _TopSpread{ o.Albedo = toptexture}
			float3 topTextureResult = step(_TopSpread, worldNormalDotNoise) * toptexture;

			// if dot product is lower than the top spread slider, multiplied by triplanar mapped side/bottom texture
			float3 sideTextureResult = step(worldNormalDotNoise, _TopSpread) * sidetexture;

			// if dot product is in between the two, make the texture darker
			float3 topTextureEdgeResult = step(_TopSpread, worldNormalDotNoise) * step(worldNormalDotNoise, _TopSpread + _EdgeWidth) *  -0.15;

			// final albedo color
			o.Albedo = topTextureResult + sideTextureResult + topTextureEdgeResult;
			o.Albedo *= _Color;
		}
	ENDCG
	}
	FallBack "Diffuse"
}
