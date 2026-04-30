using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Unity.VisualScripting.Member;

public class HeightfieldShadowMap : MonoBehaviour
{
    public int shadowMapResolution;
    public float distanceForHit;
    public float sunAngularRadius = 0.2f;
    public float shadowEpsilon = 0.2f;
    public int maxSteps;
    public Vector2 mapSize;

    public RenderTexture shadowMap;
    private ComputeShader shadowMapCS;
    private MeshToHeightField meshToHeightfield;
    private SdfRenderer sdfRenderer;
    private Light sun;
    public Matrix4x4 worldToLightClip;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        var desc = new RenderTextureDescriptor(shadowMapResolution, shadowMapResolution, GraphicsFormat.R32_SFloat, 0);
        desc.enableRandomWrite = true;
        desc.useMipMap = true;
        desc.autoGenerateMips = false;
        desc.msaaSamples = 1;
        desc.dimension = TextureDimension.Tex2D;
        desc.volumeDepth = 1;
        desc.depthBufferBits = 0;

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
        DispatchShadowmap();
    }

    Vector3[] GetBoundsCorners(Bounds b)
    {
        Vector3 min = b.min;
        Vector3 max = b.max;

        return new Vector3[]
        {
        new Vector3(min.x, min.y, min.z),
        new Vector3(max.x, min.y, min.z),
        new Vector3(min.x, max.y, min.z),
        new Vector3(max.x, max.y, min.z),

        new Vector3(min.x, min.y, max.z),
        new Vector3(max.x, min.y, max.z),
        new Vector3(min.x, max.y, max.z),
        new Vector3(max.x, max.y, max.z),
        };
    }

    void DispatchShadowmap()
    {
        CommandBuffer cmd = new CommandBuffer()
        {
            name = "ShadowMap creation"
        };
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
        cmd.SetComputeFloatParam(shadowMapCS, "_MaxSteps", maxSteps);
        cmd.SetComputeFloatParam(shadowMapCS, "_DistanceForHit", distanceForHit);
        cmd.SetComputeFloatParam(shadowMapCS, "_SunAngularRadius", sunAngularRadius);
        cmd.SetComputeFloatParam(shadowMapCS, "_ShadowEpsilon", shadowEpsilon);

        cmd.DispatchCompute(shadowMapCS, kernel,
        Mathf.CeilToInt(shadowMap.width / 16.0f),
        Mathf.CeilToInt(shadowMap.height / 16.0f),
        1);

        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

    }
    // Update is called once per frame
    void Update()
    {
    }
}
