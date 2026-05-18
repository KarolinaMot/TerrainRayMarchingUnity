#ifndef RMARCH_HELPER
#define RMARCH_HELPER

struct ChunkData
{
    float4x4 worldToLocal;
    float4x4 localToWorld;
    float3 boundsMin;
    float padding0;
    float3 boundsMax;
    float padding1;
    float3 offset;
    int heightSlice;
};

struct BVHNode
{
    float3 aabbMin;
    int leftChild;
    float3 aabbMax;
    int rightChild;
    int firstPrim;
    int primCount;
};

bool RayAABB(
    float3 ro,
    float3 rd,
    float3 bmin,
    float3 bmax, out float tEnter)
{
    
    float3 invRd = 1.0 / rd;

    float3 t0 = (bmin - ro) * invRd;
    float3 t1 = (bmax - ro) * invRd;

    float3 tmin3 = min(t0, t1);
    float3 tmax3 = max(t0, t1);

    tEnter = max(max(tmin3.x, tmin3.y), tmin3.z);
    float tExit = min(min(tmax3.x, tmax3.y), tmax3.z);

    return tExit >= max(tEnter, 0.0);
}



bool GetBoundsExit(
    float3 ro,
    float3 rd,
    float2 minPos,
    float2 maxPos,
    out float tEnter,
    out float tExit)
{
    tEnter = -1e20;
    tExit = 1e20;

    const float EPSILON = 1e-8;

    float2 safeDir = rd.xz;

    if (abs(safeDir.x) < EPSILON)
        safeDir.x = EPSILON;

    if (abs(safeDir.y) < EPSILON)
        safeDir.y = EPSILON;

    float2 invDir = 1.0 / safeDir;

    float2 t0 = (minPos - ro.xz) * invDir;
    float2 t1 = (maxPos - ro.xz) * invDir;

    float2 tNear = min(t0, t1);
    float2 tFar = max(t0, t1);

    tEnter = max(tNear.x, tNear.y);
    tExit = min(tFar.x, tFar.y);

    return tExit >= max(tEnter, 0.0);
}


uint2 GetMipSize(int2 baseSize, int mip)
{
    return max(uint2(1u, 1u), baseSize >> mip);
}

float SampleTerrainHeightChunk(
    float2 xz,
    float2 chunkSize,
    float heightScale,
    Texture2DArray<float> heightmap,
    int slice,
    SamplerState linearClampSampler)
{
    float2 safeChunkSize = max(abs(chunkSize), float2(1e-6, 1e-6));
    float2 uv = xz / safeChunkSize;

    uv = clamp(uv, float2(0.0, 0.0), float2(1.0, 1.0));

    return heightmap.SampleLevel(
        linearClampSampler,
        float3(uv, slice),
        0
    ) * heightScale;
}

float LoadMipHeight(int2 texel, int mip, Texture2D<float> heightmap)
{
    return heightmap.Load(int3(texel, mip));
}

float LoadMipHeightChunk(int2 texel, int mip, Texture2DArray<float> heightmap, int slice)
{
    return heightmap.Load(int4(texel, slice, mip));
}

void InitializeDDA(
    out float2 deltaT,
    out float t_y,
    out float t_x,
    float2 cellDimension,
    float2 rayOriginXZ,
    float2 rayDirectionXZ,
    float2 gridOrigin)
{
    float2 localPos = rayOriginXZ - gridOrigin;

    if (abs(rayDirectionXZ.x) < 1e-8)
    {
        deltaT.x = 1e30;
        t_x = 1e30;
    }
    else if (rayDirectionXZ.x < 0)
    {
        deltaT.x = cellDimension.x / abs(rayDirectionXZ.x);
        float boundary = floor(localPos.x / cellDimension.x) * cellDimension.x + gridOrigin.x;
        t_x = (boundary - rayOriginXZ.x) / rayDirectionXZ.x;
    }
    else
    {
        deltaT.x = cellDimension.x / abs(rayDirectionXZ.x);
        float boundary = (floor(localPos.x / cellDimension.x) + 1.0) * cellDimension.x + gridOrigin.x;
        t_x = (boundary - rayOriginXZ.x) / rayDirectionXZ.x;
    }

    if (abs(rayDirectionXZ.y) < 1e-8)
    {
        deltaT.y = 1e30;
        t_y = 1e30;
    }
    else if (rayDirectionXZ.y < 0)
    {
        deltaT.y = cellDimension.y / abs(rayDirectionXZ.y);
        float boundary = floor(localPos.y / cellDimension.y) * cellDimension.y + gridOrigin.y;
        t_y = (boundary - rayOriginXZ.y) / rayDirectionXZ.y;
    }
    else
    {
        deltaT.y = cellDimension.y / abs(rayDirectionXZ.y);
        float boundary = (floor(localPos.y / cellDimension.y) + 1.0) * cellDimension.y + gridOrigin.y;
        t_y = (boundary - rayOriginXZ.y) / rayDirectionXZ.y;
    }
}


