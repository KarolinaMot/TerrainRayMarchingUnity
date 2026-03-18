#ifndef NOISE_INCLUDED
#define NOISE_INCLUDED

#include "Common.hlsl"

namespace Noise
{
    // --- HASHES ---
    // Source: Dave Hoskins (https://shadertoy.com/view/4djSRW/)
    // These hashes do not rely on trigenometry functions like sin and cos, as they produce unstable and inconsistent behaviour between GPUs
    // Naming pattern is Hash[NUM_OUT_PARAMS][NUM_IN_PARAMS], so Hash13 takes 3 inputs and returns 1 output
    
    float Hash11(float p)
    {
        p = frac(p * .1031);
        p *= p + 33.33;
        p *= p + p;
        return frac(p);
    }

    float Hash12(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * .1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float Hash13(float3 p3)
    {
        p3 = frac(p3 * .1031);
        p3 += dot(p3, p3.zyx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float Hash14(float4 p4)
    {
        p4 = frac(p4 * float4(.1031, .1030, .0973, .1099));
        p4 += dot(p4, p4.wzxy + 33.33);
        return frac((p4.x + p4.y) * (p4.z + p4.w));
    }

    float2 Hash21(float p)
    {
        float3 p3 = frac(p * float3(.1031, .1030, .0973));
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.xx + p3.yz) * p3.zy);

    }

    float2 Hash22(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.xx + p3.yz) * p3.zy);

    }

    float2 Hash23(float3 p3)
    {
        p3 = frac(p3 * float3(.1031, .1030, .0973));
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.xx + p3.yz) * p3.zy);
    }

    float3 Hash31(float p)
    {
        float3 p3 = frac(p * float3(.1031, .1030, .0973));
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.xxy + p3.yzz) * p3.zyx);
    }

    float3 Hash32(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
        p3 += dot(p3, p3.yxz + 33.33);
        return frac((p3.xxy + p3.yzz) * p3.zyx);
    }

    float3 Hash33(float3 p3)
    {
        p3 = frac(p3 * float3(.1031, .1030, .0973));
        p3 += dot(p3, p3.yxz + 33.33);
        return frac((p3.xxy + p3.yxx) * p3.zyx);

    }

    float4 Hash41(float p)
    {
        float4 p4 = frac(p * float4(.1031, .1030, .0973, .1099));
        p4 += dot(p4, p4.wzxy + 33.33);
        return frac((p4.xxyz + p4.yzzw) * p4.zywx);
    
    }

    float4 Hash42(float2 p)
    {
        float4 p4 = frac(float4(p.xyxy) * float4(.1031, .1030, .0973, .1099));
        p4 += dot(p4, p4.wzxy + 33.33);
        return frac((p4.xxyz + p4.yzzw) * p4.zywx);

    }

    float4 Hash43(float3 p)
    {
        float4 p4 = frac(float4(p.xyzx) * float4(.1031, .1030, .0973, .1099));
        p4 += dot(p4, p4.wzxy + 33.33);
        return frac((p4.xxyz + p4.yzzw) * p4.zywx);
    }

    float4 Hash44(float4 p4)
    {
        p4 = frac(p4 * float4(.1031, .1030, .0973, .1099));
        p4 += dot(p4, p4.wzxy + 33.33);
        return frac((p4.xxyz + p4.yzzw) * p4.zywx);
    }
    
    // --- GRADIENT NOISE ---
    // Produces smooth noise values, aka perlin noise
    
    float TilingGradientNoise(float3 x, float freq)
    {
        freq = round(freq);
        
        // grid
        float3 i = floor(x);
        float3 f = frac(x);
        
        // quintic interpolant
        float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
        
        // gradients
        float3 ga = Hash33(fmod(i + float3(0.0, 0.0, 0.0), freq)) * 2.0 - 1.0;
        float3 gb = Hash33(fmod(i + float3(1.0, 0.0, 0.0), freq)) * 2.0 - 1.0;
        float3 gc = Hash33(fmod(i + float3(0.0, 1.0, 0.0), freq)) * 2.0 - 1.0;
        float3 gd = Hash33(fmod(i + float3(1.0, 1.0, 0.0), freq)) * 2.0 - 1.0;
        float3 ge = Hash33(fmod(i + float3(0.0, 0.0, 1.0), freq)) * 2.0 - 1.0;
        float3 gf = Hash33(fmod(i + float3(1.0, 0.0, 1.0), freq)) * 2.0 - 1.0;
        float3 gg = Hash33(fmod(i + float3(0.0, 1.0, 1.0), freq)) * 2.0 - 1.0;
        float3 gh = Hash33(fmod(i + float3(1.0, 1.0, 1.0), freq)) * 2.0 - 1.0;
    
        // projections
        float va = dot(ga, f - float3(0.0, 0.0, 0.0));
        float vb = dot(gb, f - float3(1.0, 0.0, 0.0));
        float vc = dot(gc, f - float3(0.0, 1.0, 0.0));
        float vd = dot(gd, f - float3(1.0, 1.0, 0.0));
        float ve = dot(ge, f - float3(0.0, 0.0, 1.0));
        float vf = dot(gf, f - float3(1.0, 0.0, 1.0));
        float vg = dot(gg, f - float3(0.0, 1.0, 1.0));
        float vh = dot(gh, f - float3(1.0, 1.0, 1.0));
	
        // interpolation
        return (va +
           u.x * (vb - va) +
           u.y * (vc - va) +
           u.z * (ve - va) +
           u.x * u.y * (va - vb - vc + vd) +
           u.y * u.z * (va - vc - ve + vg) +
           u.z * u.x * (va - vb - ve + vf) +
           u.x * u.y * u.z * (-va + vb + vc - vd + ve - vf - vg + vh))
           * 0.5 + 0.5;
    }
    
    float GradientNoise(float3 x)
    {
        // grid
        float3 i = floor(x);
        float3 f = frac(x);
    
        // quintic interpolant
        float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    
        // gradients
        float3 ga = Hash33(i + float3(0.0, 0.0, 0.0)) * 2.0 - 1.0;
        float3 gb = Hash33(i + float3(1.0, 0.0, 0.0)) * 2.0 - 1.0;
        float3 gc = Hash33(i + float3(0.0, 1.0, 0.0)) * 2.0 - 1.0;
        float3 gd = Hash33(i + float3(1.0, 1.0, 0.0)) * 2.0 - 1.0;
        float3 ge = Hash33(i + float3(0.0, 0.0, 1.0)) * 2.0 - 1.0;
        float3 gf = Hash33(i + float3(1.0, 0.0, 1.0)) * 2.0 - 1.0;
        float3 gg = Hash33(i + float3(0.0, 1.0, 1.0)) * 2.0 - 1.0;
        float3 gh = Hash33(i + float3(1.0, 1.0, 1.0)) * 2.0 - 1.0;
    
        // projections
        float va = dot(ga, f - float3(0.0, 0.0, 0.0));
        float vb = dot(gb, f - float3(1.0, 0.0, 0.0));
        float vc = dot(gc, f - float3(0.0, 1.0, 0.0));
        float vd = dot(gd, f - float3(1.0, 1.0, 0.0));
        float ve = dot(ge, f - float3(0.0, 0.0, 1.0));
        float vf = dot(gf, f - float3(1.0, 0.0, 1.0));
        float vg = dot(gg, f - float3(0.0, 1.0, 1.0));
        float vh = dot(gh, f - float3(1.0, 1.0, 1.0));
	
        // interpolation
        return (va +
           u.x * (vb - va) +
           u.y * (vc - va) +
           u.z * (ve - va) +
           u.x * u.y * (va - vb - vc + vd) +
           u.y * u.z * (va - vc - ve + vg) +
           u.z * u.x * (va - vb - ve + vf) +
           u.x * u.y * u.z * (-va + vb + vc - vd + ve - vf - vg + vh))
           * 0.5 + 0.5;
    }
    
    float TilingGradientNoise(float2 x, float freq)
    {
        freq = round(freq);
        
        // grid
        float2 i = floor(x);
        float2 f = frac(x);
    
        // quintic interpolant
        float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    
        // gradients
        float2 ga = Hash22(fmod(i + float2(0.0, 0.0), freq)) * 2.0 - 1.0;
        float2 gb = Hash22(fmod(i + float2(1.0, 0.0), freq)) * 2.0 - 1.0;
        float2 gc = Hash22(fmod(i + float2(0.0, 1.0), freq)) * 2.0 - 1.0;
        float2 gd = Hash22(fmod(i + float2(1.0, 1.0), freq)) * 2.0 - 1.0;
    
        // projections
        float va = dot(ga, f - float2(0.0, 0.0));
        float vb = dot(gb, f - float2(1.0, 0.0));
        float vc = dot(gc, f - float2(0.0, 1.0));
        float vd = dot(gd, f - float2(1.0, 1.0));
	
        // interpolation
        return (va +
           u.x * (vb - va) +
           u.y * (vc - va) +
           u.x * u.y * (va - vb - vc + vd))
           * 0.5 + 0.5;
    }
    
    float GradientNoise(float2 x)
    {
        // grid
        float2 i = floor(x);
        float2 f = frac(x);
    
        // quintic interpolant
        float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    
        // gradients
        float2 ga = Hash22(i + float2(0.0, 0.0)) * 2.0 - 1.0;
        float2 gb = Hash22(i + float2(1.0, 0.0)) * 2.0 - 1.0;
        float2 gc = Hash22(i + float2(0.0, 1.0)) * 2.0 - 1.0;
        float2 gd = Hash22(i + float2(1.0, 1.0)) * 2.0 - 1.0;
    
        // projections
        float va = dot(ga, f - float2(0.0, 0.0));
        float vb = dot(gb, f - float2(1.0, 0.0));
        float vc = dot(gc, f - float2(0.0, 1.0));
        float vd = dot(gd, f - float2(1.0, 1.0));
	
        // interpolation
        return (va +
           u.x * (vb - va) +
           u.y * (vc - va) +
           u.x * u.y * (va - vb - vc + vd))
           * 0.5 + 0.5;
    }
    
    float GradientNoiseFbm(float3 p, int octaves)
    {
        float G = exp2(-.85);
        float freq = 1.0;
        float amp = 1.;
        float ampSum = 0.0;
        float noise = 0.;
        for (int i = 0; i < octaves; ++i)
        {
            noise += amp * GradientNoise(p * freq);
            ampSum += amp;
            freq *= 2.;
            amp *= G;
        }
        
        return noise / ampSum;
    }
    
    float GradientNoiseFbm(float2 p, int octaves)
    {
        float G = exp2(-.85);
        float freq = 1.0;
        float amp = 1.;
        float ampSum = 0.0;
        float noise = 0.;
        for (int i = 0; i < octaves; ++i)
        {
            noise += amp * GradientNoise(p * freq);
            ampSum += amp;
            freq *= 2.;
            amp *= G;
        }
        
        return noise / ampSum;
    }
    
    float TilingGradientNoiseFbm(float3 p, int octaves, float freq)
    {
        float G = exp2(-.85);
        float amp = 1.;
        float ampSum = 0.0;
        float noise = 0.;
        for (int i = 0; i < octaves; ++i)
        {
            noise += amp * TilingGradientNoise(p * freq, freq);
            ampSum += amp;
            freq *= 2.;
            amp *= G;
        }
        
        return noise / ampSum;
    }
    
    float TilingGradientNoiseFbm(float2 p, int octaves, float freq)
    {
        float G = exp2(-.85);
        float amp = 1.;
        float ampSum = 0.0;
        float noise = 0.;
        for (int i = 0; i < octaves; ++i)
        {
            noise += amp * TilingGradientNoise(p * freq, freq);
            ampSum += amp;
            freq *= 2.;
            amp *= G;
        }
        
        return noise / ampSum;
    }
    
    // --- WORLEY NOISE ---
    // A cell-like noise, can be used to create voronoi diagrams
    
    float WorleyNoise(float3 uv)
    {
        float3 id = floor(uv);
        float3 p = frac(uv);
    
        float minDist = 10000.0;
        for (float x = -1.0; x <= 1.0; x++)
        {
            for (float y = -1.0; y <= 1.0; y++)
            {
                for (float z = -1.0; z <= 1.0; z++)
                {
                    float3 offset = float3(x, y, z);
                    float3 h = Hash33(id + offset);
                    h += offset;
                    float3 d = p - h;
                    minDist = min(minDist, dot(d, d));
                }
            }
        }
    
        return 1.0 - minDist;
    }
    
    float TilingWorleyNoise(float3 uv, float freq)
    {
        freq = round(freq);
        
        float3 id = floor(uv);
        float3 p = frac(uv);
        
        float minDist = 10000.0;
        for (float x = -1.0; x <= 1.0; x++)
        {
            for (float y = -1.0; y <= 1.0; y++)
            {
                for (float z = -1.0; z <= 1.0; z++)
                {
                    float3 offset = float3(x, y, z);
                    float3 h = Hash33(fmod(id + offset + (float3) freq, (float3) freq));
                    h += offset;
                    float3 d = p - h;
                    minDist = min(minDist, dot(d, d));
                }
            }
        }
    
        return 1.0 - minDist;
    }

    float WorleyNoiseFbm(float3 p, int octaves)
    {
        float G = exp2(-.85);
        float freq = 1.0;
        float amp = 1.;
        float ampSum = 0.0;
        float noise = 0.;
        for (int i = 0; i < octaves; ++i)
        {
            noise += amp * WorleyNoise(p * freq);
            ampSum += amp;
            freq *= 2.;
            amp *= G;
        }
    
        return noise / ampSum;
    }
    
    float TilingWorleyNoiseFbm(float3 p, int octaves, float lacunarity, float gain, float freq)
    {
        const float G = gain;
        
        float amp = 1.0;
        float ampSum = 0.0;
        float noise = 0.0;
        for (int i = 0; i < octaves; ++i)
        {
            noise += amp * TilingWorleyNoise(p * freq, freq);
            ampSum += amp;
            freq *= lacunarity;
            amp *= G;
        }
    
        return noise / ampSum;
    }
    
    float TilingWorleyNoiseFbm(float3 p, int octaves, float freq)
    {
        return TilingWorleyNoiseFbm(p, octaves, 2.0, 0.45, freq);
    }
    
    // --- PERLIN WORLEY NOISE ---
    // A combination of gradient and worley noise to create a broccoli like noise pattern
    
    float TilingPerlinWorleyNoise(float3 p, float freq)
    {        
        float perlin = TilingGradientNoise(p * freq, freq);
        
        float worley0 = TilingWorleyNoise(p * freq, freq);
        float worley1 = TilingWorleyNoise(p * freq * 2.0, freq * 2.0);
        float worley2 = TilingWorleyNoise(p * freq * 4.0, freq * 4.0);
        float worleyFbm = worley0 * 0.625 + worley1 * 0.125 + worley2 * 0.25;
        
        return Remap(abs(perlin * 2.0 - 1.0), 0.0, 1.0, worleyFbm, 1.0);
        //return saturate((perlin + worleyFbm * 0.5) / 1.5);
    }
}

#endif // NOISE_INCLUDED