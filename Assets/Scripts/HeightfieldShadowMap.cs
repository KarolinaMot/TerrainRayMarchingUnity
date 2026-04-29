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

    // Update is called once per frame
    void Update()
    {
        CommandBuffer cmd = new CommandBuffer()
        {
            name = "ShadowMap creation"
        };
        int kernel = shadowMapCS.FindKernel("Main");

        Vector3 sunDir = sun.transform.forward;

        Vector3 terrainCenter = new Vector3(
            mapSize * 0.5f,
            (meshToHeightfield.min + meshToHeightfield.max) * 0.5f,
            mapSize * 0.5f
        );
        Vector3 center = terrainCenter;

        Vector3 lightDir = -sun.transform.forward;

        float distance = 2000f;

        Vector3 lightPosition = center - lightDir * distance;
        Quaternion lightRotation = sun.transform.rotation;

        Matrix4x4 view = Matrix4x4.TRS(
            lightPosition,
            lightRotation,
            Vector3.one
        ).inverse;

        float size = mapSize * 0.5f;

        Matrix4x4 proj = Matrix4x4.Ortho(
            -size,
             size,
            -size,
             size,
             0.1f,
             5000f
        );

        proj = GL.GetGPUProjectionMatrix(proj, true);

        Matrix4x4 worldToLightClip = proj * view;
        Matrix4x4 lightClipToWorld = worldToLightClip.inverse;

        cmd.SetComputeTextureParam(shadowMapCS, kernel, "_HeightMap", meshToHeightfield.HeightTexture);
        cmd.SetComputeTextureParam(shadowMapCS, kernel, "_Result", shadowMap);
        cmd.SetComputeVectorParam(shadowMapCS, "_SunDirection", sunDir);
        cmd.SetComputeFloatParam(shadowMapCS, "_ChunkSize", mapSize);
        cmd.SetComputeFloatParam(shadowMapCS, "_MaxStepsOptimized", maxSteps);
        cmd.SetComputeFloatParam(shadowMapCS, "_DistanceForHit", distanceForHit);
        cmd.SetComputeMatrixParam(shadowMapCS, "_WorldToLightClip", worldToLightClip);
        cmd.SetComputeMatrixParam(shadowMapCS, "_LightClipToWorld", lightClipToWorld);

        Debug.Log("_WorldToLightClip:");
        Debug.Log(worldToLightClip);

        Debug.Log("_LightClipToWorld:");
        Debug.Log(lightClipToWorld);

        Debug.Log("Inverse check:");
        Debug.Log(worldToLightClip * lightClipToWorld);

        cmd.DispatchCompute(shadowMapCS, kernel,
        Mathf.CeilToInt(shadowMap.width / 16.0f),
        Mathf.CeilToInt(shadowMap.height / 16.0f),
        1);

        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();
    }
}