bool TraverseHeightfieldMaxMipChunk(
    float3 ro,
    float3 rd,
    out float hitT,
    out float hitHeight,
    float distanceForHit, Texture2DArray<float> heightmap, int slice, float2 chunkSize, float heightScale, SamplerState linearClampSampler, int maxSteps, float3 offset)
{
    uint4 dimensions;
    heightmap.GetDimensions(0, dimensions.x, dimensions.y, dimensions.w,  dimensions.z);

    hitT = 0.0;
    hitHeight = 0.0;

    float tEnterGlobal = 0, tExitDomain = 0;
    if (!GetBoundsExit(ro, rd, offset.xz, offset.xz + chunkSize, tEnterGlobal, tExitDomain))
        return false;

    hitT = max(tEnterGlobal, 0.0);
    float maxT = tExitDomain;

    float3 p0 = ro + rd * hitT;
    float terrainY0 = SampleTerrainHeightChunk(p0.xz, chunkSize, heightScale, heightmap, slice, linearClampSampler);
    float prevH = p0.y - terrainY0;

    // Start below terrain: do not render/hit underside.
    if (prevH < 0.0)
        return false;

    float2 mip0Dimension = GetMipSize(dimensions.xy, 0);
    float mip0cellDimension = chunkSize / float2(mip0Dimension);
    float e = 0.8f;

    int mip = max((int) dimensions.z - 8, 0);
    uint2 mipSize = GetMipSize(dimensions.xy, mip);
    float2 cellDimension = chunkSize / float2(mipSize);

    float tStartGlobal = max(tEnterGlobal, 0.0);
    float tRemaining = tExitDomain - tStartGlobal;

    float3 rayOriginInGrid = ro + rd * tStartGlobal;
    float tBase = tStartGlobal; // global ray-time of current local origin

    float2 deltaT = float2(1e20, 1e20);
    float t_x = 1e20;
    float t_y = 1e20;
    float t = 0.0;
    float tEnter = 0.0;
    float tExit = 0.0;

    InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);

    for (int i = 0; i < maxSteps && t < tRemaining; i++)
    {
        float3 p = rayOriginInGrid + rd * t;

        float2 uv = (p.xz - offset.xz) / chunkSize;
        float rawHeight = heightmap.SampleLevel(linearClampSampler, float3(uv, slice), 0).r;
        int2 cell = clamp((int2) floor(uv * float2(mipSize)), int2(0, 0), int2(mipSize) - 1);
        float cellHeight = LoadMipHeightChunk(cell, mip, heightmap, slice) * heightScale;
        
        if (t_x < t_y)
        {
            tExit = t_x + e;
            t_x += deltaT.x;
        }
        else
        {
            tExit = t_y + e;
            t_y += deltaT.y;
        }

        tExit = min(tExit, tRemaining);

        float y0 = rayOriginInGrid.y + rd.y * tEnter;
        float y1 = rayOriginInGrid.y + rd.y * tExit;
        float segMinY = min(y0, y1);

        if (mip > 0)
        {
            if (segMinY > cellHeight + distanceForHit)
            {
                tEnter = tExit;
                t = tEnter;
            }
            else
            {
                float consumed = t;

                mip--;
                mipSize = GetMipSize(dimensions.xy, mip);
                cellDimension = chunkSize / float2(mipSize);

                rayOriginInGrid = p;
                tBase += consumed;
                tRemaining -= consumed;

                t = 0.0;
                tEnter = 0.0;

                InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);
            }
        }
        else
        {
            if (segMinY <= cellHeight + distanceForHit)
            {
                float ta = tEnter;
                float tb = tExit;

                float3 pa = rayOriginInGrid + rd * ta;
                float3 pb = rayOriginInGrid + rd * tb;

                float ha = SampleTerrainHeightChunk(pa.xz, chunkSize, heightScale, heightmap, slice, linearClampSampler);
                float hb = SampleTerrainHeightChunk(pb.xz, chunkSize, heightScale, heightmap, slice, linearClampSampler);

                float ga = pa.y - ha;
                float gb = pb.y - hb;

                // Already close enough at entry
                if (ga <= distanceForHit)
                {
                    hitT = tBase + ta;
                    hitHeight = ha;
                    return true;
                }

                bool bracketed = (ga > distanceForHit && gb <= distanceForHit);

                // If endpoints do not bracket, try a few probes inside the interval
                if (!bracketed)
                {
                    float prevT = ta;
                    float prevG = ga;

                    [loop]
                    for (int k = 1; k <= 4; k++)
                    {
                        float s = (float) k / 5.0;
                        float tp = lerp(ta, tb, s);
                        float3 pp = rayOriginInGrid + rd * tp;
                        float hp = SampleTerrainHeightChunk(pp.xz, chunkSize, heightScale, heightmap, slice, linearClampSampler);
                        float gp = pp.y - hp;

                        if (prevG > distanceForHit && gp <= distanceForHit)
                        {
                            ta = prevT;
                            tb = tp;
                            ha = SampleTerrainHeightChunk((rayOriginInGrid + rd * ta).xz, chunkSize, heightScale, heightmap, slice, linearClampSampler);
                            hb = hp;
                            ga = prevG;
                            gb = gp;
                            bracketed = true;
                            break;
                        }

                        prevT = tp;
                        prevG = gp;
                    }
                }

                if (bracketed)
                {
                    [loop]
                    for (int j = 0; j < 8; j++)
                    {
                        float tm = 0.5 * (ta + tb);
                        float3 pm = rayOriginInGrid + rd * tm;
                        float hm = SampleTerrainHeightChunk(pm.xz, chunkSize, heightScale, heightmap, slice, linearClampSampler);
                        float gm = pm.y - hm;

                        if (gm <= distanceForHit)
                        {
                            tb = tm;
                            hb = hm;
                        }
                        else
                        {
                            ta = tm;
                            ha = hm;
                        }
                    }

                    hitT = tBase + tb;
                    hitHeight = hb;
                    return true;
                }
            }

            // No actual hit found in this leaf interval, so advance past it
            float consumed = tExit;

            int topMip = max((int) dimensions.z - 8, 0);
            mip = min(mip + 1, topMip);
            mipSize = GetMipSize(dimensions.xy, mip);
            cellDimension = chunkSize / float2(mipSize);

            float3 pExit = rayOriginInGrid + rd * consumed;
            rayOriginInGrid = pExit;
            tBase += consumed;
            tRemaining -= consumed;

            t = 0.0;
            tEnter = 0.0;
            tExit = 0.0;

            InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);
        }
    }

    return false;
}

