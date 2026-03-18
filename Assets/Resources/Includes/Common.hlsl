#ifndef COMMON_INCLUDED
#define COMMON_INCLUDED

#include "UnityCG.cginc"
#include "Lighting.cginc"
#include "HLSLSupport.cginc"

#define sqr(x) (x * x)

uniform int _FrameIndex;
uniform int _Fps;

uniform float _DeltaTime;
uniform float4 _DSimTime;

SamplerState linearRepeatSampler;
SamplerState linearClampSampler;

static const float PI = 3.14159265359;

float copysign(float x, float s)
{
    return (s >= 0.0) ? abs(x) : -abs(x);
}

float dot2(float2 x)
{
    return dot(x, x);
}

float dot2(float3 x)
{
    return dot(x, x);
}

float dot2(float4 x)
{
    return dot(x, x);
}

float save_div(float num, float denom)
{
    bool valid = abs(denom) > 1e-6;
    return lerp(0.0, num / max(abs(denom), 1e-6), (float) valid);
}

float2 save_div(float2 num, float2 denom)
{
    bool2 valid = abs(denom) > 1e-6;
    return lerp((float2) 0.0, num / max(abs(denom), 1e-6), (float2) valid);
}

float3 save_div(float3 num, float3 denom)
{
    bool3 valid = abs(denom) > 1e-6;
    return lerp((float3) 0.0, num / max(abs(denom), 1e-6), (float3) valid);
}

float InverseLerp(float minValue, float maxValue, float v)
{
    return (v - minValue) / max(maxValue - minValue, 0.0001);
}

float Remap(float v, float inMin, float inMax, float outMin, float outMax)
{
    float t = InverseLerp(inMin, inMax, v);
    return lerp(outMin, outMax, t);
}

float3 InverseLerp(float3 minValue, float3 maxValue, float3 v)
{
    return (v - minValue) / (maxValue - minValue);
}

float3 Remap(float3 v, float3 inMin, float3 inMax, float3 outMin, float3 outMax)
{
    float3 t = InverseLerp(inMin, inMax, v);
    return lerp(outMin, outMax, t);
}

// Difference weight between a and b, where the weight decreases linearly from 1 -> 0 over the range of 0% difference to percentThreshold% difference between a and b
// a and b must be positive
float WeightByRelativeDifference(float a, float b, float percentThreshold)
{
    float diff = abs(b - a) / max(abs(a), 1e-6);
    return saturate(1.0 - diff / percentThreshold);
}

float LinearToLuma(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float HenyeyGreenstein(float g, float cosTheta)
{
    float g2 = g * g;
    float denom = pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5);
    return (1.0 - g2) / (4.0 * PI * denom);
}

#endif // COMMON_INCLUDED
