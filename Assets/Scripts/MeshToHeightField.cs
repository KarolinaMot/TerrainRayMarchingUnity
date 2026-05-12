using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Unity.Burst.Intrinsics.X86.Avx;

public struct TerrainChunk
{
    public Transform transform;
    public Bounds bounds;
    public RenderTexture heightTexture;
    public int arrayIndex;
    public float minHeight;
    public float maxHeight;

    public TerrainChunk(Transform t, Bounds b, RenderTexture h, int arrayIndex, float minHeight, float maxHeight)
    {
        this.transform = t;
        this.bounds = b;
        this.heightTexture = h;
        this.arrayIndex = arrayIndex;
        this.minHeight = minHeight;
        this.maxHeight = maxHeight;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct ChunkDataGPU
{
    public Matrix4x4 worldToLocal;
    public Matrix4x4 localToWorld;
    public Vector3 boundsMin;
    public float minHeight;
    public Vector3 boundsMax;
    public float maxHeight;
    public Vector3 offset;
    public float padding3;
    public Vector3 scale;
    public int heightSlice;
    public Vector2 chunkSize;
    public Vector2 padding4;
}


public class MeshToHeightField : MonoBehaviour
{
    [Header("Target")]
    public GameObject terrainParent;

    [Header("Ouput")]
    public string outputPath;

    [Header("Bake Settings")]
    public int resolution = 2048;
    public LayerMask bakeLayer = ~0;

    Shader heightBakeShader;
    public Camera BakeCamera => _bakeCamera;

    public List<TerrainChunk> chunks = new List<TerrainChunk>();

    private Camera _bakeCamera;
    private Material _heightBakeMaterial;
    private ComputeShader mipCS;

    public RenderTexture rtArray;
    public ChunkDataGPU[] chunkData;
    public ComputeBuffer chunkBuffer;

    private void Start()
    {
        heightBakeShader = Resources.Load<Shader>("Mesh Shaders/TestBakeRed");
        mipCS = Resources.Load<ComputeShader>("Compute Shaders/MipMapGen");

        if (terrainParent == null)
        {
            Debug.LogError("MeshHeightmapBakerGPU: No terrain assigned.");
            return;
        }

        if (heightBakeShader == null)
        {
            Debug.LogError("MeshHeightmapBakerGPU: No heightBakeShader assigned.");
            return;
        }

        //Set up camera
        {
            GameObject camGO = new GameObject("HeightmapBakeCamera");
            camGO.hideFlags = HideFlags.HideAndDontSave;
            camGO.transform.SetParent(transform, false);

            _bakeCamera = camGO.AddComponent<Camera>();
            _bakeCamera.enabled = false;
            _bakeCamera.orthographic = true;
            _bakeCamera.allowHDR = false;
            _bakeCamera.allowMSAA = false;
            _bakeCamera.forceIntoRenderTexture = true;
        }

        if (_heightBakeMaterial == null)
            _heightBakeMaterial = new Material(heightBakeShader);

        MeshFilter[] meshFilters = terrainParent.GetComponentsInChildren<MeshFilter>();

        RenderTextureDescriptor desc = new RenderTextureDescriptor(resolution, resolution);
        desc.dimension = TextureDimension.Tex2D;
        desc.volumeDepth = 1;
        desc.msaaSamples = 1;
        desc.depthBufferBits = 24;
        desc.graphicsFormat = GraphicsFormat.R32_SFloat;
        desc.sRGB = false;
        desc.useMipMap = true;
        desc.autoGenerateMips = false;
        desc.enableRandomWrite = true;

        CommandBuffer cmd = new CommandBuffer();
        cmd.name = "GeneratingHeightmaps";
        int rtId = Shader.PropertyToID("TempHeightmap");
        cmd.GetTemporaryRT(rtId, desc);
        RenderTargetIdentifier tempRt = new RenderTargetIdentifier(rtId, 0, CubemapFace.Unknown, -1);

        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.sharedMesh != null)
            {
                RenderTexture heightTexture = new RenderTexture(desc);
                heightTexture.name = "MeshHeightmap_GPU_"+ mf.name;
                heightTexture.wrapMode = TextureWrapMode.Clamp;
                heightTexture.filterMode = FilterMode.Bilinear;
                heightTexture.Create();

                TerrainChunk chunk = new TerrainChunk(mf.transform, mf.sharedMesh.bounds, heightTexture, chunks.Count, 0,0);

                BakeHeightmap(cmd, chunk.heightTexture, rtId, mf.sharedMesh);
                chunks.Add(chunk);
            }
        }

        desc.dimension = TextureDimension.Tex2DArray;
        desc.volumeDepth = chunks.Count;
        rtArray = new RenderTexture(desc);
        rtArray.name = "MeshHeightmap_GPU_" + terrainParent.name;
        rtArray.wrapMode = TextureWrapMode.Clamp;
        rtArray.filterMode = FilterMode.Bilinear;
        rtArray.Create();

        chunkData = new ChunkDataGPU[chunks.Count];

        foreach (TerrainChunk chunk in chunks)
        {
            for(int i=0; i<chunk.heightTexture.mipmapCount; i++)
            {
                cmd.CopyTexture(
                  chunk.heightTexture,
                  0,
                  i,
                  rtArray,
                  chunk.arrayIndex,
                  i);

            }
            chunk.heightTexture.Release();
        }

        //Create texture
        cmd.ReleaseTemporaryRT(rtId);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        for (int i = 0; i < chunks.Count; i++)
        {
            TerrainChunk chunk = chunks[i];
            SaveRenderTextureAsRAW(chunk.heightTexture, outputPath + chunk.heightTexture.name + ".raw", out float minHeight,
                 out float maxHeight);

            chunk.minHeight = minHeight;
            chunk.maxHeight = maxHeight;
            chunks[i] = chunk;
        }

        int stride = Marshal.SizeOf<ChunkDataGPU>();
        chunkBuffer = new ComputeBuffer(chunkData.Length, stride);
        BuildChunkBuffer();
    }

