
#ifndef TERRAIN_SHADOW_INCLUDED
#define TERRAIN_SHADOW_INCLUDED

#include "../Includes/Common.hlsl"
#include "../Includes/RaymarchingHelpers.hlsl"

Texture2DArray<float> _HeightMapArray;
Texture2DArray<float> _ShadowMapArray;

StructuredBuffer<ChunkData> _Chunks;
StructuredBuffer<BVHNode> _Nodes;
int _NodeCount;
StructuredBuffer<int> _NodeIndices;

uniform uint _ChunkCount;
uniform float _ShadowEpsilon;
uniform int _MaxSteps;
uniform float _DistanceForHit;
uniform float _ShadowmapResolution;
static const float SHADOW_DARKNESS = 0.8f;

uniform uint _UseRaymarchOptimization;
uniform float _SunAngularRadius;
uniform float _ShadowBlendAlpha;
uniform float _ShadowDarkness;

namespace TerrainShadows
{
    float SampleShadowMap(float3 worldPos, int chunkId)
    {
        float result = 1.f;
        ChunkData chunk = _Chunks[chunkId];
        
        if (chunk.heightSlice < 0)
            return result;
        
        float3 localPos = mul(chunk.worldToLocal, float4(worldPos, 1.0)).xyz;

        float2 denom = max(abs(chunk.boundsMax.xz - chunk.boundsMin.xz),
        float2(1e-6, 1e-6));
        
        float2 uv = (localPos.xz - chunk.boundsMin.xz) / denom;
        uv.y = 1 - uv.y;
        
        float2 texelSize = 1.0 / float2(_ShadowmapResolution, _ShadowmapResolution);

        uv = clamp(uv, texelSize * 0.5, 1.0 - texelSize * 0.5);
        
        result = _ShadowMapArray.SampleLevel(
        linearClampSampler,
        float3(uv, chunk.heightSlice),
        0);
        return lerp(1.0, result, _ShadowDarkness);
    }
    
    bool PointInAABB_XZ(float3 p, float3 bmin, float3 bmax)
    {
        return p.x >= bmin.x && p.x <= bmax.x &&
           p.z >= bmin.z && p.z <= bmax.z;
    }

    int GetChunkIndexFromPosition(float3 WP, float minLowRes)
    {
        int lowResFallback = -1;

        float distToCamera = distance(WP, _WorldSpaceCameraPos);
        bool preferHighRes = distToCamera < minLowRes;

        int stack[64];
        int stackPtr = 0;
        stack[stackPtr++] = 0; // root node

        while (stackPtr > 0)
        {
            int nodeIndex = stack[--stackPtr];
            BVHNode node = _Nodes[nodeIndex];

            if (!PointInAABB_XZ(WP, node.aabbMin, node.aabbMax))
                continue;

            // Leaf node
            if (node.primCount > 0)
            {
                for (int i = 0; i < node.primCount; i++)
                {
                    int chunkIndex = _NodeIndices[node.firstPrim + i];
                    ChunkData chunk = _Chunks[chunkIndex];

                    if (chunk.heightSlice < 0)
                        continue;

                    float3 pLocal = mul(chunk.worldToLocal, float4(WP, 1.0)).xyz;

                    if (!PointInAABB_XZ(pLocal, chunk.boundsMin, chunk.boundsMax))
                        continue;

                    bool isHighRes = chunk.isHighRes != 0;

                    // Near camera: immediately return high-res if available.
                    if (preferHighRes && isHighRes)
                    {
                        return chunkIndex;
                    }

                    // Store low-res as fallback, but do not return yet.
                    if (!isHighRes)
                    {
                        lowResFallback = chunkIndex;
                    }

                    // Far from camera: low-res is acceptable immediately.
                    if (!preferHighRes && !isHighRes)
                    {
                        return chunkIndex;
                    }

                    // Far from camera but only high-res exists.
                    // Keep it as fallback if we do not find low-res.
                    if (!preferHighRes && isHighRes && lowResFallback < 0)
                    {
                        lowResFallback = chunkIndex;
                    }
                }
            }
            else
            {
                stack[stackPtr++] = node.leftChild;
                stack[stackPtr++] = node.rightChild;
            }
        }

        return lowResFallback;
    }
    

    bool RayAABBInv(float3 ro, float3 invDir, float3 bmin, float3 bmax, out float tNear)
    {
        float3 t0 = (bmin - ro) * invDir;
        float3 t1 = (bmax - ro) * invDir;

        float3 tmin3 = min(t0, t1);
        float3 tmax3 = max(t0, t1);

        tNear = max(max(tmin3.x, tmin3.y), tmin3.z);
        float tFar = min(min(tmax3.x, tmax3.y), tmax3.z);

        return tFar >= max(tNear, 0.0);
    }
    
    uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    float Rand(inout uint seed)
    {
        seed = Hash(seed);
        // Take lower 24 bits and normalize
        return (seed & 0x00FFFFFFu) / 16777216.0f; // 2^24
    }
    
    void MakeOrthonormalBasis(float3 n, out float3 t, out float3 b)
    {
        float3 up = (abs(n.z) < 0.999f) ? float3(0, 0, 1) : float3(0, 1, 0);
        t = normalize(cross(up, n));
        b = cross(n, t);
    }