bool TraverseHeightfieldMaxMipShadowChunk(
    float3 ro,
    float3 rd,
    out float hitT,
    out float hitHeight,
    float distanceForHit,
    Texture2DArray<float> heightmap, int slice, float2 chunkSize, SamplerState linearClampSampler, int maxSteps, float epsilon, float3 offset)
{
    uint4 dimensions;
    heightmap.GetDimensions(0, dimensions.x, dimensions.y, dimensions.w, dimensions.z);

    hitT = 0.0;
    hitHeight = 0.0;

    float tEnterGlobal = 0, tExitDomain = 0;
    if (!GetBoundsExit(ro, rd, offset.xz, offset.xz + chunkSize, tEnterGlobal, tExitDomain))
        return false;

    float2 mip0Dimension = GetMipSize(dimensions.xy, 0);
    float2 mip0cellDimension = chunkSize / float2(mip0Dimension);
    float e = min(mip0cellDimension.x, mip0cellDimension.y) * 0.2f;
    
    int mip = max((int) dimensions.z - 2, 0);
    uint2 mipSize = GetMipSize(dimensions.xy, mip);
    float2 cellDimension = chunkSize / float2(mipSize);

    float tStartGlobal = max(tEnterGlobal, 0.0);
    float tRemaining = tExitDomain - tStartGlobal;

    float3 rayOriginInGrid = ro + rd * tStartGlobal;
    rayOriginInGrid = rayOriginInGrid + rd * epsilon;
    float tBase = tStartGlobal; // global ray-time of current local origin

    float2 deltaT = float2(1e20, 1e20);
    float t_x = 1e20;
    float t_y = 1e20;
    float t = 0.0;
    float tEnter = 0.0;
    float tExit = 0.0;

    InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);
       
    for (int i = 0; i < maxSteps && t < tRemaining; i++)
    {
        float3 p = rayOriginInGrid + rd * t;

        float2 uv = (p.xz - offset.xz) / chunkSize;
        int2 cell = clamp((int2) floor(uv * float2(mipSize)), int2(0, 0), int2(mipSize) - 1);
        float cellHeight = LoadMipHeightChunk(cell, mip, heightmap, slice);

        if (t_x < t_y)
        {
            tExit = t_x + e;
            t_x += deltaT.x;
        }
        else
        {
            tExit = t_y + e;
            t_y += deltaT.y;
        }

        tExit = min(tExit, tRemaining);

        float y0 = rayOriginInGrid.y + rd.y * tEnter;
        float y1 = rayOriginInGrid.y + rd.y * tExit;
        float segMinY = min(y0, y1);

        if (mip > 0)
        {
            if (segMinY > cellHeight + distanceForHit)
            {
                tEnter = tExit;
                t = tEnter;
            }
            else
            {
                float consumed = t;

                mip--;
                mipSize = GetMipSize(dimensions.xy, mip);
                cellDimension = chunkSize / float2(mipSize);

                rayOriginInGrid = p;
                tBase += consumed;
                tRemaining -= consumed;

                t = 0.0;
                tEnter = 0.0;

                InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);
            }
        }
        else
        {
            if (segMinY <= cellHeight + distanceForHit)
            {
                hitT = t;
                hitHeight = cellHeight;
                return true;
            }
            
            // Missed in this leaf segment, so advance past it
            float consumed = tExit;

            mip++;
            mipSize = GetMipSize(dimensions.xy, mip);
            cellDimension = chunkSize / float2(mipSize);

            float3 pExit = rayOriginInGrid + rd * consumed;
            rayOriginInGrid = pExit;
            tBase += consumed;
            tRemaining -= consumed;

            t = 0.0;
            tEnter = 0.0;
            tExit = 0.0;

            InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);
        }
    }


    return false;
}


