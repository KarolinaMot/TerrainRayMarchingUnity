using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using static Unity.VisualScripting.Member;

public class SdfRenderer : MonoBehaviour
{
    private ComputeShader marchCS;
    private Camera camera;
    private Light sun;
    private NoiseGeneration noiseGen;

    [Header("Ray-marching")]
    public int maxSteps = 100;
    public int maxDistance = 100;
    public float distanceForHit = 0.001f;
    [Space(10)]


    [Header("Biomes")]
    public Color grassColor;
    public Color waterColor;
    public Color snowColor;
    public Color sandColor;
    public Color forestColor;
    public Color rockColor;
    [Space(5)]
    public float grassLevel;
    public float forestLevel;
    public float rockLevel;
    public float snowLevel;
    public float oceanDepth;
    [Space(10)]

    [Header("Terrain shading")]
    public float shadowSoftness = 1;
    public float shadowSteps = 1024;


    private void Start()
    {
        marchCS = Resources.Load<ComputeShader>("Compute Shaders/TerrainRayMarch");

        camera = GetComponent<Camera>();
        noiseGen = GetComponent<NoiseGeneration>();
        sun = RenderSettings.sun;
    }
    private void Update()
    {
        if (!noiseGen)
        {
            Debug.LogError("Noise generator not found");
            return;
        }

        CommandBuffer cmd = new CommandBuffer()
        {
            name = "My Cmd Buffer2"
        };

        int kernel = marchCS.FindKernel("Main");


        RenderTextureDescriptor desc = new RenderTextureDescriptor();
        desc.width = camera.pixelWidth;
        desc.height = camera.pixelHeight;
        desc.msaaSamples = 1;
        desc.volumeDepth = 1;
        desc.mipCount = 1;
        desc.dimension = TextureDimension.Tex2D;
        desc.enableRandomWrite = true;
        desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.B10G11R11_UFloatPack32;

        int rt = Shader.PropertyToID("_TmpRT");
        cmd.GetTemporaryRT(rt, desc);
        RenderTargetIdentifier rtId = new RenderTargetIdentifier(rt, 0, CubemapFace.Unknown, -1);
        cmd.SetComputeTextureParam(marchCS, kernel, "_Result", rtId);

        int sourceRT = Shader.PropertyToID("_SourceRT");
        cmd.GetTemporaryRT(sourceRT, desc);
        RenderTargetIdentifier sourceId = new RenderTargetIdentifier(sourceRT);

        // Copy current camera target into source texture
        RenderTargetIdentifier cameraTarget = BuiltinRenderTextureType.CameraTarget;
        cmd.Blit(cameraTarget, sourceId);

        cmd.SetComputeTextureParam(marchCS, kernel, "_Source", sourceId);


        Matrix4x4 cameraToWorld = camera.cameraToWorldMatrix;
        Matrix4x4 inverseProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true).inverse;
        Vector3 cameraWorldPos = camera.transform.position;

        cmd.SetComputeMatrixParam(marchCS, "_CameraToWorld", cameraToWorld);
        cmd.SetComputeMatrixParam(marchCS, "_CameraInverseProjection", inverseProjection);
        cmd.SetComputeVectorParam(marchCS, "_WorldSpaceCameraPos", cameraWorldPos);


        TerrainParameters(kernel, cmd);


        cmd.DispatchCompute(marchCS, kernel,
                Mathf.CeilToInt(camera.pixelWidth / 16.0f),
                Mathf.CeilToInt(camera.pixelHeight / 16.0f),
                1);

        RenderTargetIdentifier dstId = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget, 0, CubemapFace.Unknown, -1);
        cmd.Blit(rtId, dstId);

        cmd.ReleaseTemporaryRT(rt);

        camera.RemoveCommandBuffers(CameraEvent.BeforeImageEffects);
        camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, cmd);
    }

    private void TerrainParameters(int kernel, CommandBuffer cmd)
    {
        cmd.SetComputeIntParam(marchCS, "_MaxSteps", maxSteps);
        cmd.SetComputeIntParam(marchCS, "_MaxDistance", maxDistance);
        cmd.SetComputeFloatParam(marchCS, "_DistanceForHit", distanceForHit);
        cmd.SetComputeTextureParam(marchCS, kernel, "_HeightMap", noiseGen.heightmap);

        Vector4 color = new Vector4(grassColor.r, grassColor.g, grassColor.b, 0.8f);
        Vector4 waterColorRoughness = new Vector4(waterColor.r, waterColor.g, waterColor.b, 0.25f);
        Vector4 snowColorRoughness = new Vector4(snowColor.r, snowColor.g, snowColor.b, 0.5f);
        Vector4 sandColorRoughness = new Vector4(sandColor.r, sandColor.g, sandColor.b, 0.8f);
        Vector4 forestColorRoughness = new Vector4(forestColor.r, forestColor.g, forestColor.b, 0.8f);
        Vector4 rockColorRoughness = new Vector4(rockColor.r, rockColor.g, rockColor.b, 0.8f);
        Vector3 sunDir = sun.transform.forward;
        Vector3 sunColor = new Vector3(sun.color.r, sun.color.g, sun.color.b);
        Vector4 sunDirIntensity = new Vector4(sunDir.x, sunDir.y, sunDir.z, sun.intensity);

        cmd.SetComputeVectorParam(marchCS, "_GrassColorRoughness", color);
        cmd.SetComputeVectorParam(marchCS, "_WaterColorRoughness", waterColorRoughness);
        cmd.SetComputeVectorParam(marchCS, "_SnowColorRoughness", snowColorRoughness);
        cmd.SetComputeVectorParam(marchCS, "_SandColorRoughness", sandColorRoughness);
        cmd.SetComputeVectorParam(marchCS, "_ForestColorRoughness", forestColorRoughness);
        cmd.SetComputeVectorParam(marchCS, "_RockColorRoughness", rockColorRoughness);
        cmd.SetComputeFloatParam(marchCS, "_GrassLevel", grassLevel);
        cmd.SetComputeFloatParam(marchCS, "_RockLevel", rockLevel);
        cmd.SetComputeFloatParam(marchCS, "_ForestLevel", forestLevel);
        cmd.SetComputeFloatParam(marchCS, "_SnowLevel", snowLevel);
        cmd.SetComputeFloatParam(marchCS, "_OceanDepth", oceanDepth);
        cmd.SetComputeVectorParam(marchCS, "_SunDirectionIntensity", sunDirIntensity);
        cmd.SetComputeVectorParam(marchCS, "_SunColor", sunColor);
        cmd.SetComputeFloatParam(marchCS, "_ShadowSoftness", shadowSoftness);
        cmd.SetComputeFloatParam(marchCS, "_ChunkSize", noiseGen.chunkSize);
        cmd.SetComputeVectorParam(marchCS, "_ChunkCoord", new Vector2(0f,0f));
        cmd.SetComputeFloatParam(marchCS, "_ShadowSteps", shadowSteps);
    }
}
