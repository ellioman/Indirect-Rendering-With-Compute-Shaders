#ifndef __HI_Z__
#define __HI_Z__

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

Texture2D _CameraDepthTexture;
SamplerState sampler_CameraDepthTexture;

float4 _MainTex_TexelSize;

Varyings vertex(in Input input)
{
    Varyings output;

    output.vertex = UnityObjectToClipPos(input.vertex.xyz);
    output.uv = input.uv;

    #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0)
        {
            output.uv.y = 1. - input.uv.y;
        }
    #endif

    return output;
}

float4 blit(in Varyings input) : SV_Target
{
    return _CameraDepthTexture.Sample(sampler_CameraDepthTexture, input.uv).r;
}

float4 reduce(in Varyings input) : SV_Target
{
    #if SHADER_API_METAL
        int2 xy = (int2) (input.uv * (_MainTex_TexelSize.zw - 1.));
        float4 texels[2] = {
            float4(_MainTex.mips[0][xy].rg, _MainTex.mips[0][xy + int2(1, 0)].rg),
            float4(_MainTex.mips[0][xy + int2(0, 1)].rg, _MainTex.mips[0][xy + 1].rg)
        };
    
        float4 r = float4(texels[0].rb, texels[1].rb);
        float4 g = float4(texels[0].ga, texels[1].ga);
    #else
        float4 r = _MainTex.GatherRed  (sampler_MainTex, input.uv);
        float4 g = _MainTex.GatherGreen(sampler_MainTex, input.uv);
    #endif
    
        float minimum = min(min(min(r.x, r.y), r.z), r.w);
        float maximum = max(max(max(g.x, g.y), g.z), g.w);
        return float4(minimum, maximum, 1.0, 1.0);
}

#endif
