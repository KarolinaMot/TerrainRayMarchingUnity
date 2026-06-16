using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;
using static Unity.VisualScripting.Member;

[StructLayout(LayoutKind.Sequential)]
public struct TileDataGPU
{
    public Matrix4x4 worldToLocal;
    public Matrix4x4 localToWorld;
    public Vector3 boundsMin;
    public uint isHighRes;
    public Vector3 boundsMax;
    public int heightSlice;
}

public class SdfRenderer : MonoBehaviour
{
    private Camera mainCamera;
    private Light sun;
    private TileBVH chunkBVH;
    private TileHeightmapGenerator tileHeightmapGenerator;
    private TileDataGPU[] chunkDataCPU;
    private ComputeBuffer chunkDataBuffer;
    private int fullShadowKernelMain;
    private int ditherKernelMain;
    private int activeTileCount;
    private const int MAX_TILES_IN_ARRAY = 500;

    private RenderTexture heightMapArray;
    private RenderTexture shadowArray;

    private ComputeShader ditherShadomapCS;
    private ComputeShader fullShadowmapCS;
    private ComputeShader terrainRaymarchCS;
    private CommandBuffer cmd;

    [Header("Ray-marching initialization")]
    public int heightmapDimensions = 526;
    public int shadowmapDimensions = 526;
    public GameObject parentTerrain;

    [Header("Ray-marching")]
    public bool visualizeTerrain = false;
    public bool useMaxMipOptimization = true;
    public int maxSteps = 5000;
    public float distanceForHit = 0.2f;
    public float shadowEpsilon = 0.5f;
    public int shadowUpdateFrequency = 8;
    public float shadowSoftness = 0.2f;
    public float shadowDarkness = 0f;
    public float shadowBlendAlpha = 0.2f;


    private void Start()
    {
        mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("Camera.main is null. Make sure your camera has the MainCamera tag.");
            return;
        }

        mainCamera.depthTextureMode |= DepthTextureMode.Depth;

        chunkDataCPU = new TileDataGPU[MAX_TILES_IN_ARRAY];
        tileHeightmapGenerator = new TileHeightmapGenerator();
        chunkBVH = new TileBVH(MAX_TILES_IN_ARRAY);
        int stride = Marshal.SizeOf<TileDataGPU>();
        chunkDataBuffer = new ComputeBuffer(MAX_TILES_IN_ARRAY, stride);

        sun = RenderSettings.sun;

        if (sun == null)
        {
            Debug.LogError("RenderSettings.sun is null. Assign a Directional Light as the scene sun.");
            return;
        }

        // create render textures...

        ditherShadomapCS = Resources.Load<ComputeShader>("Compute Shaders/DitherShadowBakeCS");
        fullShadowmapCS = Resources.Load<ComputeShader>("Compute Shaders/FullShadowBakeCS");
        terrainRaymarchCS = Resources.Load<ComputeShader>("Compute Shaders/TerrainRayMarch");

        if (ditherShadomapCS == null)
        {
            Debug.LogError("DitherShadowBakeCS not found in Resources/Compute Shaders/");
            return;
        }

        if (fullShadowmapCS == null)
        {
            Debug.LogError("FullShadowBakeCS not found in Resources/Compute Shaders/");
            return;
        }

        if (terrainRaymarchCS == null)
        {
            Debug.LogError("TerrainRaymarchCS not found in Resources/Compute Shaders/");
            return;
        }

        ditherKernelMain = ditherShadomapCS.FindKernel("Main");
        fullShadowKernelMain = fullShadowmapCS.FindKernel("Main");

        RenderTextureDescriptor desc = new RenderTextureDescriptor(heightmapDimensions, heightmapDimensions);
        desc.dimension = TextureDimension.Tex2DArray;
        desc.volumeDepth = MAX_TILES_IN_ARRAY;
        desc.msaaSamples = 1;
        desc.depthBufferBits = 0;
        desc.graphicsFormat = GraphicsFormat.R32_SFloat;
        desc.sRGB = false;
        desc.useMipMap = true;
        desc.autoGenerateMips = false;
        desc.enableRandomWrite = true;
        heightMapArray = new RenderTexture(desc);
        heightMapArray.name = "MeshHeightmap_GPU_terrain";
        heightMapArray.wrapMode = TextureWrapMode.Clamp;
        heightMapArray.filterMode = FilterMode.Bilinear;
        heightMapArray.Create();

        desc = new RenderTextureDescriptor(shadowmapDimensions, shadowmapDimensions);
        desc.dimension = TextureDimension.Tex2DArray;
        desc.volumeDepth = MAX_TILES_IN_ARRAY;
        desc.msaaSamples = 1;
        desc.depthBufferBits = 0;
        desc.graphicsFormat = GraphicsFormat.R32_SFloat;
        desc.sRGB = false;
        desc.useMipMap = false;
        desc.autoGenerateMips = false;
        desc.enableRandomWrite = true;
        shadowArray = new RenderTexture(desc);
        shadowArray.name = "MeshShadowmap_GPU_terrain";
        shadowArray.wrapMode = TextureWrapMode.Clamp;
        shadowArray.filterMode = FilterMode.Bilinear;
        shadowArray.Create();

