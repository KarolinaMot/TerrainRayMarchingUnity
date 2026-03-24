using System;
using System.Drawing;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

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
    }
}
