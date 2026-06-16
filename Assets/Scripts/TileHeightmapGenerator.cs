

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

using static Unity.Burst.Intrinsics.X86.Avx;

public class TileHeightmapGenerator
{
    private Shader heightShader;
    private ComputeShader mipShader;
    private Material bakeMaterial;
    private int mipKernel;
    private RenderTexture mipTemp2D;

    public TileHeightmapGenerator()
    {
        heightShader = Resources.Load<Shader>("Mesh Shaders/MeshToHeightfield");
        mipShader = Resources.Load<ComputeShader>("Compute Shaders/MipMapGen");
        if (bakeMaterial == null)
            bakeMaterial = new Material(heightShader);
        mipKernel = mipShader.FindKernel("ReduceMaxMip");

        if (mipTemp2D != null)
            return;
    }

    public void OnDestroy()
    {
        if (bakeMaterial != null)
        {
            Object.Destroy(bakeMaterial);
            bakeMaterial = null;
        }

        if (mipTemp2D != null)
        {
            mipTemp2D.Release();
            Object.Destroy(mipTemp2D);
            mipTemp2D = null;
        }
    }

    void CreateMipTempIfNeeded(int dimensions)
    {
        if (mipTemp2D != null)
            return;

        var desc = new RenderTextureDescriptor(dimensions, dimensions)
        {
            dimension = TextureDimension.Tex2D,
            volumeDepth = 1,
            msaaSamples = 1,
            depthBufferBits = 0,
            graphicsFormat = GraphicsFormat.R32_SFloat,
            sRGB = false,
            useMipMap = true,
            autoGenerateMips = false,
            enableRandomWrite = true
        };

        mipTemp2D = new RenderTexture(desc);
        mipTemp2D.Create();
    }

    public void GetHeightmap(CommandBuffer cmd, GameObject tile, int dimensions, int mipCount, RenderTexture heightTex, int index)
    {
        MeshFilter meshFilter = tile.GetComponentInChildren<MeshFilter>();

        if (!meshFilter)
        {
            Debug.LogError(string.Format("Terrain tile {0} does not have a mesh filter", tile.name));

        }

        cmd.SetRenderTarget(heightTex, 0, CubemapFace.Unknown, index);

        Mesh mesh = meshFilter.sharedMesh;
        Transform meshT = meshFilter.transform;
        Bounds bounds = mesh.bounds;
        Vector3 center = bounds.center;

        float halfSize =
            Mathf.Max(bounds.extents.x, bounds.extents.z);

        Vector3 cameraPos =
            new Vector3(
                center.x,
                bounds.max.y + 10f,
                center.z
            );

        Matrix4x4 view = Matrix4x4.TRS(
          cameraPos,
          Quaternion.LookRotation(Vector3.up, -Vector3.forward),
          Vector3.one
        ).inverse;

        Matrix4x4 proj = Matrix4x4.Ortho(
            -halfSize,
            halfSize,
            -halfSize,
            halfSize,
            0.01f,
            100000f
        );

        cmd.BeginSample("Heightmap Bake");
        cmd.ClearRenderTarget(true, true, Color.black);

        cmd.SetViewProjectionMatrices(view, proj);

        cmd.DrawMesh(
            mesh,
            Matrix4x4.identity,
            bakeMaterial,
            0
        );
        cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        cmd.EndSample("Heightmap Bake");

        cmd.BeginSample("Heightmap mipmap gen");

        int srcWidth = dimensions;
        int srcHeight = dimensions;


        GenerateMipsForSlice(cmd, heightTex, index, heightTex.width, heightTex.mipmapCount);
        cmd.EndSample("Heightmap mipmap gen");

    }

    void GenerateMipsForSlice(CommandBuffer cmd, RenderTexture heightTex, int slice, int dimensions, int mipCount)
    {
        CreateMipTempIfNeeded(dimensions);

        int srcWidth = dimensions;
        int srcHeight = dimensions;

        for (int srcMip = 0; srcMip < mipCount - 1; srcMip++)
        {
            int dstMip = srcMip + 1;
            int dstWidth = Mathf.Max(1, srcWidth / 2);
            int dstHeight = Mathf.Max(1, srcHeight / 2);

            cmd.SetComputeIntParam(mipShader, "_SrcMip", srcMip);
            cmd.SetComputeIntParam(mipShader, "_SrcMipSizeX", srcWidth);
            cmd.SetComputeIntParam(mipShader, "_SrcMipSizeY", srcHeight);
            cmd.SetComputeIntParam(mipShader, "_SrcSlice", slice);
            cmd.SetComputeIntParam(mipShader, "_DstSlice", slice);

            cmd.SetComputeTextureParam(mipShader, mipKernel, "_SrcTex", heightTex);

            cmd.SetComputeTextureParam(mipShader, mipKernel, "_DstTex", mipTemp2D, dstMip);
            int groupsX = Mathf.CeilToInt(dstWidth / 8.0f);
            int groupsY = Mathf.CeilToInt(dstHeight / 8.0f);

            cmd.DispatchCompute(mipShader, mipKernel, groupsX, groupsY, 1);

            // Copy temp 2D mip back into heightTex array slice mip.
            cmd.CopyTexture(
                mipTemp2D, 0, dstMip,
                heightTex, slice, dstMip
            );

            srcWidth = dstWidth;
            srcHeight = dstHeight;
        }
    }
}