bool RaymarchChunk(float3 rOrigin, float3 rDirection, out float hitT, out float terrainHeightAtHit, float maxSteps, float distanceForHit, float maxStepPrecision, float2 chunkSize, float heightScale, float3 offset, Texture2DArray<float> heightmap, int slice, SamplerState linearClampSampler
)
{
    hitT = 0.0;
    terrainHeightAtHit = 0.0;
    
    float tEnter = 0, tExitDomain = 0;

    if (!GetBoundsExit(rOrigin,
    rDirection,
    offset.xz,
    offset.xz + chunkSize,
    tEnter,
    tExitDomain))
        return false;

    bool belowTerrain = rOrigin.y < SampleTerrainHeightChunk(rOrigin.xz, chunkSize, heightScale, heightmap, slice, linearClampSampler);
    
    hitT = max(tEnter, 0.0);
    float maxT = tExitDomain;
    float3 p0 = rOrigin + rDirection * hitT;
    float terrainY0 = SampleTerrainHeightChunk(p0.xz, chunkSize, heightScale, heightmap, slice, linearClampSampler);
    float prevH = p0.y - terrainY0;

    // Start below terrain: do not render/hit underside.
    //if (prevH < 0.0)
    //    return false;

    for (int i = 0; i < maxSteps && hitT < maxT; i++)
    {
        float3 p = rOrigin + rDirection * hitT;

        float terrainY = SampleTerrainHeightChunk(p.xz, chunkSize, heightScale, heightmap, slice, linearClampSampler);
        float h = p.y - terrainY;

        // Only accept crossing from above to the terrain surface.
        if (h <= distanceForHit)
        {
            terrainHeightAtHit = terrainY;
            return true;
        }

        float stepSize = max(h * 0.2, 0.01);
        stepSize = min(stepSize, maxT * maxStepPrecision);

        hitT += stepSize;
    }

    return false;
}