    private void OnDestroy()
    {
        chunkBuffer?.Release();
        chunkBuffer = null;

        if (rtArray != null)
        {
            rtArray.Release();
            Destroy(rtArray);
            rtArray = null;
        }

        foreach (var chunk in chunks)
        {
            if (chunk.heightTexture != null)
            {
                chunk.heightTexture.Release();
                Destroy(chunk.heightTexture);
            }
        }

        if (_heightBakeMaterial != null)
        {
            Destroy(_heightBakeMaterial);
            _heightBakeMaterial = null;
        }

        if (_bakeCamera != null)
        {
            Destroy(_bakeCamera.gameObject);
            _bakeCamera = null;
        }
    }

    private void Update()
    {
        BuildChunkBuffer();
    }

    private void BuildChunkBuffer()
    {
        for (int i = 0; i < chunks.Count; i++)
        {
            Bounds b = chunks[i].bounds;

            chunkData[i] = new ChunkDataGPU
            {
                boundsMin = b.min,
                boundsMax = b.max,
                worldToLocal = chunks[i].transform.worldToLocalMatrix,
                localToWorld = chunks[i].transform.localToWorldMatrix,
                heightSlice = i,
                minHeight = chunks[i].minHeight,
                maxHeight = chunks[i].maxHeight,
                chunkSize = new Vector2(b.size.x, b.size.z),
                offset = chunks[i].transform.position,
                scale = chunks[i].transform.lossyScale
            };  
        }


        chunkBuffer.SetData(chunkData);
    }

    public void SaveRenderTextureAsRAW(RenderTexture rt, string path, out float min, out float max)
    {
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RFloat, false, true);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();

        RenderTexture.active = previous;

        float[] data = tex.GetRawTextureData<float>().ToArray();

        // Find actual min/max height in the texture
        max = float.NegativeInfinity;
        min = float.PositiveInfinity;

        for (int i = 0; i < data.Length; i++)
        {
            float h = data[i];
            if (h < min)
                min = h;
            if (h > max)
                max = h;
        }

        Debug.Log($"Height range: min = {min}, max = {max}");

        ushort[] raw = new ushort[data.Length];

        // Avoid divide-by-zero if the texture is flat
        if (Mathf.Approximately(min, max))
        {
            for (int i = 0; i < raw.Length; i++)
                raw[i] = 0;
        }
        else
        {
            for (int i = 0; i < data.Length; i++)
            {
                float normalized = Mathf.InverseLerp(min, max, data[i]);
                raw[i] = (ushort)Mathf.RoundToInt(normalized * 65535.0f);
            }
        }

        byte[] bytes = new byte[raw.Length * 2];

        for (int i = 0; i < raw.Length; i++)
        {
            bytes[i * 2] = (byte)(raw[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)((raw[i] >> 8) & 0xFF);
        }

        System.IO.File.WriteAllBytes(path, bytes);

        // Create PNG texture (8-bit grayscale preview)
        Texture2D pngTex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false, true);

