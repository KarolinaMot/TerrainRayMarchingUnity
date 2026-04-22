using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;
using static Unity.VisualScripting.Member;

public class SdfRenderer : MonoBehaviour
{
    private ComputeShader marchCS;
    private Camera sceneCamera;
    private Light sun;


    [Header("Ray-marching source")]
    public string heightMapFilePath;
    private Texture2D heightTexture;
    public int heightfieldDimensions;
    public float heightMultiplier;
    public float worldSize;

    [Header("Ray-marching")]
    public bool raymarch = true;
    public bool visualizeMesh = true;
    public int maxSteps = 100;
    public int maxStepsOptimized = 100;
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
    public float sunAngularRadius = 3;
    public float shadowSteps = 1024;
    public float shadowStepsOptimized = 225;
    private int shadowSamples = 1;
    public float shdowEpsilon = 35f;
    public float shdowEpsilonOptimized = 40f;
    public float shadowHitDistance = -5f;
    public bool useBlueNoise = true;
    public bool pathTracedShadows = true;
    public bool optimizeTracing = true;

    private RenderTexture[] temporalShadow = new RenderTexture[2];
    private Vector3 prevCameraPos = Vector3.zero;
    private Quaternion prevCameraRot = Quaternion.identity;
    float prevShadowSoftness;
    Vector3 prevSunDirection;
    int prevShadowSamples;
    bool prevUseBlueNoise;
    bool prevUsePathtraced;
    public void LoadRaw()
    {
        byte[] bytes = System.IO.File.ReadAllBytes(heightMapFilePath);


        int expectedBytes = heightfieldDimensions * heightfieldDimensions * 2; // 16-bit
        if (bytes.Length != expectedBytes)
        {
            Debug.LogError($"RAW size mismatch. Expected {expectedBytes} bytes, got {bytes.Length}");
            return;
        }

        heightTexture = new Texture2D(heightfieldDimensions, heightfieldDimensions, TextureFormat.RFloat, false, true);

        Color[] pixels = new Color[heightfieldDimensions * heightfieldDimensions];

        for (int i = 0; i < heightfieldDimensions * heightfieldDimensions; i++)
        {
            int byteIndex = i * 2;

            // little-endian ushort
            ushort value = (ushort)(bytes[byteIndex] | (bytes[byteIndex + 1] << 8));

            float normalized = value / 65535.0f;
            pixels[i] = new Color(normalized, 0, 0, 1);
        }

        heightTexture.SetPixels(pixels);
        heightTexture.Apply();

        Debug.Log("RAW heightmap loaded.");
    }
    private void Start()
    {
        marchCS = Resources.Load<ComputeShader>("Compute Shaders/TerrainRayMarch");
        LoadRaw();

        sceneCamera = GetComponent<Camera>();
        sun = RenderSettings.sun;

        for (int i = 0; i < 2; i++)
        {
            temporalShadow[i] = new RenderTexture(sceneCamera.pixelWidth, sceneCamera.pixelHeight, 0);
            temporalShadow[i].graphicsFormat = GraphicsFormat.R32G32_SFloat;
            temporalShadow[i].enableRandomWrite = true;
            temporalShadow[i].wrapMode = TextureWrapMode.Clamp;
            temporalShadow[i].filterMode = FilterMode.Bilinear;
            temporalShadow[i].Create();
        }
    }
    private void Update()
    {
        CommandBuffer cmd = new CommandBuffer()
        {
            name = "My Cmd Buffer2"
        };

        int kernel = marchCS.FindKernel("Main");


        RenderTextureDescriptor desc = new RenderTextureDescriptor();
        desc.width = sceneCamera.pixelWidth;
        desc.height = sceneCamera.pixelHeight;
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

        Matrix4x4 cameraToWorld = sceneCamera.cameraToWorldMatrix;
        Matrix4x4 inverseProjection = GL.GetGPUProjectionMatrix(sceneCamera.projectionMatrix, true).inverse;
        Vector3 cameraWorldPos = sceneCamera.transform.position;

        cmd.SetComputeMatrixParam(marchCS, "_CameraToWorld", cameraToWorld);
        cmd.SetComputeMatrixParam(marchCS, "_CameraInverseProjection", inverseProjection);
        cmd.SetComputeVectorParam(marchCS, "_WorldSpaceCameraPos", cameraWorldPos);

        TerrainParameters(kernel, cmd);

        cmd.DispatchCompute(marchCS, kernel,
                Mathf.CeilToInt(sceneCamera.pixelWidth / 16.0f),
                Mathf.CeilToInt(sceneCamera.pixelHeight / 16.0f),
                1);

        RenderTargetIdentifier dstId = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget, 0, CubemapFace.Unknown, -1);
        cmd.Blit(rtId, dstId);