        cmd = new CommandBuffer
        {
            name = "My Cmd Buffer2"
        };

        mainCamera.AddCommandBuffer(CameraEvent.BeforeImageEffects, cmd);
        GenerateHeightmaps(cmd);
        Graphics.ExecuteCommandBuffer(cmd);
    }

    void GenerateHeightmaps(CommandBuffer commandBuffer)
    {
        if (parentTerrain == null)
        {
            Debug.LogError("parentTerrain is null. Assign it in the Inspector.");
            return;
        }

        MeshFilter[] meshFilters = parentTerrain.GetComponentsInChildren<MeshFilter>();
        activeTileCount = 0;
        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.sharedMesh == null)
                continue;

            TileDataGPU tileData = new TileDataGPU();
            tileData = BuildChunkData(mf.gameObject, activeTileCount);
            chunkDataCPU[activeTileCount] = tileData;

            //HEIGHTMAP
            {
                var tileGO = mf.gameObject;

                //Generate heightmap
                tileHeightmapGenerator.GetHeightmap(
                        commandBuffer,
                        tileGO,
                        heightMapArray.width,
                        heightMapArray.mipmapCount, heightMapArray, activeTileCount);
            }

            activeTileCount++;
        }

        commandBuffer.SetBufferData(chunkDataBuffer, chunkDataCPU);
        chunkBVH.Build(commandBuffer, activeTileCount, chunkDataCPU);
        TerrainParameters();

        for (int i=0; i< activeTileCount; i++)
        {
            //Clear shadowmap slice
            commandBuffer.SetRenderTarget(shadowArray, 0, CubemapFace.Unknown, i);
            commandBuffer.ClearRenderTarget(false, true, Color.white);

            //HIGH RES FALLBACK SHADOWMAP
            {
                var tileData = chunkDataCPU[i];

                commandBuffer.BeginSample("Fallback shadow Bake");
                commandBuffer.SetComputeTextureParam(fullShadowmapCS, fullShadowKernelMain, "_Result", shadowArray);
                commandBuffer.SetComputeVectorParam(fullShadowmapCS, "_SunDirection", RenderSettings.sun.transform.forward);
                commandBuffer.SetComputeIntParam(fullShadowmapCS, "_ChunkIndex", tileData.heightSlice);

                int groupsX = Mathf.CeilToInt(shadowArray.width / 8f);
                int groupsY = Mathf.CeilToInt(shadowArray.height / 8f);
                int groupsZ = 1;

                commandBuffer.DispatchCompute(
                    fullShadowmapCS,
                    fullShadowKernelMain,
                    groupsX,
                    groupsY,
                    groupsZ
                );

                commandBuffer.EndSample("Fallback shadow Bake");
            }
        }

    }

    private TileDataGPU BuildChunkData(GameObject chunkGO, int index)
    {
        Transform tr = chunkGO.transform;

        MeshFilter meshFilter = chunkGO.GetComponentInChildren<MeshFilter>();
        Mesh mesh = meshFilter.sharedMesh;

        Bounds meshB = mesh.bounds;

        return new TileDataGPU
        {
            worldToLocal = tr.worldToLocalMatrix,
            localToWorld = tr.localToWorldMatrix,

            boundsMin = meshB.min,
            boundsMax = meshB.max,

            heightSlice = index,
            isHighRes = 0 //actually used in the internship code, but not in this demo
        };
    }

    private void OnDestroy()
    {
        if (cmd != null)
        {
            if (mainCamera != null)
                mainCamera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, cmd);

            cmd.Release();
            cmd = null;
        }

        if (chunkBVH != null)
        {
            chunkBVH.Destroy();
            chunkBVH = null;
        }

        if (heightMapArray != null)
        {
            heightMapArray.Release();
            Destroy(heightMapArray);
            heightMapArray = null;
        }

        if (shadowArray != null)
        {
            shadowArray.Release();
            Destroy(shadowArray);
            shadowArray = null;
        }

        if (chunkDataBuffer != null)
        {
            chunkDataBuffer.Release();
            chunkDataBuffer = null;
        }
    }

    private void Update()
    {
        cmd.Clear();

        TerrainParameters();

        UpdateShadows();

        VisualizeShadows();

    }

    private void UpdateShadows()
    {
        cmd.BeginSample("Shadow Bake");

        cmd.SetComputeTextureParam(ditherShadomapCS, ditherKernelMain, "_Result", shadowArray);
        cmd.SetComputeVectorParam(ditherShadomapCS, "_SunDirection", RenderSettings.sun.transform.forward);

        cmd.SetComputeIntParam(ditherShadomapCS, "_UpdateFrequency", shadowUpdateFrequency);

        int totalFrames = shadowUpdateFrequency * shadowUpdateFrequency;
        int groupsX = Mathf.CeilToInt(shadowArray.width / shadowUpdateFrequency / 8f);
        int groupsY = Mathf.CeilToInt(shadowArray.height / shadowUpdateFrequency / 8f);
        int groupsZ = activeTileCount;

        cmd.DispatchCompute(
            ditherShadomapCS,
            ditherKernelMain,
            groupsX,
            groupsY,
            groupsZ
        );

        cmd.EndSample("Shadow Bake");
    }

    private void VisualizeShadows()
    {
        cmd.BeginSample("Visualize shadows");

        int kernel = terrainRaymarchCS.FindKernel("Main");

        RenderTextureDescriptor desc = new RenderTextureDescriptor();
        desc.width = mainCamera.pixelWidth;
        desc.height = mainCamera.pixelHeight;
        desc.msaaSamples = 1;
        desc.volumeDepth = 1;
        desc.mipCount = 1;
        desc.dimension = TextureDimension.Tex2D;
        desc.enableRandomWrite = true;
        desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.B10G11R11_UFloatPack32;

        int rt = Shader.PropertyToID("_TmpRT");
        cmd.GetTemporaryRT(rt, desc);
        RenderTargetIdentifier rtId = new RenderTargetIdentifier(rt, 0, CubemapFace.Unknown, -1);

        int sourceRT = Shader.PropertyToID("_SourceRT");
        cmd.GetTemporaryRT(sourceRT, desc);
        RenderTargetIdentifier sourceId = new RenderTargetIdentifier(sourceRT);

        // Copy current camera target into source texture
        RenderTargetIdentifier cameraTarget = BuiltinRenderTextureType.CameraTarget;
        cmd.Blit(cameraTarget, sourceId);
        cmd.SetComputeTextureParam(terrainRaymarchCS, kernel, "_Source", sourceId);
        cmd.SetComputeTextureParam(terrainRaymarchCS, kernel, "_Result", rtId);

        cmd.SetComputeIntParam(terrainRaymarchCS, "_UseRaymarchOptimization", useMaxMipOptimization ? 1 : 0);
        cmd.SetComputeIntParam(terrainRaymarchCS, "_VisualizeTerrain", visualizeTerrain ? 1 : 0);
        cmd.SetComputeTextureParam(terrainRaymarchCS, kernel, "_CameraDepthTexture", BuiltinRenderTextureType.Depth);

        Matrix4x4 cameraToWorld = mainCamera.cameraToWorldMatrix;
        Matrix4x4 inverseProjection = GL.GetGPUProjectionMatrix(mainCamera.projectionMatrix, true).inverse;
        Vector3 cameraWorldPos = mainCamera.transform.position;

        cmd.SetComputeMatrixParam(terrainRaymarchCS, "_CameraToWorld", cameraToWorld);
        cmd.SetComputeMatrixParam(terrainRaymarchCS, "_CameraInverseProjection", inverseProjection);
        cmd.SetComputeVectorParam(terrainRaymarchCS, "_WorldSpaceCameraPos", cameraWorldPos);

        cmd.DispatchCompute(terrainRaymarchCS, kernel,
                Mathf.CeilToInt(mainCamera.pixelWidth / 8.0f),
                Mathf.CeilToInt(mainCamera.pixelHeight / 8.0f),
                1);

        RenderTargetIdentifier dstId = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget, 0, CubemapFace.Unknown, -1);
        cmd.Blit(rtId, dstId);

        cmd.ReleaseTemporaryRT(rt);
        cmd.ReleaseTemporaryRT(sourceRT);
        cmd.EndSample("Visualize shadows");

    }

    private void TerrainParameters()
    {
        cmd.SetGlobalBuffer("_Chunks", chunkDataBuffer);
        cmd.SetGlobalInt("_ChunkCount", activeTileCount);
        chunkBVH.BindGlobalData(cmd);
        cmd.SetGlobalTexture("_HeightMapArray", heightMapArray);
        cmd.SetGlobalTexture("_ShadowMapArray", shadowArray);
        cmd.SetGlobalInt("_MaxSteps", maxSteps);
        cmd.SetGlobalFloat("_DistanceForHit", distanceForHit);
        cmd.SetGlobalFloat("_ShadowEpsilon", shadowEpsilon);
        cmd.SetGlobalFloat("_SunAngularRadius", shadowSoftness);
        cmd.SetGlobalFloat("_ShadowBlendAlpha", shadowBlendAlpha);
        cmd.SetGlobalFloat("_ShadowDarkness", shadowDarkness);
        cmd.SetGlobalFloat("_ShadowmapResolution", shadowmapDimensions);
    }
}