        UnityEngine.Color[] colors = new UnityEngine.Color[data.Length];

        if (!Mathf.Approximately(min, max))
        {
            for (int i = 0; i < data.Length; i++)
            {
                float normalized = Mathf.InverseLerp(min, max, data[i]);
                colors[i] = new UnityEngine.Color(normalized, normalized, normalized);
            }
        }
        else
        {
            for (int i = 0; i < data.Length; i++)
            {
                colors[i] = UnityEngine.Color.black;
            }
        }

        pngTex.SetPixels(colors);
        pngTex.Apply();

        // Save PNG
        string pngPath = path.Replace(".raw", ".png");
        byte[] pngBytes = pngTex.EncodeToPNG();
        System.IO.File.WriteAllBytes(pngPath, pngBytes);

        UnityEngine.Object.DestroyImmediate(pngTex);
        UnityEngine.Object.DestroyImmediate(tex);
    }

    [ContextMenu("Bake heightmap")]
    void BakeHeightmap(CommandBuffer cmd, RenderTexture heightMap, int tempRtId, Mesh terrain)
    {
        //Configure camera
        {
            Vector3 center = terrain.bounds.center;

            _bakeCamera.transform.position = new Vector3(center.x, terrain.bounds.max.y + 10f, center.z);
            _bakeCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            float halfSize = Mathf.Max(terrain.bounds.extents.x, terrain.bounds.extents.z);
            _bakeCamera.orthographicSize = halfSize;

            float heightRange = terrain.bounds.size.y + 20f;
            _bakeCamera.nearClipPlane = 0.01f;
            _bakeCamera.farClipPlane = 100000f;
        }

        var previousRT = _bakeCamera.targetTexture;
        int oldMask = _bakeCamera.cullingMask;
        Color oldBg = _bakeCamera.backgroundColor;
        CameraClearFlags oldFlags = _bakeCamera.clearFlags;

        _bakeCamera.targetTexture = heightMap;
        _bakeCamera.cullingMask = bakeLayer.value;
        _bakeCamera.clearFlags = CameraClearFlags.SolidColor;
        _bakeCamera.backgroundColor = new Color(terrain.bounds.min.y, 0, 0, 0);
        _bakeCamera.orthographic = true;

        _bakeCamera.cullingMask = ~0;
        _bakeCamera.clearFlags = CameraClearFlags.SolidColor;
        _bakeCamera.backgroundColor = Color.black;

        cmd.BeginSample("Heightmap Bake");
        cmd.SetRenderTarget(heightMap);
        cmd.ClearRenderTarget(true, true, Color.black);

        cmd.SetViewProjectionMatrices(
        _bakeCamera.worldToCameraMatrix,
        _bakeCamera.projectionMatrix);
        cmd.DrawMesh(
            terrain,
            Matrix4x4.identity,
            _heightBakeMaterial,
            0
        );
        cmd.EndSample("Heightmap Bake");

        _bakeCamera.targetTexture = previousRT;
        _bakeCamera.cullingMask = oldMask;
        _bakeCamera.backgroundColor = oldBg;
        _bakeCamera.clearFlags = oldFlags;

        int srcWidth = heightMap.width;
        int srcHeight = heightMap.height;
        int mipCount = heightMap.mipmapCount;
        int mipKernel = mipCS.FindKernel("ReduceMaxMip");

        for (int srcMip = 0; srcMip < mipCount - 1; srcMip++)
        {
            int dstWidth = Mathf.Max(1, srcWidth / 2);
            int dstHeight = Mathf.Max(1, srcHeight / 2);

            cmd.SetComputeIntParam(mipCS, "_SrcMip", srcMip);
            cmd.SetComputeIntParam(mipCS, "_SrcMipSizeX", srcWidth);
            cmd.SetComputeIntParam(mipCS, "_SrcMipSizeY", srcHeight);

            cmd.SetComputeTextureParam(mipCS, mipKernel, "_SrcTex", heightMap);
            cmd.SetComputeTextureParam(mipCS, mipKernel, "_DstTex", tempRtId, srcMip + 1);

            int groupsX = Mathf.CeilToInt(dstWidth / 8.0f);
            int groupsY = Mathf.CeilToInt(dstHeight / 8.0f);

            cmd.DispatchCompute(mipCS, mipKernel, groupsX, groupsY, 1);

            cmd.CopyTexture(tempRtId, 0, srcMip + 1, heightMap, 0, srcMip + 1);

            srcWidth = dstWidth;
            srcHeight = dstHeight;
        }
    }
}
