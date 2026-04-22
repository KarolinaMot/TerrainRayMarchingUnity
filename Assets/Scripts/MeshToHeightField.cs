using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class MeshToHeightField : MonoBehaviour
{
    [Header("Target")]
    public Renderer targetRenderer;

    [Header("Ouput")]
    public string outputPath;


    [Header("Bake Settings")]
    public int resolution = 2048;
    public LayerMask bakeLayer = ~0;

    Shader heightBakeShader;
    public RenderTexture HeightTexture => _heightTexture;
    public Camera BakeCamera => _bakeCamera;
    public Bounds TargetBounds => _bounds;

    [HideInInspector]
    public float max, min;

    private Camera _bakeCamera;
    private RenderTexture _heightTexture;
    private RenderTexture _tempHeightTexture;
    private Material _heightBakeMaterial;
    private Bounds _bounds;
    private ComputeShader mipCS;

    public void SaveRenderTextureAsRAW(RenderTexture rt, string path)
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
    }

    [ContextMenu("Bake heightmap")]
    void BakeHeightmap()
    {
        heightBakeShader = Resources.Load<Shader>("Mesh Shaders/MeshToHeightfield");
        mipCS = Resources.Load<ComputeShader>("Compute Shaders/MipMapGen");

        if (targetRenderer == null)
        {
            Debug.LogError("MeshHeightmapBakerGPU: No targetRenderer assigned.");
            return;
        }

        if (heightBakeShader == null)
        {
            Debug.LogError("MeshHeightmapBakerGPU: No heightBakeShader assigned.");
            return;
        }

        _bounds = targetRenderer.bounds;

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

        //Create texture
        {
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

            _heightTexture = new RenderTexture(desc);
            _heightTexture.name = "MeshHeightmap_GPU";
            _heightTexture.wrapMode = TextureWrapMode.Clamp;
            _heightTexture.filterMode = FilterMode.Bilinear;
            _heightTexture.Create();

            _tempHeightTexture = new RenderTexture(desc);
            _tempHeightTexture.name = "MeshHeightmapTemp_GPU";
            _tempHeightTexture.wrapMode = TextureWrapMode.Clamp;
            _tempHeightTexture.filterMode = FilterMode.Bilinear;
            _tempHeightTexture.Create();
        }

        //Configure camera
        {
            Vector3 center = _bounds.center;

            _bakeCamera.transform.position = new Vector3(center.x, _bounds.max.y + 10f, center.z);
            _bakeCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            float halfSize = Mathf.Max(_bounds.extents.x, _bounds.extents.z);
            _bakeCamera.orthographicSize = halfSize;

            float heightRange = _bounds.size.y + 20f;
            _bakeCamera.nearClipPlane = 0.01f;
            _bakeCamera.farClipPlane = heightRange;
        }

        var previousRT = _bakeCamera.targetTexture;
        int oldMask = _bakeCamera.cullingMask;
        Color oldBg = _bakeCamera.backgroundColor;
        CameraClearFlags oldFlags = _bakeCamera.clearFlags;

        _bakeCamera.targetTexture = _heightTexture;
        _bakeCamera.cullingMask = bakeLayer;
        _bakeCamera.clearFlags = CameraClearFlags.SolidColor;
        _bakeCamera.backgroundColor = new Color(_bounds.min.y, 0, 0, 0);

        // Render using replacement shader so the output is raw world height.
        _bakeCamera.RenderWithShader(heightBakeShader, "");

        _bakeCamera.targetTexture = previousRT;
        _bakeCamera.cullingMask = oldMask;
        _bakeCamera.backgroundColor = oldBg;
        _bakeCamera.clearFlags = oldFlags;
        SaveRenderTextureAsRAW(_heightTexture, outputPath);

        int srcWidth = _heightTexture.width;
        int srcHeight = _heightTexture.height;
        int mipCount = _heightTexture.mipmapCount;
        int mipKernel = mipCS.FindKernel("ReduceMaxMip");

        for (int srcMip = 0; srcMip < mipCount - 1; srcMip++)
        {
            int dstWidth = Mathf.Max(1, srcWidth / 2);
            int dstHeight = Mathf.Max(1, srcHeight / 2);

            mipCS.SetInt("_SrcMip", srcMip);
            mipCS.SetInt("_SrcMipSizeX", srcWidth);
            mipCS.SetInt("_SrcMipSizeY", srcHeight);

            mipCS.SetTexture(mipKernel, "_SrcTex", _heightTexture);
            mipCS.SetTexture(mipKernel, "_DstTex", _tempHeightTexture, srcMip + 1);

            int groupsX = Mathf.CeilToInt(dstWidth / 8.0f);
            int groupsY = Mathf.CeilToInt(dstHeight / 8.0f);

            mipCS.Dispatch(mipKernel, groupsX, groupsY, 1);

            Graphics.CopyTexture(_tempHeightTexture, 0, srcMip + 1, _heightTexture, 0, srcMip + 1);

            srcWidth = dstWidth;
            srcHeight = dstHeight;
        }
    }
}
