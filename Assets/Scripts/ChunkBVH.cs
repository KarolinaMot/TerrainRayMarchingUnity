using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class ChunkBVH : MonoBehaviour
{
    public struct BVHNode
    {
        public Vector3 aabbMin, aabbMax;
        public int leftChild, rightChild;
        public int firstPrim, primCount;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    MeshToHeightField meshToHeightField;
    int roodNodeID = 0;
    List<BVHNode> nodes;
    int usedNodes = 0;
    int rootNodeIdx = 0, nodesUsed = 1;



    void Start()
    {
        meshToHeightField = GetComponent<MeshToHeightField>();
        nodes = new List<BVHNode>(meshToHeightField.chunks.Count*2 -1);

        BVHNode root = new BVHNode();
        root.firstPrim = 0;
        root.primCount = meshToHeightField.chunks.Count;
        root.leftChild = root.rightChild = 0;
        nodes[rootNodeIdx] = root;

        UpdateNodeBounds(roodNodeID);
        Subdivide(roodNodeID);
    }

    void UpdateNodeBounds(int nodeIdx)
    {
        BVHNode node = nodes[nodeIdx];
        node.aabbMin = new Vector3(1e30f, 1e30f, 1e30f);
        node.aabbMax = new Vector3(-1e30f, -1e30f, -1e30f);

        for (int first = node.firstPrim, i = 0; i < node.primCount; i++)
        {
            TerrainChunk chunk = meshToHeightField.chunks[first + i];
            node.aabbMin = Vector3.Min(node.aabbMin, chunk.bounds.min);
            node.aabbMax = Vector3.Max(node.aabbMax, chunk.bounds.max);
            node.aabbMin.y = Mathf.Min(node.aabbMin.y, chunk.minHeight);
            node.aabbMax.y = Mathf.Max(node.aabbMax.y, chunk.maxHeight);
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

            if (chunk.bounds.center[axis] < splitPos)
                i++;
            else
            {
                (meshToHeightField.chunks[i], meshToHeightField.chunks[j]) = (meshToHeightField.chunks[j], meshToHeightField.chunks[i]);
                j--;
            }
        }

        int leftCount = i - node.firstPrim;
        if (leftCount == 0 || leftCount == node.primCount) return;

        // create child nodes
        int leftChildIdx = usedNodes++;
        int rightChildIdx = usedNodes++;
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
        nodes[leftChildIdx] = leftChildNode;
        nodes[rightChildIdx] = rightChildNode;
        UpdateNodeBounds(leftChildIdx);
        UpdateNodeBounds(rightChildIdx);

        Subdivide(leftChildIdx);
        Subdivide(rightChildIdx);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
