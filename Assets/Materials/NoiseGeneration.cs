using System;
using System.Drawing;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Unity.Burst.Intrinsics.X86.Avx;

public class NoiseGeneration : MonoBehaviour
{
    public int verticesPerChunk = 128;
    public float chunkSize = 128;
    private ComputeShader noiseCS;
    private ComputeShader mipCS;
    private Camera camera;

    [Header("Terrain generation")]
    public int octaves = 2;
    public float persistence = 0.01f;
    public float lacunarity = 7.43f;
    public float noiseCellScaling = 0.04f;
    public float heightMultiplier = 7.13f;
    public float lows = 1.78f;
    public float mountainRangeHeight = 3.19f;
    public float mountainRangeContrast = 7.73f;
    public float mountainRangeDensity = 0.005f;

    [HideInInspector]
    public RenderTexture heightmap;
    public RenderTexture tempHightmap;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        camera = GetComponent<Camera>();

        noiseCS = Resources.Load<ComputeShader>("Compute Shaders/NoiseGeneration");
        mipCS = Resources.Load<ComputeShader>("Compute Shaders/MipMapGen");
        
        if (noiseCS == null)
        {
            Debug.LogError("Failed to load compute shader: Compute Shaders/NoiseGeneration");
            return;
        }

        heightmap = CreateHeightTexture(verticesPerChunk);
        tempHightmap = CreateHeightTexture(verticesPerChunk);

        RunCompute();
    }

    void Update()
    {
        RunCompute();
    }

    RenderTexture CreateHeightTexture(int size)
    {
        var desc = new RenderTextureDescriptor(size, size, GraphicsFormat.R32_SFloat, 0);
        desc.enableRandomWrite = true;
        desc.useMipMap = true;
        desc.autoGenerateMips = false;
        desc.msaaSamples = 1;
        desc.dimension = TextureDimension.Tex2D;
        desc.volumeDepth = 1;
        desc.depthBufferBits = 0;

        var rt = new RenderTexture(desc);
        rt.filterMode = FilterMode.Trilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.Create();

        return rt;
    }
    public static void SaveRenderTextureAsRAW(RenderTexture rt, string path)
    {
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RFloat, false, true);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();

        RenderTexture.active = previous;

        float[] data = tex.GetRawTextureData<float>().ToArray();

        // Find actual min/max height in the texture
        float minH = float.PositiveInfinity;
        float maxH = float.NegativeInfinity;

        for (int i = 0; i < data.Length; i++)
        {
            float h = data[i];
            if (h < minH) minH = h;
            if (h > maxH) maxH = h;
        }

        Debug.Log($"Height range: min = {minH}, max = {maxH}");

        ushort[] raw = new ushort[data.Length];

        // Avoid divide-by-zero if the texture is flat
        if (Mathf.Approximately(minH, maxH))
        {
            for (int i = 0; i < raw.Length; i++)
                raw[i] = 0;
        }
        else
        {
            for (int i = 0; i < data.Length; i++)
            {
                float normalized = Mathf.InverseLerp(minH, maxH, data[i]);
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

        if (!Mathf.Approximately(minH, maxH))
        {
            for (int i = 0; i < data.Length; i++)
            {
                float normalized = Mathf.InverseLerp(minH, maxH, data[i]);
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

        UnityEngine.Object.Destroy(pngTex);
        UnityEngine.Object.Destroy(tex);
    }

    // Update is called once per frame
    [ContextMenu("Generate Noise")]
    void RunCompute()
    {
        int kernel = noiseCS.FindKernel("Main");
        int mipKernel = mipCS.FindKernel("ReduceMaxMip");

        noiseCS.SetTexture(kernel, "_HeightMap", heightmap);

        noiseCS.SetInt("_ChunkResolution", verticesPerChunk);
        noiseCS.SetFloat("_ChunkSize", chunkSize);
        noiseCS.SetVector("_ChunkCoord", new Vector4(0f, 0f, 0f, 0f));

        noiseCS.SetFloat("_CellScaling", noiseCellScaling);
        noiseCS.SetInt("_Octaves", octaves);
        noiseCS.SetFloat("_HeightMultiplier", heightMultiplier);
        noiseCS.SetFloat("_Persistence", persistence);
        noiseCS.SetFloat("_Lacunarity", lacunarity);
        noiseCS.SetFloat("_Lows", lows);
        noiseCS.SetFloat("_MountainRangeHeight", mountainRangeHeight);
        noiseCS.SetFloat("_MountainRangeContrast", mountainRangeContrast);
        noiseCS.SetFloat("_MountainRangeDensity", mountainRangeDensity);

        int groups = Mathf.CeilToInt(verticesPerChunk / 16.0f);
        noiseCS.Dispatch(kernel, groups, groups, 1);

        int srcWidth = heightmap.width;
        int srcHeight = heightmap.height;
        int mipCount = heightmap.mipmapCount;

        for (int srcMip = 0; srcMip < mipCount - 1; srcMip++)
        {
            int dstWidth = Mathf.Max(1, srcWidth / 2);
            int dstHeight = Mathf.Max(1, srcHeight / 2);

            mipCS.SetInt("_SrcMip", srcMip);
            mipCS.SetInt("_SrcMipSizeX", srcWidth);
            mipCS.SetInt("_SrcMipSizeY", srcHeight);

            mipCS.SetTexture(mipKernel, "_SrcTex", heightmap);
            mipCS.SetTexture(mipKernel, "_DstTex", tempHightmap, srcMip + 1);

            int groupsX = Mathf.CeilToInt(dstWidth / 8.0f);
            int groupsY = Mathf.CeilToInt(dstHeight / 8.0f);

            mipCS.Dispatch(mipKernel, groupsX, groupsY, 1);

            Graphics.CopyTexture(tempHightmap, 0, srcMip + 1, heightmap, 0, srcMip + 1);

            srcWidth = dstWidth;
            srcHeight = dstHeight;
        }

      //  SaveRenderTextureAsRAW(heightmap, "Assets/Resources/Heightmaps/heightmap.raw");
    }
}
