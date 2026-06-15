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
    private ComputeShader marchCS;
    private Camera camera;
    private Light sun;
    private TileBVH chunkBVH;
    private TileHeightmapGenerator tileHeightmapGenerator;
    public readonly TileDataGPU[] chunkDataCPU;
    private ComputeBuffer chunkDataBuffer;

    private const int MAX_TILES_IN_ARRAY = 500;

    private RenderTexture heightMapArray;
    private RenderTexture shadowArray;

    private ComputeShader ditherShadomapCS;
    private ComputeShader fullShadowmapCS;

    [Header("Ray-marching initialization")]
    public int heightmapDimensions = 526;
    public int shadowmapDimensions = 526;
    public GameObject parentTerrain;

    [Header("Ray-marching")]
    public bool enableBVH = true;
    public bool visualizeTerrain = false;
    public int maxSteps = 100;
    public int maxStepsOptimized = 100;
    public float distanceForHit = 0.001f;
    [Space(10)]

    [Header("Terrain shading")]
    public bool optimizeTracing = true;
    public Transform terrainTransform;
    CommandBuffer cmd;

    private void Start()
    {
        Camera cam = Camera.main;
        cam.depthTextureMode |= DepthTextureMode.Depth;
        tileHeightmapGenerator = new TileHeightmapGenerator();
        chunkBVH = new TileBVH(MAX_TILES_IN_ARRAY);
        camera = GetComponent<Camera>();

        sun = RenderSettings.sun;

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
        shadowArray.name = "MeshHeightmap_GPU_terrain";
        shadowArray.wrapMode = TextureWrapMode.Clamp;
        shadowArray.filterMode = FilterMode.Bilinear;
        shadowArray.Create();

        ditherShadomapCS = Resources.Load<ComputeShader>("Compute Shaders/DitherShadowBakeCS");

        cmd = new CommandBuffer()
        {
            name = "My Cmd Buffer2"
        };
        camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, cmd);
    }

    void GenerateHeightmaps()
    {
        MeshFilter[] meshFilters = parentTerrain.GetComponentsInChildren<MeshFilter>();
        int index = 0;
        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.sharedMesh != null)
            {
                TileDataGPU tileData = new TileDataGPU();
                tileData = BuildChunkData(mf.gameObject, index);
                chunkDataCPU[index] = tileData;

                //HEIGHTMAP
                {
                    //Set chunk index value in material
                    MeshRenderer meshRenderer = tileGO.GetComponentInChildren<MeshRenderer>();
                    meshRenderer.GetPropertyBlock(mpb);
                    mpb.SetInt("_ChunkIndex", index);
                    meshRenderer.SetPropertyBlock(mpb);
                    mpb.Clear();

                    //Generate heightmap
                    tileHeightmapGenerator.GetTileDataAndHeightmap(
                            commandBuffer,
                            tileGO,
                            heightmaps.width,
                            heightmaps.mipmapCount, heightmaps, index);
                }

                //Clear shadowmap slice
                commandBuffer.SetRenderTarget(shadowmaps, 0, CubemapFace.Unknown, index);
                commandBuffer.ClearRenderTarget(false, true, Color.white);


                BakeHeightmap(cmd, chunk.heightTexture, rtId, mf.sharedMesh);
                chunks.Add(chunk);
                index++;

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
            isHighRes = chunkGO.gameObject.layer != LayerMask.NameToLayer("Low Resolution") ? 1u : 0u
        };
    }

    private void OnDestroy()
    {
        if (cmd != null)
        {
            camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, cmd);
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
    }

    private void Update()
    {
        cmd.Clear();
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
        cmd.ReleaseTemporaryRT(sourceRT);

    }
    private void TerrainParameters(int kernel, CommandBuffer cmd)
    {
        
        cmd.SetComputeIntParam(marchCS, "_MaxSteps", maxSteps);
        cmd.SetComputeFloatParam(marchCS, "_DistanceForHit", distanceForHit);

        Vector3 sunDir = sun.transform.forward;
        Vector3 sunColor = new Vector3(sun.color.r, sun.color.g, sun.color.b);
        Vector4 sunDirIntensity = new Vector4(sunDir.x, sunDir.y, sunDir.z, sun.intensity);

        cmd.SetComputeFloatParam(marchCS, "_MaxStepsOptimized", maxStepsOptimized);
        cmd.SetComputeVectorParam(marchCS, "_SunDirectionIntensity", sunDirIntensity);
        cmd.SetComputeVectorParam(marchCS, "_SunColor", sunColor);
        cmd.SetComputeBufferParam(marchCS, kernel, "_Chunks", meshToHeightfield.chunkBuffer);
        cmd.SetComputeBufferParam(marchCS, kernel, "_Nodes", chunkBVH.bvhBuffer);
        cmd.SetComputeIntParam(marchCS, "_ChunkCount", meshToHeightfield.chunkData.Length);
        cmd.SetComputeVectorParam(marchCS, "_AverageChunkSize", meshToHeightfield.averageChunkSize);
        cmd.SetComputeVectorParam(marchCS, "_ChunkGridSize", meshToHeightfield.chunkGridSize);
        cmd.SetComputeIntParam(marchCS, "_NodeCount", chunkBVH.nodes.Count);
        cmd.SetComputeTextureParam(marchCS, kernel, "_HeightMapArray", meshToHeightfield.rtArray);

        cmd.SetComputeIntParam(marchCS, "_UseRaymarchOptimization", optimizeTracing ? 1 : 0);
        cmd.SetComputeIntParam(marchCS, "_VisualizeTerrain", visualizeTerrain ? 1 : 0);
        cmd.SetComputeIntParam(marchCS, "_EnableBVH", enableBVH ? 1 : 0);
        cmd.SetComputeMatrixParam(marchCS, "_WorldToLightClip", heightfieldShadowMap.worldToLightClip);
        cmd.SetComputeTextureParam(marchCS, kernel, "_ShadowMap", heightfieldShadowMap.rtShadowArray[Time.frameCount % 2]);
        cmd.SetComputeTextureParam(
            marchCS,
            kernel,
            "_CameraDepthTexture",
            BuiltinRenderTextureType.Depth
        );
    }
}
