using System.Collections.Generic;
using System.Runtime.InteropServices;

using UnityEngine;
using UnityEngine.Rendering;

public class TileBVH
{
    public struct BVHNode
    {
        public Vector3 aabbMin;
        public int leftChild;
        public Vector3 aabbMax;
        public int rightChild;
        public int firstPrim;
        public int primCount;
    }

    int roodNodeID = 0;
    private List<BVHNode> nodes;
    private int[] indices;

    public ComputeBuffer bvhNodeBuffer;
    public ComputeBuffer bvhIndexBuffer;
    public TileBVH(int maxChunkCount)
    {
        nodes = new List<BVHNode>(maxChunkCount * 2 - 1);
        int stride = Marshal.SizeOf<BVHNode>();
        bvhNodeBuffer = new ComputeBuffer(maxChunkCount * 2 - 1, stride);
        bvhIndexBuffer = new ComputeBuffer(maxChunkCount, sizeof(int));
        indices = new int[maxChunkCount];
        for (int i = 0; i < maxChunkCount; i++)
        {
            indices[i] = i;
        }
    }

    public void Destroy()
    {
        if (bvhNodeBuffer != null)
        {
            bvhNodeBuffer.Release();
            bvhNodeBuffer = null;
        }
        if (bvhIndexBuffer != null)
        {
            bvhIndexBuffer.Release();
            bvhIndexBuffer = null;
        }
    }

    public void Build(CommandBuffer cmd, int chunkCount, TileDataGPU[] chunks)
    {
        nodes.Clear();

        BVHNode root = new BVHNode();
        root.firstPrim = 0;
        root.primCount = chunkCount;
        root.leftChild = -1;
        root.rightChild = -1;
        nodes.Add(root);

        UpdateNodeBounds(roodNodeID, chunks);
        Subdivide(roodNodeID, chunks);

        cmd.SetBufferData(bvhNodeBuffer, nodes);
        cmd.SetBufferData(bvhIndexBuffer, indices);
    }

    public void Refit(CommandBuffer cmd, TileDataGPU[] chunks)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            BVHNode node = nodes[i];

            node.aabbMin = new Vector3(1e30f, 1e30f, 1e30f);
            node.aabbMax = new Vector3(-1e30f, -1e30f, -1e30f);

            if (node.primCount > 0)
            {
                // Leaf: bounds from chunks
                for (int p = 0; p < node.primCount; p++)
                {
                    int chunkIndex = indices[node.firstPrim + p];
                    TileDataGPU chunk = chunks[chunkIndex];

                    if (chunk.heightSlice < 0)
                        continue;

                    Bounds chunkBound = new Bounds();
                    chunkBound.SetMinMax(chunk.boundsMin, chunk.boundsMax);

                    Bounds worldBounds = TransformBounds(chunk.localToWorld, chunkBound);

                    node.aabbMin = Vector3.Min(node.aabbMin, worldBounds.min);
                    node.aabbMax = Vector3.Max(node.aabbMax, worldBounds.max);
                }
            }
            else
            {
                // Internal: bounds from children
                if (node.leftChild >= 0)
                {
                    BVHNode left = nodes[node.leftChild];

                    node.aabbMin = Vector3.Min(node.aabbMin, left.aabbMin);
                    node.aabbMax = Vector3.Max(node.aabbMax, left.aabbMax);
                }

                if (node.rightChild >= 0)
                {
                    BVHNode right = nodes[node.rightChild];

                    node.aabbMin = Vector3.Min(node.aabbMin, right.aabbMin);
                    node.aabbMax = Vector3.Max(node.aabbMax, right.aabbMax);
                }
            }

