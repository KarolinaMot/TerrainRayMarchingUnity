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
    public int maxSteps;
    public float mapSize;

    public RenderTexture shadowMap;
    private ComputeShader shadowMapCS;
    private MeshToHeightField meshToHeightfield;
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

        shadowMapCS = Resources.Load<ComputeShader>("Compute Shaders/HeightmapShadowmap");
        meshToHeightfield = GetComponent<MeshToHeightField>();
        sun = RenderSettings.sun;


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

    // Update is called once per frame
    void Update()
    {
        CommandBuffer cmd = new CommandBuffer()
        {
            name = "ShadowMap creation"
        };
        int kernel = shadowMapCS.FindKernel("Main");

        // 1. World-space terrain bounds
        Bounds bounds = meshToHeightfield.TargetBounds;

        // 2. Make light view matrix first
        Vector3 lightDir = -sun.transform.forward;
        Vector3 center = bounds.center;
        float distance = bounds.size.magnitude;

        Vector3 lightPos = center - lightDir * distance;

        Matrix4x4 view = Matrix4x4.TRS(
            lightPos,
            sun.transform.rotation,
            Vector3.one
        ).inverse;

        // 3. Transform bounds corners into light view space
        Vector3[] corners = GetBoundsCorners(bounds);

        Vector3 lightMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 lightMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < 8; i++)
        {
            Vector3 p = view.MultiplyPoint(corners[i]);
            lightMin = Vector3.Min(lightMin, p);
            lightMax = Vector3.Max(lightMax, p);
        }

        // 4. Build ortho from LIGHT-SPACE bounds
        float padding = 10f;

        Matrix4x4 proj = Matrix4x4.Ortho(
            lightMin.x - padding,
            lightMax.x + padding,
            lightMin.y - padding,
            lightMax.y + padding,
            -lightMax.z - padding,
            -lightMin.z + padding
        );
        proj = GL.GetGPUProjectionMatrix(proj, true);

        worldToLightClip = proj * view;
        Matrix4x4 lightClipToWorld = worldToLightClip.inverse;

        cmd.SetComputeTextureParam(shadowMapCS, kernel, "_HeightMap", meshToHeightfield.HeightTexture);
        cmd.SetComputeTextureParam(shadowMapCS, kernel, "_Result", shadowMap);
        cmd.SetComputeVectorParam(shadowMapCS, "_SunDirection", sun.transform.forward);
        cmd.SetComputeFloatParam(shadowMapCS, "_ChunkSize", mapSize);
        cmd.SetComputeFloatParam(shadowMapCS, "_MaxStepsOptimized", maxSteps);
        cmd.SetComputeFloatParam(shadowMapCS, "_DistanceForHit", distanceForHit);
        cmd.SetComputeMatrixParam(shadowMapCS, "_WorldToLightClip", worldToLightClip);
        cmd.SetComputeMatrixParam(shadowMapCS, "_LightClipToWorld", lightClipToWorld);

        cmd.DispatchCompute(shadowMapCS, kernel,
        Mathf.CeilToInt(shadowMap.width / 16.0f),
        Mathf.CeilToInt(shadowMap.height / 16.0f),
        1);

        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();
    }
}
