Shader "IndirectRendering/HiZ/Debug"
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

    int _NUM;
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
        float4 output = _MainTex.SampleLevel(sampler_MainTex, input.uv, _LOD);
        return lerp(output.r * 100, output.g, _NUM);
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