            nodes[i] = node;
        }

        cmd.SetBufferData(bvhNodeBuffer, nodes);
    }

    public static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        Vector3[] corners = new Vector3[8]
        {
        center + new Vector3(-extents.x, -extents.y, -extents.z),
        center + new Vector3( extents.x, -extents.y, -extents.z),
        center + new Vector3(-extents.x,  extents.y, -extents.z),
        center + new Vector3( extents.x,  extents.y, -extents.z),

        center + new Vector3(-extents.x, -extents.y,  extents.z),
        center + new Vector3( extents.x, -extents.y,  extents.z),
        center + new Vector3(-extents.x,  extents.y,  extents.z),
        center + new Vector3( extents.x,  extents.y,  extents.z),
        };

        Bounds transformedBounds =
            new Bounds(matrix.MultiplyPoint3x4(corners[0]), Vector3.zero);

        for (int i = 1; i < 8; i++)
        {
            transformedBounds.Encapsulate(
                matrix.MultiplyPoint3x4(corners[i])
            );
        }

        return transformedBounds;
    }

    void UpdateNodeBounds(int nodeIdx, TileDataGPU[] chunks)
    {
        BVHNode node = nodes[nodeIdx];

        node.aabbMin = new Vector3(1e30f, 1e30f, 1e30f);
        node.aabbMax = new Vector3(-1e30f, -1e30f, -1e30f);

        for (int first = node.firstPrim, i = 0; i < node.primCount; i++)
        {
            int chunkIndex = indices[first + i];
            TileDataGPU chunk = chunks[chunkIndex];
            if (chunk.heightSlice < 0)
                continue;
            Bounds chunkBound = new Bounds(chunk.boundsMin, chunk.boundsMax);
            chunkBound.SetMinMax(chunk.boundsMin, chunk.boundsMax);

            Bounds worldBounds = TransformBounds(chunk.localToWorld, chunkBound);

            node.aabbMin = Vector3.Min(node.aabbMin, worldBounds.min);
            node.aabbMax = Vector3.Max(node.aabbMax, worldBounds.max);
        }

        nodes[nodeIdx] = node;
    }

    void Subdivide(int nodeIdx, TileDataGPU[] chunks)
    {
        BVHNode node = nodes[nodeIdx];
        if (node.primCount <= 1)
            return;

        Vector3 extent = node.aabbMax - node.aabbMin;
        int axis = 0;
        if (extent.y > extent.x) axis = 1;
        if (extent.z > extent[axis]) axis = 2;
        float splitPos = node.aabbMin[axis] + extent[axis] * 0.5f;

        int i = node.firstPrim;
        int j = i + node.primCount - 1;
        while (i <= j)
        {
            int chunkIndex = indices[i];
            TileDataGPU chunk = chunks[chunkIndex];

            if (chunk.heightSlice < 0)
            {
                (indices[i], indices[j]) = (indices[j], indices[i]);
                j--;
                continue;
            }

            Bounds chunkBound = new Bounds(chunk.boundsMin, chunk.boundsMax);
            chunkBound.SetMinMax(chunk.boundsMin, chunk.boundsMax);

            Bounds worldBounds = TransformBounds(chunk.localToWorld, chunkBound);

            if (worldBounds.center[axis] < splitPos)
            {
                i++;
            }
            else
            {
                (indices[i], indices[j]) = (indices[j], indices[i]);
                j--;
            }
        }

        int leftCount = i - node.firstPrim;
        if (leftCount == 0 || leftCount == node.primCount) return;

        // create child nodes
        int leftChildIdx = nodes.Count;
        int rightChildIdx = nodes.Count + 1;
        node.leftChild = leftChildIdx;
        node.rightChild = rightChildIdx;

        BVHNode leftChildNode = new BVHNode();
        BVHNode rightChildNode = new BVHNode();

        leftChildNode.firstPrim = node.firstPrim;
        leftChildNode.primCount = leftCount;
        leftChildNode.leftChild = -1;
        leftChildNode.rightChild = -1;


        rightChildNode.firstPrim = i;
        rightChildNode.primCount = node.primCount - leftCount;
        rightChildNode.leftChild = -1;
        rightChildNode.rightChild = -1;

        node.primCount = 0;

        nodes[nodeIdx] = node;
        nodes.Add(leftChildNode);
        nodes.Add(rightChildNode);
        UpdateNodeBounds(leftChildIdx, chunks);
        UpdateNodeBounds(rightChildIdx, chunks);

        Subdivide(leftChildIdx, chunks);
        Subdivide(rightChildIdx, chunks);
    }

    public void BindGlobalData(CommandBuffer commandBuffer)
    {
        commandBuffer.SetGlobalBuffer("_Nodes", bvhNodeBuffer);
        commandBuffer.SetGlobalBuffer("_NodeIndices", bvhIndexBuffer);
        commandBuffer.SetGlobalInt("_NodeCount", nodes.Count);
    }
}