float SampleTerrainHeight(float2 xz, float3 offset, float2 chunkSize, float heightScale, Texture2D<float> heightmap, SamplerState linearClampSampler)
{
    float2 uv = (xz - offset.xz) / chunkSize;
    return heightmap.SampleLevel(linearClampSampler, uv, 0) * heightScale + offset.y;
}


bool Raymarch(float3 rOrigin, float3 rDirection, out float hitT, out float terrainHeightAtHit, float maxSteps, float distanceForHit, float maxStepPrecision, float2 chunkSize, float heightScale, float3 offset, Texture2D<float> heightmap, SamplerState linearClampSampler)
{
    hitT = 0.0;
    terrainHeightAtHit = 0.0;
    
    float tEnter = 0, tExitDomain = 0;

    if (!GetBoundsExit(rOrigin,
    rDirection,
    offset.xz,
    offset.xz + chunkSize,
    tEnter,
    tExitDomain))
        return false;

    bool belowTerrain = rOrigin.y < SampleTerrainHeight(rOrigin.xz, offset, chunkSize, heightScale, heightmap, linearClampSampler);
    
    hitT = max(tEnter, 0.0);
    float maxT = tExitDomain;
    float3 p0 = rOrigin + rDirection * hitT;
    float terrainY0 = SampleTerrainHeight(p0.xz, offset, chunkSize, heightScale, heightmap, linearClampSampler);
    float prevH = p0.y - terrainY0;

    // Start below terrain: do not render/hit underside.
    if (prevH < 0.0)
        return false;

    for (int i = 0; i < maxSteps && hitT < maxT; i++)
    {
        float3 p = rOrigin + rDirection * hitT;

        float terrainY = SampleTerrainHeight(p.xz, offset, chunkSize, heightScale, heightmap, linearClampSampler);
        float h = p.y - terrainY;

        // Only accept crossing from above to the terrain surface.
        if (h <= distanceForHit)
        {
            terrainHeightAtHit = terrainY;
            return true;
        }

        float stepSize = max(h * 0.2, 0.01);
        stepSize = min(stepSize, maxT * maxStepPrecision);

        hitT += stepSize;
    }

    return false;
}


