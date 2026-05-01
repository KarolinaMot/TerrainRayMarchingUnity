using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Unity.Burst.Intrinsics.X86.Avx;
using static Unity.VisualScripting.Member;

public class HeightfieldShadowMap : MonoBehaviour
{
    public bool convergeShadows;
    public int shadowMapResolution;
    public float distanceForHit;
    public float sunAngularRadius = 0.2f;
    public float shadowEpsilon = 0.2f;
    public int maxSteps;
    public int convergenceLimit = 100;
    public Vector2 mapSize;

    public RenderTexture shadowMap;
    private ComputeShader shadowMapCS;
    private MeshToHeightField meshToHeightfield;
    private SdfRenderer sdfRenderer;
    private Light sun;
    public Matrix4x4 worldToLightClip;
    private RenderTexture[] temporalShadow = new RenderTexture[2];

    private Vector3 prevCameraPos = Vector3.zero;
    private Quaternion prevCameraRot = Quaternion.identity;
    private Camera camera;
    private int convergenceCounter;

    float prevShadowSoftness;
    Vector3 prevSunDirection;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        camera = GetComponent<Camera>();

        var desc = new RenderTextureDescriptor(shadowMapResolution, shadowMapResolution, GraphicsFormat.R32_SFloat, 0);
        desc.enableRandomWrite = true;
        desc.useMipMap = true;
        desc.autoGenerateMips = false;
        desc.msaaSamples = 1;
        desc.dimension = TextureDimension.Tex2D;
        desc.volumeDepth = 1;
        desc.depthBufferBits = 0;

        for (int i = 0; i < 2; i++)
        {
            temporalShadow[i] = new RenderTexture(shadowMapResolution, shadowMapResolution, 0);
            temporalShadow[i].graphicsFormat = GraphicsFormat.R32G32_SFloat;
            temporalShadow[i].enableRandomWrite = true;
            temporalShadow[i].wrapMode = TextureWrapMode.Clamp;
            temporalShadow[i].filterMode = FilterMode.Bilinear;
            temporalShadow[i].Create();
        }

        shadowMap = new RenderTexture(desc);
        shadowMap.filterMode = FilterMode.Trilinear;
        shadowMap.wrapMode = TextureWrapMode.Clamp;
        shadowMap.Create();

        shadowMapCS = Resources.Load<ComputeShader>("Compute Shaders/BakeShadows");
        meshToHeightfield = GetComponent<MeshToHeightField>();
        sdfRenderer = GetComponent<SdfRenderer>();
        sun = RenderSettings.sun;
        mapSize.x = meshToHeightfield.TargetBounds.size.x;
        mapSize.y = meshToHeightfield.TargetBounds.size.z;

        //CommandBuffer cmd = new CommandBuffer()
        //{
        //    name = "ShadowMap creation"
        //};

        //DispatchShadowmap(cmd);

        //cmd.Release();
    }

    private void ClearTemporal(CommandBuffer cmd)
    {
        for (int i = 0; i < 2; i++)
        {
            cmd.SetRenderTarget(temporalShadow[i]);
            cmd.ClearRenderTarget(false, true, UnityEngine.Color.clear); // RGFloat → (0,0)
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
        Bounds bounds = meshToHeightfield.TargetBounds;

        // 2. Make light view matrix first
        Vector3 lightDir = sun.transform.forward;

        Matrix4x4 lightClipToWorld = worldToLightClip.inverse;
        cmd.SetComputeTextureParam(shadowMapCS, kernel, "_HeightMap", meshToHeightfield.HeightTexture);
        cmd.SetComputeTextureParam(shadowMapCS, kernel, "_Result", shadowMap);
        cmd.SetComputeVectorParam(shadowMapCS, "_SunDirection", lightDir);
        cmd.SetComputeVectorParam(shadowMapCS, "_ChunkSize", mapSize);
        cmd.SetComputeVectorParam(shadowMapCS, "_ChunkOrigin", bounds.min);
        cmd.SetComputeIntParam(shadowMapCS, "_MaxSteps", maxSteps);
        cmd.SetComputeFloatParam(shadowMapCS, "_DistanceForHit", distanceForHit);
        cmd.SetComputeFloatParam(shadowMapCS, "_SunAngularRadius", sunAngularRadius);
        cmd.SetComputeFloatParam(shadowMapCS, "_ShadowEpsilon", shadowEpsilon);
        cmd.SetComputeTextureParam(shadowMapCS, kernel, "_TemporalShadow", temporalShadow[Time.frameCount % 2]);
        cmd.SetComputeTextureParam(shadowMapCS, kernel, "_TemporalShadowPrev", temporalShadow[(Time.frameCount + 1) % 2]);
        cmd.SetComputeIntParam(shadowMapCS, "_ConvergeShadows", convergeShadows ? 1 : 0);

        cmd.DispatchCompute(shadowMapCS, kernel,
        Mathf.CeilToInt(shadowMap.width / 16.0f),
        Mathf.CeilToInt(shadowMap.height / 16.0f),
        1);

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