        cmd.ReleaseTemporaryRT(rt);

        sceneCamera.RemoveCommandBuffers(CameraEvent.BeforeImageEffects);
        sceneCamera.AddCommandBuffer(CameraEvent.BeforeImageEffects, cmd);
    }

    private void ClearTemporal(CommandBuffer cmd)
    {
        for (int i = 0; i < 2; i++)
        {
            cmd.SetRenderTarget(temporalShadow[i]);
            cmd.ClearRenderTarget(false, true, Color.clear); // RGFloat → (0,0)
        }
    }

    bool CameraMoved()
    {
        bool moved = sceneCamera.transform.position != prevCameraPos ||
                     sceneCamera.transform.rotation != prevCameraRot;

        prevCameraPos = sceneCamera.transform.position;
        prevCameraRot = sceneCamera.transform.rotation;

        return moved;
    }

    bool SettingsChanged()
    {
        bool changed =
            prevShadowSoftness != sunAngularRadius ||
            prevShadowSamples != shadowSamples ||
            prevUseBlueNoise != useBlueNoise ||
            prevSunDirection != sun.transform.forward ||
            prevUsePathtraced != pathTracedShadows;

        prevShadowSoftness = sunAngularRadius;
        prevShadowSamples = shadowSamples;
        prevUseBlueNoise = useBlueNoise;
        prevUsePathtraced = pathTracedShadows;
        prevSunDirection = sun.transform.forward;

        return changed;
    }

    private void TerrainParameters(int kernel, CommandBuffer cmd)
    {
        if(CameraMoved() || SettingsChanged())
        {
            ClearTemporal(cmd);
        }

        cmd.SetComputeIntParam(marchCS, "_MaxSteps", maxSteps);
        cmd.SetComputeFloatParam(marchCS, "_DistanceForHit", distanceForHit);
        cmd.SetComputeTextureParam(marchCS, kernel, "_MeshHeightMap", heightTexture);

        Vector4 color = new Vector4(grassColor.r, grassColor.g, grassColor.b, 0.8f);
        Vector4 waterColorRoughness = new Vector4(waterColor.r, waterColor.g, waterColor.b, 0.1f);
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
        cmd.SetComputeFloatParam(marchCS, "_MaxStepsOptimized", maxStepsOptimized);
        cmd.SetComputeFloatParam(marchCS, "_GrassLevel", grassLevel);
        cmd.SetComputeFloatParam(marchCS, "_RockLevel", rockLevel);
        cmd.SetComputeFloatParam(marchCS, "_ForestLevel", forestLevel);
        cmd.SetComputeFloatParam(marchCS, "_SnowLevel", snowLevel);
        cmd.SetComputeFloatParam(marchCS, "_OceanDepth", oceanDepth);
        cmd.SetComputeVectorParam(marchCS, "_SunDirectionIntensity", sunDirIntensity);
        cmd.SetComputeVectorParam(marchCS, "_SunColor", sunColor);
        cmd.SetComputeFloatParam(marchCS, "_SunAngularRadius", sunAngularRadius);
        cmd.SetComputeFloatParam(marchCS, "_ShdowEpsilon", shdowEpsilon);
        cmd.SetComputeFloatParam(marchCS, "_ShdowEpsilonOptimized", shdowEpsilonOptimized);
        cmd.SetComputeFloatParam(marchCS, "_ChunkSize", worldSize);
        cmd.SetComputeFloatParam(marchCS, "_ShadowHitDistance", shadowHitDistance);
        cmd.SetComputeFloatParam(marchCS, "_ShadowSteps", shadowSteps);
        cmd.SetComputeFloatParam(marchCS, "_HeightMultiplier", heightMultiplier);
        cmd.SetComputeFloatParam(marchCS, "_ShadowStepsOptimized", shadowStepsOptimized);
        cmd.SetComputeIntParam(marchCS, "_ShadowSamples", (int)shadowSamples);
        cmd.SetComputeIntParam(marchCS, "_UseBlueNoise", useBlueNoise ? 1 : 0);
        cmd.SetComputeIntParam(marchCS, "_UsePathtracedShadows", pathTracedShadows ? 1 : 0);
        cmd.SetComputeIntParam(marchCS, "_UseRaymarchOptimization", optimizeTracing ? 1 : 0);
        cmd.SetComputeIntParam(marchCS, "_Render", raymarch ? 1 : 0);
        cmd.SetComputeIntParam(marchCS, "_RenderMesh", visualizeMesh ? 1 : 0);
        cmd.SetComputeTextureParam(marchCS, kernel, "_TemporalShadow", temporalShadow[Time.frameCount % 2]);
        cmd.SetComputeTextureParam(marchCS, kernel, "_TemporalShadowPrev", temporalShadow[(Time.frameCount + 1) % 2]);
    }
}
