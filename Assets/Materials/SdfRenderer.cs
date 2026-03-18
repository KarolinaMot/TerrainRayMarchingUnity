using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

public class SdfRenderer : MonoBehaviour
{
    private ComputeShader marchCS;
    private Camera camera;

    [Header("Ray-marching")]
    public int maxSteps = 100;
    public int maxDistance = 100;
    public float distanceForHit = 0.001f;
    [Space(10)]


    [Header("Terrain generation")]
    public int octaves = 1;
    public float persistence = 0.5f;
    public float lacunarity = 4.0f;
    public float noiseCellScaling = 1;
    public float heightMultiplier = 1;
    public float lows = 1;
    [Space(10)]

    [Header("Biomes")]
    public Color grassColor;
    public Color waterColor;
    public Color snowColor;
    public float seaLevel;
    public float snowLevel;
    [Space(10)]

    [Header("Terrain shading")]
    public float roughness = 1;
    public float metallic = 0;
    public Vector3 sunDirection;
    public float sunIntensity = 1;


    private void Start()
    {
        marchCS = Resources.Load<ComputeShader>("Compute Shaders/TerrainRayMarch");

        camera = GetComponent<Camera>();
    }
    private void Update()
    {
        CommandBuffer cmd = new CommandBuffer()
        {
            name = "My Cmd Buffer"
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

        int rt = Shader.PropertyToID("_TmpRT ");
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

        cmd.SetComputeFloatParam(marchCS, "_CellScaling", noiseCellScaling);
        cmd.SetComputeIntParam(marchCS, "_Octaves", octaves);
        cmd.SetComputeFloatParam(marchCS, "_HeightMultiplier", heightMultiplier);
        cmd.SetComputeFloatParam(marchCS, "_Persistence", persistence);
        cmd.SetComputeFloatParam(marchCS, "_Lacunarity", lacunarity);
        cmd.SetComputeFloatParam(marchCS, "_HeightMultiplier", heightMultiplier);
        cmd.SetComputeFloatParam(marchCS, "_Lows", lows);

        Vector4 color = new Vector4(grassColor.r, grassColor.g, grassColor.b, 0.8f);
        Vector4 waterColorRoughness = new Vector4(waterColor.r, waterColor.g, waterColor.b, 0.25f);
        Vector4 snowColorRoughness = new Vector4(snowColor.r, snowColor.g, snowColor.b, 0.5f);
        Vector4 sunDirIntensity = new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, sunIntensity);

        cmd.SetComputeVectorParam(marchCS, "_GrassColorRoughness", color);
        cmd.SetComputeVectorParam(marchCS, "_WaterColorRoughness", waterColorRoughness);
        cmd.SetComputeVectorParam(marchCS, "_SnowColorRoughness", snowColorRoughness);
        cmd.SetComputeFloatParam(marchCS, "_SeaLevel", seaLevel);
        cmd.SetComputeFloatParam(marchCS, "_SnowLevel", snowLevel);
        cmd.SetComputeFloatParam(marchCS, "_Metallic", metallic);
        cmd.SetComputeVectorParam(marchCS, "_SunDirectionIntensity", sunDirIntensity);
    }
}
