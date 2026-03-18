#ifndef PBR_INCLUDED
#define PBR_INCLUDED
static const float sPi = 3.14159265359;

struct PBRMaterial
{
    float3 baseColor;
    float3 emissiveColor;
    float metallic;
    float roughness;
    float3 normal;
    float occlusionColor;
    float3 F0;
    float3 diffuse;
};

float3 FSchlick(float3 F0, float3 F90, float vDotH)
{
    return F0 + (F90 - F0) * pow(clamp(1.0 - vDotH, 0.0, 1.0), 5.0);
}

float3 LambertianDiffuse(float3 albedo, float3 f0, float3 f90, float vDotH)
{
    return (1.0 - FSchlick(f0, f90, vDotH)) * (albedo / sPi);
}

float D_GGX(in float NdotH, in float alphaRoughness)
{
    float alphaRoughnessSq = alphaRoughness * alphaRoughness;
    float f = (NdotH * NdotH) * (alphaRoughnessSq - 1.0) + 1.0;

    return alphaRoughnessSq / (sPi * f * f);
}

float V_GGX(in float NdotL, in float NoV, in float alphaRoughness)
{
    float alphaRoughnessSq = alphaRoughness * alphaRoughness;

    float GGXV = NdotL * sqrt(NoV * NoV * (1.0 - alphaRoughnessSq) + alphaRoughnessSq);
    float GGXL = NoV * sqrt(NdotL * NdotL * (1.0 - alphaRoughnessSq) + alphaRoughnessSq);

    float GGX = GGXV + GGXL;
    if (GGX > 0.0)
    {
        return 0.5 / GGX;
    }

    return 0.0;
}

void GetBRDF(
    PBRMaterial mat,
    float3 viewDir,
    float3 lightDir,
    float3 lightColor,
    float lightIntensity,
    float attenuation,
    inout float3 diffuse,
    inout float3 specular)
{
    float3 halfAngle = normalize(viewDir + lightDir);
    float nDotL = clamp(dot(mat.normal, lightDir), 0.0, 1.0);
    float nDotH = clamp(dot(mat.normal, halfAngle), 0.0, 1.0);
    float nDotV = clamp(dot(mat.normal, viewDir), 0.0, 1.0);
    float vDotH = clamp(dot(viewDir, halfAngle), 0.0, 1.0);

    float3 colorIntensity = lightColor * lightIntensity;
    colorIntensity *= attenuation;

    float3 diffuseBRDF = LambertianDiffuse(mat.diffuse, mat.F0, float3(1.0, 1.0, 1.0), vDotH);

    float3 F = FSchlick(mat.F0, float3(1.0, 1.0, 1.0), vDotH*1.8);
    float3 G = V_GGX(nDotL, nDotV, mat.roughness);
    float3 D = D_GGX(nDotH, mat.roughness);
    float3 specularBRDF = F * G * D;

    diffuse += colorIntensity * nDotL * diffuseBRDF;
    specular += colorIntensity * nDotL * specularBRDF;
}

#endif // PBR_INCLUDED