    float3 SampleConeDirection(float3 axis, float cosMax, float2 u)
    {
        float cosTheta = lerp(cosMax, 1.0f, u.x);
        float sinTheta = sqrt(max(0.0f, 1.0f - cosTheta * cosTheta));
        float phi = 2.0f * PI * u.y;

        float3 t, b;
        MakeOrthonormalBasis(axis, t, b);

        return normalize(t * (cos(phi) * sinTheta) + b * (sin(phi) * sinTheta) + axis * cosTheta);
    }
      
    bool MarchedBVH(float3 ro, float3 rd, MarchOptions opt, out float hitT, out float hitHeight, out int chunkIndex)
    {
        rd = normalize(rd);
        ro = ro + float3(0, opt.epsilon, 0) + rd * opt.epsilon;

        hitT = 1e20;
        hitHeight = 0.0;
        chunkIndex = -1;
        
        bool anyHit = false;

        int stack[64];
        int stackPtr = 0;
        stack[stackPtr++] = 0;

        float3 rcpDir = 1.0 / max(abs(rd), 1e-8) * sign(rd);
        float closestWorldT = 1e20;
        hitT = 1e20;
        
        while (stackPtr > 0)
        {
            int nodeIndex = stack[--stackPtr];
            BVHNode node = _Nodes[nodeIndex];

            float nodeT;
            if (!RayAABBInv(ro, rcpDir, node.aabbMin, node.aabbMax, nodeT))
                continue;

            if (nodeT > closestWorldT)
                continue;

            if (node.primCount > 0)
            {
                for (int i = 0; i < node.primCount; i++)
                {
                    int testChunkIndex = _NodeIndices[node.firstPrim + i];
                    ChunkData chunk = _Chunks[testChunkIndex];

                    if (chunk.heightSlice < 0)
                        continue;

                    float3 roLocal = mul(chunk.worldToLocal, float4(ro, 1.0)).xyz;
                    float3 rdLocal = normalize(mul((float3x3) chunk.worldToLocal, rd));

                    float chunkT;
                    if (!RayAABB(roLocal, rdLocal, chunk.boundsMin, chunk.boundsMax, chunkT))
                        continue;

                    float2 chunkSize = (chunk.boundsMax - chunk.boundsMin).xz;

                    float testT = 0.0;
                    float testHeight = 0.0;
                    bool hit = false;
                    
                    if(_UseRaymarchOptimization)
                    {
                        hit = TraverseHeightfieldMaxMip(
                            roLocal,
                            rdLocal,
                            testT,
                            testHeight,
                            _DistanceForHit,
                            _HeightMapArray,
                            chunk.heightSlice,
                            chunkSize,
                            1.0,
                            linearClampSampler,
                            _MaxSteps,
                            _ShadowEpsilon,
                            chunk.boundsMin,
                            opt
                            );
                    }
                    else
                    {
                        hit = RaymarchChunk(
                            roLocal,
                            rdLocal,
                            testT,
                            testHeight,
                            _MaxSteps,
                            _DistanceForHit,
                            1e-2,
                            chunkSize,
                            1.0,
                            chunk.boundsMin,
                            _HeightMapArray,
                            chunk.heightSlice,
                            linearClampSampler
                            );
                    }

                    if (hit)
                    {
                        float3 hitPosGrid = roLocal + rdLocal * testT;
                        float3 hitPosWorld = mul(chunk.localToWorld, float4(hitPosGrid, 1.0)).xyz;
                        float testWorldT = distance(ro, hitPosWorld);

                        if (testWorldT < closestWorldT)
                        {
                            closestWorldT = testWorldT;
                            hitT = testWorldT;
                            hitHeight = testHeight;
                            chunkIndex = testChunkIndex;
                            anyHit = true;
                        }
                    }
                }
            }
            else
            {
                if (node.leftChild >= 0)
                    stack[stackPtr++] = node.leftChild;

                if (node.rightChild >= 0)
                    stack[stackPtr++] = node.rightChild;
            }
        }

        return anyHit;
    }
    
    bool MarchedTerrain(float3 ro, float3 rd, out float hitT, out float elevation, out int hitChunkIndex)
    {
        MarchOptions opt;
        opt.isShadow = false;
        opt.useMaxMipOptimization = _UseRaymarchOptimization;
        opt.seedBase = -1.0;
        opt.randomU = 0.0;
        opt.epsilon = _ShadowEpsilon;

        return MarchedBVH(ro, rd, opt, hitT, elevation, hitChunkIndex);
    }
    
    //In my internship code ignoreHighRes and tminLowRes are used to manage chunk LODs, but those are not present in this demo
    bool MarchedShadows(
    float3 ro,
    float3 rd,
    float seedBase,
    float tminLowRes,
    bool ignoreHighRes,
    float2 u)
    {
        MarchOptions opt;
        opt.isShadow = true;
        opt.useMaxMipOptimization = _UseRaymarchOptimization;
        opt.seedBase = seedBase;
        opt.randomU = u;
        opt.epsilon = _ShadowEpsilon;
        float hitT, elevation;
        int hitIndex;
        return MarchedBVH(ro, rd, opt, hitT, elevation, hitIndex);
    }
    
}


#endif