bool TraverseHeightfieldMaxMip(
    float3 ro,
    float3 rd,
    out float hitT,
    out float hitHeight,
    float distanceForHit, Texture2D<float> heightmap, float2 chunkSize, float heightScale, SamplerState linearClampSampler, int maxSteps, float3 offset)
{
    uint3 dimensions;
    heightmap.GetDimensions(0, dimensions.x, dimensions.y, dimensions.z);

    hitT = 0.0;
    hitHeight = 0.0;
    
    float tEnterGlobal = 0, tExitDomain = 0;
    if (!GetBoundsExit(ro, rd, offset.xz, offset.xz + chunkSize, tEnterGlobal, tExitDomain))
        return false;

    hitT = max(tEnterGlobal, 0.0);
    float maxT = tExitDomain;

    float3 p0 = ro + rd * hitT;
    float terrainY0 = SampleTerrainHeight(p0.xz, offset, chunkSize, heightScale, heightmap, linearClampSampler);
    float prevH = p0.y - terrainY0;

    // Start below terrain: do not render/hit underside.
    if (prevH < 0.0)
        return false;

    float2 mip0Dimension = GetMipSize(dimensions.xy, 0);
    float mip0cellDimension = chunkSize / float2(mip0Dimension);
    float e = mip0cellDimension * 0.09f;

    int mip = max((int) dimensions.z - 8, 0);
    uint2 mipSize = GetMipSize(dimensions.xy, mip);
    float2 cellDimension = chunkSize / float2(mipSize);

    float tStartGlobal = max(tEnterGlobal, 0.0);
    float tRemaining = tExitDomain - tStartGlobal;

    float3 rayOriginInGrid = ro + rd * tStartGlobal;
    float tBase = tStartGlobal; // global ray-time of current local origin

    float2 deltaT;
    float t_x, t_y;
    float t = 0.0;
    float tEnter = 0.0;
    float tExit = 0.0;

    InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);

    for (int i = 0; i < maxSteps && t < tRemaining; i++)
    {
        float3 p = rayOriginInGrid + rd * t;

        float2 uv = (p.xz -offset.xz) / chunkSize;
        int2 cell = clamp((int2) floor(uv * float2(mipSize)), int2(0, 0), int2(mipSize) - 1);
        float cellHeight = LoadMipHeight(cell, mip, heightmap) * heightScale + offset.y;
        
        if (t_x < t_y)
        {
            tExit = t_x + e;
            t_x += deltaT.x;
        }
        else
        {
            tExit = t_y + e;
            t_y += deltaT.y;
        }

        tExit = min(tExit, tRemaining);

        float y0 = rayOriginInGrid.y + rd.y * tEnter;
        float y1 = rayOriginInGrid.y + rd.y * tExit;
        float segMinY = min(y0, y1);

        if (mip > 0)
        {
            if (segMinY > cellHeight + distanceForHit)
            {
                tEnter = tExit;
                t = tEnter;
            }
            else
            {
                float consumed = t;

                mip--;
                mipSize = GetMipSize(dimensions.xy, mip);
                cellDimension = chunkSize / float2(mipSize);

                rayOriginInGrid = p;
                tBase += consumed;
                tRemaining -= consumed;

                t = 0.0;
                tEnter = 0.0;

                InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);
            }
        }
        else
        {
            if (segMinY <= cellHeight + distanceForHit)
            {
                float ta = tEnter;
                float tb = tExit;

                float3 pa = rayOriginInGrid + rd * ta;
                float3 pb = rayOriginInGrid + rd * tb;

                float ha = SampleTerrainHeight(pa.xz, offset, chunkSize, heightScale, heightmap, linearClampSampler);
                float hb = SampleTerrainHeight(pb.xz, offset, chunkSize, heightScale, heightmap, linearClampSampler);

                float ga = pa.y - ha;
                float gb = pb.y - hb;

                // Already close enough at entry
                if (ga <= distanceForHit)
                {
                    hitT = tBase + ta;
                    hitHeight = ha;
                    return true;
                }

                bool bracketed = (ga > distanceForHit && gb <= distanceForHit);

                // If endpoints do not bracket, try a few probes inside the interval
                if (!bracketed)
                {
                    float prevT = ta;
                    float prevG = ga;

                    [loop]
                    for (int k = 1; k <= 4; k++)
                    {
                        float s = (float) k / 5.0;
                        float tp = lerp(ta, tb, s);
                        float3 pp = rayOriginInGrid + rd * tp;
                        float hp = SampleTerrainHeight(pp.xz, offset, chunkSize, heightScale, heightmap, linearClampSampler);
                        float gp = pp.y - hp;

                        if (prevG > distanceForHit && gp <= distanceForHit)
                        {
                            ta = prevT;
                            tb = tp;
                            ha = SampleTerrainHeight((rayOriginInGrid + rd * ta).xz, offset, chunkSize, heightScale, heightmap, linearClampSampler);
                            hb = hp;
                            ga = prevG;
                            gb = gp;
                            bracketed = true;
                            break;
                        }

                        prevT = tp;
                        prevG = gp;
                    }
                }

                if (bracketed)
                {
                    [loop]
                    for (int j = 0; j < 8; j++)
                    {
                        float tm = 0.5 * (ta + tb);
                        float3 pm = rayOriginInGrid + rd * tm;
                        float hm = SampleTerrainHeight(pm.xz, offset, chunkSize, heightScale, heightmap, linearClampSampler);
                        float gm = pm.y - hm;

                        if (gm <= distanceForHit)
                        {
                            tb = tm;
                            hb = hm;
                        }
                        else
                        {
                            ta = tm;
                            ha = hm;
                        }
                    }

                    hitT = tBase + tb;
                    hitHeight = hb;
                    return true;
                }
            }

            // No actual hit found in this leaf interval, so advance past it
            float consumed = tExit;

            int topMip = max((int) dimensions.z - 8, 0);
            mip = min(mip + 1, topMip);
            mipSize = GetMipSize(dimensions.xy, mip);
            cellDimension = chunkSize / float2(mipSize);

            float3 pExit = rayOriginInGrid + rd * consumed;
            rayOriginInGrid = pExit;
            tBase += consumed;
            tRemaining -= consumed;

            t = 0.0;
            tEnter = 0.0;
            tExit = 0.0;

            InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);
        }
    }

    return false;
}

