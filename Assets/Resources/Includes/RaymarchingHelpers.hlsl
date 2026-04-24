#ifndef RMARCH_HELPER
#define RMARCH_HELPER

float SampleTerrainHeight(float2 xz, float chunkSize, Texture2D<float> heightmap, SamplerState linearClampSampler)
{
    float2 uv = xz / chunkSize;
    return heightmap.SampleLevel(linearClampSampler, uv, 0);
}

float LoadMipHeight(int2 texel, int mip, Texture2D<float> heightmap)
{
    return heightmap.Load(int3(texel, mip));
}

bool GetBoundsExit(float3 ro, float3 rd, float2 minPos, float2 maxPos, out float tEnter, out float tExit)
{
    tEnter = -1e38;
    tExit = 1e38;

    const float EPSILON = 1e-8;

    float2 invDir = 1.0 / rd.xz;
    float2 t0 = (minPos - ro.xz) * invDir;
    float2 t1 = (maxPos - ro.xz) * invDir;

    float2 tNear = min(t0, t1);
    float2 tFar = max(t0, t1);

    tEnter = max(tNear.x, tNear.y);
    tExit = min(tFar.x, tFar.y);

    if (tExit < max(tEnter, 0.0))
        return false;

    return true;
}


bool Raymarch(float3 rOrigin, float3 rDirection, out float hitT, out float terrainHeightAtHit, float maxSteps, float distanceForHit, float maxStepPrecision, float chunkSize, Texture2D<float> heightmap, SamplerState linearClampSampler)
{
    hitT = 0.0;
    terrainHeightAtHit = 0.0;
    
    float tEnter, tExitDomain;
    if (!GetBoundsExit(rOrigin, rDirection, float2(0.0, 0.0), chunkSize.xx, tEnter, tExitDomain))
        return false;

    bool belowTerrain = rOrigin.y < SampleTerrainHeight(rOrigin.xz, chunkSize, heightmap, linearClampSampler);
    
    hitT = max(tEnter, 0.0);
    float maxT = tExitDomain;

    float3 p0 = rOrigin + rDirection * hitT;
    float terrainY0 = SampleTerrainHeight(p0.xz, chunkSize, heightmap, linearClampSampler);
    float prevH = p0.y - terrainY0;

    // Start below terrain: do not render/hit underside.
    if (prevH < 0.0)
        return false;

    for (int i = 0; i < maxSteps && hitT < maxT; i++)
    {
        float3 p = rOrigin + rDirection * hitT;

        float terrainY = SampleTerrainHeight(p.xz, chunkSize, heightmap, linearClampSampler);
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




void GetTexelWorldPos(float2 mipSizeInPixels, uint2 texel, out float2 worldTexelPos1, out float2 worldTexelPos2, float chunkSize)
{
    float worldTexelSize = chunkSize / mipSizeInPixels;
    worldTexelPos1 = float2(texel) * worldTexelSize;
    worldTexelPos2 = float2(texel + 1) * worldTexelSize;
}

uint2 GetMipSize(int2 baseSize, int mip)
{
    return max(uint2(1u, 1u), baseSize >> mip);
}

void InitializeDDA(
    out float2 deltaT,
    out float t_y,
    out float t_x,
    float2 cellDimension,
    float2 rayOriginXZ,
    float2 rayDirectionXZ)
{
    if (abs(rayDirectionXZ.x) < 1e-8)
    {
        deltaT.x = 1e30;
        t_x = 1e30;
    }
    else if (rayDirectionXZ.x < 0)
    {
        deltaT.x = cellDimension.x / abs(rayDirectionXZ.x);
        t_x = (floor(rayOriginXZ.x / cellDimension.x) * cellDimension.x - rayOriginXZ.x) / rayDirectionXZ.x;
    }
    else
    {
        deltaT.x = cellDimension.x / abs(rayDirectionXZ.x);
        t_x = ((floor(rayOriginXZ.x / cellDimension.x) + 1.0) * cellDimension.x - rayOriginXZ.x) / rayDirectionXZ.x;
    }

    if (abs(rayDirectionXZ.y) < 1e-8)
    {
        deltaT.y = 1e30;
        t_y = 1e30;
    }
    else if (rayDirectionXZ.y < 0)
    {
        deltaT.y = cellDimension.y / abs(rayDirectionXZ.y);
        t_y = (floor(rayOriginXZ.y / cellDimension.y) * cellDimension.y - rayOriginXZ.y) / rayDirectionXZ.y;
    }
    else
    {
        deltaT.y = cellDimension.y / abs(rayDirectionXZ.y);
        t_y = ((floor(rayOriginXZ.y / cellDimension.y) + 1.0) * cellDimension.y - rayOriginXZ.y) / rayDirectionXZ.y;
    }
}

bool TraverseHeightfieldMaxMip(
    float3 ro,
    float3 rd,
    out float hitT,
    out float hitHeight,
    float distanceForHit, Texture2D<float> heightmap, float chunkSize, SamplerState linearClampSampler, int maxSteps)
{
    uint3 dimensions;
    heightmap.GetDimensions(0, dimensions.x, dimensions.y, dimensions.z);

    hitT = 0.0;
    hitHeight = 0.0;

    float tEnterGlobal, tExitDomain;
    if (!GetBoundsExit(ro, rd, float2(0.0, 0.0), float2(chunkSize, chunkSize), tEnterGlobal, tExitDomain))
        return false;

    hitT = max(tEnterGlobal, 0.0);
    float maxT = tExitDomain;

    float3 p0 = ro + rd * hitT;
    float terrainY0 = SampleTerrainHeight(p0.xz, chunkSize, heightmap, linearClampSampler);
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

    InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz);

    for (int i = 0; i < maxSteps && t < tRemaining; i++)
    {
        float3 p = rayOriginInGrid + rd * t;

        float2 uv = p.xz / chunkSize;
        int2 cell = clamp((int2) floor(uv * float2(mipSize)), int2(0, 0), int2(mipSize) - 1);
        float cellHeight = LoadMipHeight(cell, mip, heightmap);

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

                InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz);
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

                float ha = SampleTerrainHeight(pa.xz, chunkSize, heightmap, linearClampSampler);
                float hb = SampleTerrainHeight(pb.xz, chunkSize, heightmap, linearClampSampler);

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
                        float hp = SampleTerrainHeight(pp.xz, chunkSize, heightmap, linearClampSampler);
                        float gp = pp.y - hp;

                        if (prevG > distanceForHit && gp <= distanceForHit)
                        {
                            ta = prevT;
                            tb = tp;
                            ha = SampleTerrainHeight((rayOriginInGrid + rd * ta).xz, chunkSize, heightmap, linearClampSampler);
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
                        float hm = SampleTerrainHeight(pm.xz, chunkSize, heightmap, linearClampSampler);
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

            InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz);
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
    inout float softness, Texture2D<float> heightmap, float chunkSize, SamplerState linearClampSampler, int maxSteps, float epsilon)
{
    uint3 dimensions;
    heightmap.GetDimensions(0, dimensions.x, dimensions.y, dimensions.z);

    hitT = 0.0;
    hitHeight = 0.0;

    float tEnterGlobal, tExitDomain;
    if (!GetBoundsExit(ro, rd, float2(0.0, 0.0), float2(chunkSize, chunkSize), tEnterGlobal, tExitDomain))
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

    InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz);
       
    for (int i = 0; i < epsilon && t < tRemaining; i++)
    {
        float3 p = rayOriginInGrid + rd * t;

        float2 uv = p.xz / chunkSize;
        int2 cell = clamp((int2) floor(uv * float2(mipSize)), int2(0, 0), int2(mipSize) - 1);
        float cellHeight = LoadMipHeight(cell, mip, heightmap);

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

                InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz);
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

            InitializeDDA(deltaT, t_y, t_x, cellDimension, rayOriginInGrid.xz, rd.xz);
        }
    }


    return false;
}


#endif 