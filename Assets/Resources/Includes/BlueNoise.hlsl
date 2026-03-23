#ifndef BLUE_NOISE_INCLUDED
#define BLUE_NOISE_INCLUDED

Texture2DArray<float> _BlueNoise64;
SamplerState sampler_BlueNoise64;

namespace BlueNoise
{
    float Sample(float2 uv, uint2 dims, uint frameIndex)
    {
        float slice = frameIndex % 64;
    
        float2 tiling = (float2) dims / 64.0;
        uv *= tiling;
    
        return _BlueNoise64.SampleLevel(sampler_BlueNoise64, float3(uv, slice), 0.0).r;
    }
    
    
}

#endif // BLUE_NOISE_INCLUDED
