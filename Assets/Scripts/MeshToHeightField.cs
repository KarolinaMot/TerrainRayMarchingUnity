using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Unity.Burst.Intrinsics.X86.Avx;

public struct TerrainChunk
{
    public Transform transform;
    public Bounds bounds;
    public int indexInArray;

    public TerrainChunk(Transform t, Bounds b, int index)
    {
        this.transform = t;
        this.bounds = b;
        this.indexInArray = index;
    }
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
   // public RenderTexture HeightTexture;
    public Camera BakeCamera => _bakeCamera;

    public float max, min;
    public List<TerrainChunk> chunks = new List<TerrainChunk>();
    public RenderTexture heightRTArray;

    private Camera _bakeCamera;
  //  private RenderTexture _tempHeightTexture;
    private Material _heightBakeMaterial;
    private ComputeShader mipCS;

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

        int slices = meshFilters.Count();

        if (slices == 0)
            return;

        heightRTArray = new RenderTexture(resolution, resolution, 0);
        heightRTArray.dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray;
        heightRTArray.volumeDepth = slices;
        heightRTArray.graphicsFormat = GraphicsFormat.R32_SFloat;
        heightRTArray.enableRandomWrite = true; // REQUIRED for compute shaders
        heightRTArray.useMipMap = false;

        heightRTArray.wrapMode = TextureWrapMode.Clamp;
        heightRTArray.filterMode = FilterMode.Bilinear;
        heightRTArray.name = "MeshHeightmap_GPU_" + terrainParent.name;
        heightRTArray.Create();

        int counter = 0;
        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.sharedMesh != null)
            {
                TerrainChunk chunk = new TerrainChunk(mf.transform, mf.sharedMesh.bounds, counter);

                BakeHeightmap(cmd, counter, tempRt, mf.sharedMesh);
                chunks.Add(chunk);
                counter++;
            }
        }

        //Create texture
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        foreach (TerrainChunk chunk in chunks)
        {
            SaveRenderTextureAsRAW(chunk.indexInArray, outputPath + heightRTArray.name + chunk.indexInArray + ".raw");
        }

    }

    private void Update()
    {

    }
    public void SaveRenderTextureAsRAW(int indexInArray, string path)
    {
        Texture2D tex = new Texture2D(heightRTArray.width, heightRTArray.height, TextureFormat.RFloat, false, true);

        RenderTexture previous = RenderTexture.active;
        Graphics.SetRenderTarget(
            heightRTArray,
            0,                      // mip
            CubemapFace.Unknown,
            indexInArray            // THIS is the slice
        );

        tex.ReadPixels(new Rect(0, 0, heightRTArray.width, heightRTArray.height), 0, 0);
        tex.Apply();


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
        Texture2D pngTex = new Texture2D(heightRTArray.width, heightRTArray.height, TextureFormat.RGB24, false, true);

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
    void BakeHeightmap(CommandBuffer cmd, int indexInArray, RenderTargetIdentifier tempRt, Mesh terrain)
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

        int oldMask = _bakeCamera.cullingMask;
        Color oldBg = _bakeCamera.backgroundColor;
        CameraClearFlags oldFlags = _bakeCamera.clearFlags;

        _bakeCamera.cullingMask = bakeLayer.value;
        _bakeCamera.clearFlags = CameraClearFlags.SolidColor;
        _bakeCamera.backgroundColor = new Color(terrain.bounds.min.y, 0, 0, 0);
        _bakeCamera.orthographic = true;

        _bakeCamera.cullingMask = ~0;
        _bakeCamera.clearFlags = CameraClearFlags.SolidColor;
        _bakeCamera.backgroundColor = Color.black;

        cmd.BeginSample("Heightmap Bake");

        RenderTargetIdentifier slice =
            new RenderTargetIdentifier(
                heightRTArray,
                0,                      // mip
                CubemapFace.Unknown,
                indexInArray                       // array slice
            );

        cmd.SetRenderTarget(slice);
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

        _bakeCamera.cullingMask = oldMask;
        _bakeCamera.backgroundColor = oldBg;
        _bakeCamera.clearFlags = oldFlags;

        int srcWidth = heightRTArray.width;
        int srcHeight = heightRTArray.height;
        int mipCount = heightRTArray.mipmapCount;
        int mipKernel = mipCS.FindKernel("ReduceMaxMip");

        for (int srcMip = 0; srcMip < mipCount - 1; srcMip++)
        {
            int dstWidth = Mathf.Max(1, srcWidth / 2);
            int dstHeight = Mathf.Max(1, srcHeight / 2);

            cmd.SetComputeIntParam(mipCS, "_SrcMip", srcMip);
            cmd.SetComputeIntParam(mipCS, "_SrcMipSizeX", srcWidth);
            cmd.SetComputeIntParam(mipCS, "_SrcMipSizeY", srcHeight);

            cmd.SetComputeTextureParam(mipCS, mipKernel, "_SrcTex", slice);
            cmd.SetComputeTextureParam(mipCS, mipKernel, "_DstTex", tempRt, srcMip + 1);

            int groupsX = Mathf.CeilToInt(dstWidth / 8.0f);
            int groupsY = Mathf.CeilToInt(dstHeight / 8.0f);

            cmd.DispatchCompute(mipCS, mipKernel, groupsX, groupsY, 1);

            cmd.CopyTexture(tempRt, 0, srcMip + 1, slice, 0, srcMip + 1);

            srcWidth = dstWidth;
            srcHeight = dstHeight;
        }

    }
}