bool TraverseHeightfieldMaxMipShadow(
    float3 ro,
    float3 rd,
    out float hitT,
    out float hitHeight,
    float distanceForHit,
    inout float softness, Texture2D<float> heightmap, float2 chunkSize, float heightScale, SamplerState linearClampSampler, int maxSteps, float epsilon, float3 offset)
{
    uint3 dimensions;
    heightmap.GetDimensions(0, dimensions.x, dimensions.y, dimensions.z);

    hitT = 0.0;
    hitHeight = 0.0;

    float tEnterGlobal = 0, tExitDomain = 0;
    if (!GetBoundsExit(ro, rd, offset.xz, offset.xz + chunkSize, tEnterGlobal, tExitDomain))
        return false;

    float2 mip0Dimension = GetMipSize(dimensions.xy, 0);
    float mip0cellDimension = chunkSize / float2(mip0Dimension);
    float e = mip0cellDimension * 0.2f;

    int mip = max((int) dimensions.z - 2, 0);
    uint2 mipSize = GetMipSize(dimensions.xy, mip);
    float2 cellDimension = chunkSize / float2(mipSize);

    float tStartGlobal = max(tEnterGlobal, 0.0);
    float tRemaining = tExitDomain - tStartGlobal;

    float3 rayOriginInGrid = ro + rd * tStartGlobal;
    rayOriginInGrid = rayOriginInGrid + rd * epsilon;
    float tBase = tStartGlobal; // global ray-time of current local origin

    float2 deltaT;
    float t_x, t_y;
    float t = 0.0;
    float tEnter = 0.0;
    float tExit = 0.0;

    InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);
       
    for (int i = 0; i < maxSteps && t < tRemaining; i++)
    {
        float3 p = rayOriginInGrid + rd * t;

        float2 uv = (p.xz - offset.xz) / chunkSize;
        int2 cell = clamp((int2) floor(uv * float2(mipSize)), int2(0, 0), int2(mipSize) - 1);
        float cellHeight = LoadMipHeight(cell, mip, heightmap) * heightScale + offset.y;

        if (t_x < t_y)
        {
            tExit = t_x + e;
            t_x += deltaT.x;
        }
        else
        {
            tExit = t_y + e;
            t_y += deltaT.y;
        }

        tExit = min(tExit, tRemaining);

        float y0 = rayOriginInGrid.y + rd.y * tEnter;
        float y1 = rayOriginInGrid.y + rd.y * tExit;
        float segMinY = min(y0, y1);

        if (mip > 0)
        {
            if (segMinY > cellHeight + distanceForHit)
            {
                tEnter = tExit;
                t = tEnter;
            }
            else
            {
                float consumed = t;

                mip--;
                mipSize = GetMipSize(dimensions.xy, mip);
                cellDimension = chunkSize / float2(mipSize);

                rayOriginInGrid = p;
                tBase += consumed;
                tRemaining -= consumed;

                t = 0.0;
                tEnter = 0.0;

                InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);
            }
        }
        else
        {
            if (segMinY <= cellHeight + distanceForHit)
            {
                hitT = t;
                hitHeight = cellHeight;
                return true;
            }
            
            // Missed in this leaf segment, so advance past it
            float consumed = tExit;

            mip++;
            mipSize = GetMipSize(dimensions.xy, mip);
            cellDimension = chunkSize / float2(mipSize);

            float3 pExit = rayOriginInGrid + rd * consumed;
            rayOriginInGrid = pExit;
            tBase += consumed;
            tRemaining -= consumed;

            t = 0.0;
            tEnter = 0.0;
            tExit = 0.0;

            InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz, offset.xz);
        }
    }


    return false;
}


#endif 