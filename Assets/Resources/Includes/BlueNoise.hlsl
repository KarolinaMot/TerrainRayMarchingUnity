#ifndef BLUE_NOISE_INCLUDED
#define BLUE_NOISE_INCLUDED

#include "Common.hlsl"

Texture2DArray<float4> _BlueNoise64;
SamplerState sampler_BlueNoise64;

namespace BlueNoise
{
    const static float GOLDEN_RATIO = 1.61803398875;

    float Sample(float2 uv, uint2 dims, uint frameIndex)
    {
        float slice = frameIndex % 64;
    
        float2 tiling = (float2) dims / 64.0;
        uv *= tiling;
    
        return _BlueNoise64.SampleLevel(sampler_BlueNoise64, float3(uv, slice), 0.0).r;
    }
    
    float Fibonacci1D(int i)
    {
        return frac((float(i) + 1.0) * GOLDEN_RATIO);
    }

    float2 Fibonacci2D(int i, int sampleCount)
    {
        return float2(
            ((float) i + 0.5) / (float) sampleCount,
            Fibonacci1D(i)
        );
    }

    float2 SampleDithered(int i, int sampleCount, float2 uv, uint2 dims)
    {
        float2 result = Fibonacci2D(i, sampleCount);
    
        float blueNoise = BlueNoise::Sample(uv, dims, _FrameIndex);
        result.x += blueNoise;
    
        return result;
    }
}

#endif // BLUE_NOISE_INCLUDED
