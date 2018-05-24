Shader "HiZ/DebugViewer"
{
    Properties
    {
       _MainTex ("Texture", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    struct Input
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Varyings
    {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    Texture2D _MainTex;
    SamplerState sampler_MainTex;

    float4 _MainTex_TexelSize;
Texture2D _CameraDepthTexture;
SamplerState sampler_CameraDepthTexture;

    int _LOD;

    Varyings vertex(Input input)
    {
        Varyings output;

        output.vertex = UnityObjectToClipPos(input.vertex.xyz);
        output.uv = input.uv;

    #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0)
            output.uv.y = 1. - input.uv.y;
    #endif

        return output;
    }

    float4 fragment(in Varyings input) : SV_Target
    {
        //return _CameraDepthTexture.Sample(sampler_CameraDepthTexture, input.uv).r;
        return _MainTex.SampleLevel(sampler_MainTex, input.uv, _LOD).g * 100.0;
        
        //float2 rg = _MainTex.SampleLevel(sampler_MainTex, input.uv, _LOD).rg;
        //return lerp(float4(rg.r, 0., 0., 1.), float4(0., rg.g, 0., 1.), step(.5, input.uv.x));
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vertex
            #pragma fragment fragment
            ENDCG
        }
    }
}
