using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class HeightfieldShadowMap : MonoBehaviour
{
    public bool convergeShadows;
    public int shadowMapResolution;
    public float distanceForHit;
    public float sunAngularRadius = 0.2f;
    public float shadowEpsilon = 0.2f;
    public int maxSteps;
    public int convergenceLimit = 100;
    private ComputeShader shadowMapCS;
    private MeshToHeightField meshToHeightfield;
    private Light sun;
    public Matrix4x4 worldToLightClip;
  //  private RenderTexture[] temporalShadow = new RenderTexture[2];
    public RenderTexture[] rtShadowArray;

    private Vector3 prevCameraPos = Vector3.zero;
    private Quaternion prevCameraRot = Quaternion.identity;
    private Camera camera;
    private int convergenceCounter;

    float prevShadowSoftness;
    Vector3 prevSunDirection;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ReleaseShadowTextures();
        camera = GetComponent<Camera>();
        shadowMapCS = Resources.Load<ComputeShader>("Compute Shaders/BakeShadows");
        meshToHeightfield = GetComponent<MeshToHeightField>();
        sun = RenderSettings.sun;

        RenderTextureDescriptor desc = new RenderTextureDescriptor(shadowMapResolution, shadowMapResolution);
        desc.dimension = TextureDimension.Tex2DArray;
        desc.volumeDepth = meshToHeightfield.chunks.Count;
        desc.msaaSamples = 1;
        desc.depthBufferBits = 0;
        desc.graphicsFormat = GraphicsFormat.R32G32_SFloat;
        desc.sRGB = false;
        desc.useMipMap = false;
        desc.autoGenerateMips = false;
        desc.enableRandomWrite = true;

        rtShadowArray = new RenderTexture[2];
        for (int i = 0; i < 2; i++)
        {
            rtShadowArray[i] = new RenderTexture(desc);
            rtShadowArray[i].name = "Shadowmap_GPU_" + meshToHeightfield.terrainParent.name+i;
            rtShadowArray[i].wrapMode = TextureWrapMode.Clamp;
            rtShadowArray[i].filterMode = FilterMode.Bilinear;
            rtShadowArray[i].Create();
        }

        //for (int i = 0; i < 2; i++)
        //{
        //    temporalShadow[i] = new RenderTextureDescriptor(shadowMapResolution, shadowMapResolution);
        //    temporalShadow[i].graphicsFormat = GraphicsFormat.R32G32_SFloat;
        //    temporalShadow[i].enableRandomWrite = true;
        //    temporalShadow[i].wrapMode = TextureWrapMode.Clamp;
        //    temporalShadow[i].filterMode = FilterMode.Bilinear;
        //    temporalShadow[i].Create();
        //}

        //mapSize.x = meshToHeightfield.TargetBounds.size.x;
        //mapSize.y = meshToHeightfield.TargetBounds.size.z;

    }

    //private void OnDisable()
    //{
    //    ReleaseShadowTextures();
    //}

    private void OnDestroy()
    {
        ReleaseShadowTextures();
    }

    private void ReleaseShadowTextures()
    {
        if (rtShadowArray == null) return;

        for (int i = 0; i < rtShadowArray.Length; i++)
        {
            if (rtShadowArray[i] != null)
            {
                rtShadowArray[i].Release();
                Destroy(rtShadowArray[i]);
                rtShadowArray[i] = null;
            }
        }
    }
    private void ClearTemporal(CommandBuffer cmd)
    {
        for (int i = 0; i < 2; i++)
        {
            for (int slice = 0; slice < rtShadowArray[i].volumeDepth; slice++)
            {
                cmd.SetRenderTarget(rtShadowArray[i], 0, CubemapFace.Unknown, slice);
                cmd.ClearRenderTarget(false, true, Color.clear);
            }
        }
    }

    bool CameraMoved()
    {
        bool moved = camera.transform.position != prevCameraPos ||
                     camera.transform.rotation != prevCameraRot;

        prevCameraPos = camera.transform.position;
        prevCameraRot = camera.transform.rotation;

        return moved;
    }

    bool SettingsChanged()
    {
        bool changed = prevShadowSoftness != sunAngularRadius ||
            prevSunDirection != sun.transform.forward;

        prevShadowSoftness = sunAngularRadius;
        prevSunDirection = sun.transform.forward;

        return changed;
    }

    void DispatchShadowmap(CommandBuffer cmd)
    {

        int kernel = shadowMapCS.FindKernel("Main");

        // 1. World-space terrain bounds
        //Bounds bounds = meshToHeightfield.TargetBounds;

        // 2. Make light view matrix first
        Vector3 lightDir = sun.transform.forward;

        Matrix4x4 lightClipToWorld = worldToLightClip.inverse;
        cmd.SetComputeTextureParam(shadowMapCS, kernel, "_Result", rtShadowArray[Time.frameCount % 2]);
        cmd.SetComputeTextureParam(shadowMapCS, kernel, "_ResultPrev", rtShadowArray[(Time.frameCount +1) % 2]);
        cmd.SetComputeTextureParam(shadowMapCS, kernel, "_HeightMapArray", meshToHeightfield.rtArray);
        cmd.SetComputeVectorParam(shadowMapCS, "_SunDirection", lightDir);
        cmd.SetComputeIntParam(shadowMapCS, "_MaxSteps", maxSteps);
        cmd.SetComputeFloatParam(shadowMapCS, "_DistanceForHit", distanceForHit);
        cmd.SetComputeFloatParam(shadowMapCS, "_SunAngularRadius", sunAngularRadius);
        cmd.SetComputeFloatParam(shadowMapCS, "_ShadowEpsilon", shadowEpsilon);
        cmd.SetComputeBufferParam(shadowMapCS, kernel, "_Chunks", meshToHeightfield.chunkBuffer);
        cmd.SetComputeIntParam(shadowMapCS, "_ChunkCount", meshToHeightfield.chunkData.Length);
        cmd.SetComputeIntParam(shadowMapCS, "_ConvergeShadows", convergeShadows ? 1 : 0);

        cmd.DispatchCompute(shadowMapCS, kernel,
        Mathf.CeilToInt(rtShadowArray[0].width / 8f),
        Mathf.CeilToInt(rtShadowArray[0].height / 8f),
        Mathf.CeilToInt(meshToHeightfield.chunkData.Length));

        convergenceCounter++;

    }

    // Update is called once per frame
    void Update()
    {
        CommandBuffer cmd = new CommandBuffer()
        {
            name = "ShadowMap creation"
        };

        if (CameraMoved() || SettingsChanged() || !convergeShadows)
        {
            ClearTemporal(cmd);
            convergenceCounter = 0;
        }

        if(convergenceCounter<convergenceLimit)
            DispatchShadowmap(cmd);

        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

    }
}
