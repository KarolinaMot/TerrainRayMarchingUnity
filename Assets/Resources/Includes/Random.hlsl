#ifndef RANDOM_INCLUDED
#define RANDOM_INCLUDED

#include "Common.hlsl"

namespace Random
{
    uint PcgHash(uint x)
    {
        uint state = x * 747796405u + 2891336453u;
        uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
        return (word >> 22u) ^ word;
    }

    float UniformFloat(inout uint state)
    {
        state = PcgHash(state);
        return ((float) state) / (float) (0xFFFFFFFF);
    }
    
    float2 UniformFloat2(inout uint state)
    {
        return float2(
            UniformFloat(state),
            UniformFloat(state)
        );
    }
    
    uint XorShiftU32(uint state)
    {
        uint s = state ^ (state << 13);
        s ^= s >> 17;
        s ^= s << 5;
        return s;
    }
}

#endif // RANDOM_INCLUDED
