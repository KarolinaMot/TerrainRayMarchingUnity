using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class ChunkBVH : MonoBehaviour
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
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    MeshToHeightField meshToHeightField;
    int roodNodeID = 0;
    public List<BVHNode> nodes;
    public ComputeBuffer bvhBuffer;

    void Start()
    {
        meshToHeightField = GetComponent<MeshToHeightField>();
        nodes = new List<BVHNode>(meshToHeightField.chunks.Count * 2 -1);
        int stride = Marshal.SizeOf<BVHNode>();
        bvhBuffer = new ComputeBuffer(meshToHeightField.chunks.Count * 2 - 1, stride);

        Build();
    }

    void OnDestroy()
    {
        if (bvhBuffer != null)
        {
            bvhBuffer.Release();
            bvhBuffer = null;
        }
    }

    void Build()
    {
        nodes.Clear();
        nodes = new List<BVHNode>(meshToHeightField.chunks.Count * 2 - 1);

        BVHNode root = new BVHNode();
        root.firstPrim = 0;
        root.primCount = meshToHeightField.chunks.Count;
        root.leftChild = root.rightChild = 0;
        nodes.Add(root);

        UpdateNodeBounds(roodNodeID);
        Subdivide(roodNodeID);

        meshToHeightField.BuildChunkBuffer();
        bvhBuffer.SetData(nodes);

    }

    Bounds TransformBounds(Bounds localBounds, Matrix4x4 localToWorld)
    {
        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;

        Vector3[] corners =
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

        Vector3 worldMin = new Vector3(1e30f, 1e30f, 1e30f);
        Vector3 worldMax = new Vector3(-1e30f, -1e30f, -1e30f);

        for (int i = 0; i < 8; i++)
        {
            Vector3 p = localToWorld.MultiplyPoint3x4(corners[i]);
            worldMin = Vector3.Min(worldMin, p);
            worldMax = Vector3.Max(worldMax, p);
        }

        Bounds result = new Bounds();
        result.SetMinMax(worldMin, worldMax);
        return result;
    }

    void UpdateNodeBounds(int nodeIdx)
    {
        BVHNode node = nodes[nodeIdx];

        node.aabbMin = new Vector3(1e30f, 1e30f, 1e30f);
        node.aabbMax = new Vector3(-1e30f, -1e30f, -1e30f);

        for (int first = node.firstPrim, i = 0; i < node.primCount; i++)
        {
            TerrainChunk chunk = meshToHeightField.chunks[first + i];

            Bounds worldBounds = TransformBounds(
                chunk.bounds,
                chunk.transform.localToWorldMatrix
            );

            node.aabbMin = Vector3.Min(node.aabbMin, worldBounds.min);
            node.aabbMax = Vector3.Max(node.aabbMax, worldBounds.max);
        }

        nodes[nodeIdx] = node;
    }

    void Subdivide(int nodeIdx)
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
            TerrainChunk chunk = meshToHeightField.chunks[i];

            Bounds worldBounds = TransformBounds(
                chunk.bounds,
                chunk.transform.localToWorldMatrix
            );

            if (worldBounds.center[axis] < splitPos)
            {
                i++;
            }
            else
            {
                (meshToHeightField.chunks[i], meshToHeightField.chunks[j]) =
                    (meshToHeightField.chunks[j], meshToHeightField.chunks[i]);

                j--;
            }
        }

        int leftCount = i - node.firstPrim;
        if (leftCount == 0 || leftCount == node.primCount) return;

        // create child nodes
        int leftChildIdx = nodes.Count;
        int rightChildIdx = nodes.Count+1;
        node.leftChild = leftChildIdx;
        node.rightChild = rightChildIdx;

        BVHNode leftChildNode = new BVHNode();
        BVHNode rightChildNode = new BVHNode();

        leftChildNode.firstPrim = node.firstPrim;
        leftChildNode.primCount = leftCount;
        leftChildNode.leftChild = leftChildNode.rightChild = 0;

        rightChildNode.firstPrim = i;
        rightChildNode.primCount = node.primCount - leftCount;
        rightChildNode.leftChild = rightChildNode.rightChild = 0;
        node.primCount = 0;

        nodes[nodeIdx] = node;
        nodes.Add(leftChildNode);
        nodes.Add(rightChildNode);
        UpdateNodeBounds(leftChildIdx);
        UpdateNodeBounds(rightChildIdx);

        Subdivide(leftChildIdx);
        Subdivide(rightChildIdx);
    }

    // Update is called once per frame
    void Update()
    {
        Build();
    }
}
