using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Unity.Burst.Intrinsics.X86.Avx;

public class MeshToHeightField : MonoBehaviour
{
    [Header("Target")]
    public Mesh terrain;

    [Header("Ouput")]
    public string outputPath;


    [Header("Bake Settings")]
    public int resolution = 2048;
    public LayerMask bakeLayer = ~0;

    Shader heightBakeShader;
    public RenderTexture HeightTexture;
    public Camera BakeCamera => _bakeCamera;
    public Bounds TargetBounds => _bounds;

    public float max, min;

    private Camera _bakeCamera;
    private RenderTexture _tempHeightTexture;
    private Material _heightBakeMaterial;
    private Bounds _bounds;
    private ComputeShader mipCS;

    private void Start()
    {
        heightBakeShader = Resources.Load<Shader>("Mesh Shaders/TestBakeRed");
        mipCS = Resources.Load<ComputeShader>("Compute Shaders/MipMapGen");

        if (terrain == null)
        {
            Debug.LogError("MeshHeightmapBakerGPU: No terrain assigned.");
            return;
        }

        if (heightBakeShader == null)
        {
            Debug.LogError("MeshHeightmapBakerGPU: No heightBakeShader assigned.");
            return;
        }

        _bounds = terrain.bounds;

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

            HeightTexture = new RenderTexture(desc);
            HeightTexture.name = "MeshHeightmap_GPU";
            HeightTexture.wrapMode = TextureWrapMode.Clamp;
            HeightTexture.filterMode = FilterMode.Bilinear;
            HeightTexture.Create();

            _tempHeightTexture = new RenderTexture(desc);
            _tempHeightTexture.name = "MeshHeightmapTemp_GPU";
            _tempHeightTexture.wrapMode = TextureWrapMode.Clamp;
            _tempHeightTexture.filterMode = FilterMode.Bilinear;
            _tempHeightTexture.Create();
        }

        BakeHeightmap();
        SaveRenderTextureAsRAW(HeightTexture, outputPath);

    }

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
    void BakeHeightmap()
    {
        CommandBuffer cmd = new CommandBuffer();
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            bool included = (_bakeCamera.cullingMask & (1 << r.gameObject.layer)) != 0;
            if (included)
                Debug.Log($"Bake sees renderer: {r.name}, layer {r.gameObject.layer}, enabled {r.enabled}");
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
            _bakeCamera.farClipPlane = 100000f;
        }

        var previousRT = _bakeCamera.targetTexture;
        int oldMask = _bakeCamera.cullingMask;
        Color oldBg = _bakeCamera.backgroundColor;
        CameraClearFlags oldFlags = _bakeCamera.clearFlags;

        _bakeCamera.targetTexture = HeightTexture;
        _bakeCamera.cullingMask = bakeLayer.value;
        _bakeCamera.clearFlags = CameraClearFlags.SolidColor;
        _bakeCamera.backgroundColor = new Color(_bounds.min.y, 0, 0, 0);
        _bakeCamera.orthographic = true;

        // Render using replacement shader so the output is raw world height.
        Debug.Log($"BakeCam pos: {_bakeCamera.transform.position}");
        Debug.Log($"BakeCam forward: {_bakeCamera.transform.forward}");
        Debug.Log($"Bounds min/max: {_bounds.min} / {_bounds.max}");
        Debug.Log($"Ortho: {_bakeCamera.orthographic}, size: {_bakeCamera.orthographicSize}");
        Debug.Log($"Near/Far: {_bakeCamera.nearClipPlane} / {_bakeCamera.farClipPlane}");

        _bakeCamera.cullingMask = ~0;
        _bakeCamera.clearFlags = CameraClearFlags.SolidColor;
        _bakeCamera.backgroundColor = Color.black;

        cmd.BeginSample("Heightmap Bake");
        cmd.SetRenderTarget(HeightTexture);
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

        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        _bakeCamera.targetTexture = previousRT;
        _bakeCamera.cullingMask = oldMask;
        _bakeCamera.backgroundColor = oldBg;
        _bakeCamera.clearFlags = oldFlags;

        int srcWidth = HeightTexture.width;
        int srcHeight = HeightTexture.height;
        int mipCount = HeightTexture.mipmapCount;
        int mipKernel = mipCS.FindKernel("ReduceMaxMip");

        for (int srcMip = 0; srcMip < mipCount - 1; srcMip++)
        {
            int dstWidth = Mathf.Max(1, srcWidth / 2);
            int dstHeight = Mathf.Max(1, srcHeight / 2);

            mipCS.SetInt("_SrcMip", srcMip);
            mipCS.SetInt("_SrcMipSizeX", srcWidth);
            mipCS.SetInt("_SrcMipSizeY", srcHeight);

            mipCS.SetTexture(mipKernel, "_SrcTex", HeightTexture);
            mipCS.SetTexture(mipKernel, "_DstTex", _tempHeightTexture, srcMip + 1);

            int groupsX = Mathf.CeilToInt(dstWidth / 8.0f);
            int groupsY = Mathf.CeilToInt(dstHeight / 8.0f);

            mipCS.Dispatch(mipKernel, groupsX, groupsY, 1);

            Graphics.CopyTexture(_tempHeightTexture, 0, srcMip + 1, HeightTexture, 0, srcMip + 1);

            srcWidth = dstWidth;
            srcHeight = dstHeight;
        }
    }
}
