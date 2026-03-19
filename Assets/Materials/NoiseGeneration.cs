using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class NoiseGeneration : MonoBehaviour
{
    public int verticesPerChunk = 129;
    public float chunkSize = 128;
    private ComputeShader noiseCS;
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

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        camera = GetComponent<Camera>();

        noiseCS = Resources.Load<ComputeShader>("Compute Shaders/NoiseGeneration");
        if (noiseCS == null)
        {
            Debug.LogError("Failed to load compute shader: Compute Shaders/NoiseGeneration");
            return;
        }

        heightmap = new RenderTexture(verticesPerChunk, verticesPerChunk, 0);
        heightmap.graphicsFormat = GraphicsFormat.R32_SFloat;
        heightmap.enableRandomWrite = true;
        heightmap.wrapMode = TextureWrapMode.Clamp;
        heightmap.filterMode = FilterMode.Bilinear;
        heightmap.Create();

        RunCompute();
    }

    void Update()
    {

    }

    // Update is called once per frame
    [ContextMenu("Generate Noise")]
    void RunCompute()
    {
        int kernel = noiseCS.FindKernel("Main");

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
    }
}
