using System;
using System.Collections.Generic;
using UnityEngine;

public static class MeshUtil
{
    public static Mesh CreateMeshFromPoints(Transform transform, Vector3[] vertices)
    {
        Mesh mesh = new Mesh();

        // Transforma os vertices em local position
        Vector3[] localVertices = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            localVertices[i] = transform.InverseTransformPoint(vertices[i]);
        }

        mesh.vertices = localVertices;

        int[] triangles = new int[(vertices.Length - 2) * 3];
        for (int i = 0; i < vertices.Length - 2; i++)
        {
            triangles[i * 3] = 0;
            triangles[(i * 3) + 1] = i + 1;
            triangles[(i * 3) + 2] = i + 2;
        }

        mesh.triangles = triangles;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
    
    public static Mesh CreateConvexMesh(Vector3[] vertices, Transform transform)
    {
        Mesh mesh = new Mesh();

        // Transforma os vertices em local position
        Vector3[] localVertices = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            localVertices[i] = transform.InverseTransformPoint(vertices[i]);
        }

        mesh.vertices = localVertices;

        // Cria a lista de triângulos usando a função ConvexHull
        List<int> triangles = new List<int>();
        int[] hullIndices = ConvexHull(localVertices);
        for (int i = 2; i < hullIndices.Length; i++)
        {
            triangles.Add(hullIndices[0]);
            triangles.Add(hullIndices[i - 1]);
            triangles.Add(hullIndices[i]);
        }

        mesh.triangles = triangles.ToArray();

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    public static Mesh CreateHexagonMesh(Vector3[] vertices, Transform transform)
    {
        Mesh mesh = new Mesh();
        
        // Transforma os vertices em local position
        Vector3[] localVertices = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            localVertices[i] = transform.InverseTransformPoint(vertices[i]);
        }

        SortVerticesClockwise(localVertices);
        Vector3[] hexagonVertices = new Vector3[7]; // array com os 7 vértices do hexágono
        hexagonVertices[0] = GetCentroid(localVertices);
        for (int i = 0; i < 6; i++)
        {
            hexagonVertices[i + 1] = localVertices[i];
        }

        int[] triangles = new int[18]; // array com os índices dos vértices para formar os triângulos
        triangles[0] = 0;
        triangles[1] = 1;
        triangles[2] = 2;
        triangles[3] = 0;
        triangles[4] = 2;
        triangles[5] = 3;
        triangles[6] = 0;
        triangles[7] = 3;
        triangles[8] = 4;
        triangles[9] = 0;
        triangles[10] = 4;
        triangles[11] = 5;
        triangles[12] = 0;
        triangles[13] = 5;
        triangles[14] = 6;
        triangles[15] = 0;
        triangles[16] = 6;
        triangles[17] = 1;

        mesh.vertices = hexagonVertices;
        mesh.triangles = triangles;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    public static Mesh WeldDuplicatedVertices(Mesh mesh, float mergeVertexTolerance)
    {
        // Remova os vertices duplicados e crie uma nova lista de vertices
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Dictionary<Vector3, int> vertexIndexMap = new Dictionary<Vector3, int>();
        List<Vector3> newVertices = new List<Vector3>();
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!vertexIndexMap.ContainsKey(vertices[i]))
            {
                vertexIndexMap.Add(vertices[i], newVertices.Count);
                newVertices.Add(vertices[i]);
            }
        }

        // Mapeia os vertices duplicados para seus equivalentes na nova lista de vertices
        int[] newTriangles = new int[triangles.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            Vector3 vertex = vertices[triangles[i]];
            int newIndex;
            if (vertexIndexMap.TryGetValue(vertex, out newIndex))
            {
                newTriangles[i] = newIndex;
            }
        }

        // Combina vertices que estao muito proximos
        CombineCloseVertices(newVertices, newTriangles, mergeVertexTolerance);

        // Cria um novo Mesh
        Mesh newMesh = new Mesh();
        newMesh.vertices = newVertices.ToArray();
        newMesh.triangles = newTriangles;
        newMesh.RecalculateNormals();
        return newMesh;
    }
    
    private static void CombineCloseVertices(List<Vector3> vertices, int[] triangles, float mergeVertexTolerance)
    {
        for (int i = 0; i < vertices.Count; i++)
        {
            for (int j = i + 1; j < vertices.Count; j++)
            {
                if (Vector3.Distance(vertices[i], vertices[j]) < mergeVertexTolerance)
                {
                    vertices[j] = vertices[i];
                }
            }
        }
    }
    
    private static int[] ConvexHull(Vector3[] points)
    {
        int[] hullIndices;

        // Encontra o ponto mais à esquerda
        int leftmostIndex = 0;
        for (int i = 1; i < points.Length; i++)
        {
            if (points[i].x < points[leftmostIndex].x)
            {
                leftmostIndex = i;
            }
        }

        // Inicia a busca do Convex Hull pelo ponto mais à esquerda
        List<int> hullList = new List<int>();
        int currentVertex = leftmostIndex;
        int initialVertex = currentVertex;
        hullList.Add(currentVertex);

        do
        {
            int nextVertex = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (i == currentVertex)
                {
                    continue;
                }

                if (nextVertex == currentVertex ||
                    IsPointToLeftOfLine(points[currentVertex], points[nextVertex], points[i]))
                {
                    nextVertex = i;
                }
            }

            currentVertex = nextVertex;
            hullList.Add(currentVertex);
        } while (currentVertex != initialVertex);

        hullIndices = hullList.ToArray();
        return hullIndices;
    }

    // Função auxiliar para verificar se um ponto está à esquerda de uma reta
    private static bool IsPointToLeftOfLine(Vector3 lineStart, Vector3 lineEnd, Vector3 point)
    {
        return (lineEnd.x - lineStart.x) * (point.z - lineStart.z) >
               (lineEnd.z - lineStart.z) * (point.x - lineStart.x);
    }
    
    private static void SortVerticesClockwise(Vector3[] vertices)
    {
        // Sort vertices clockwise
        Vector3 centroid = GetCentroid(vertices);
        Array.Sort(vertices,(a, b) => GetAngle(centroid, a).CompareTo(GetAngle(centroid, b)));
    }

    private static Vector3 GetCentroid(Vector3[]vertices)
    {
        Vector3 sum = Vector3.zero;
        foreach (var vertex in vertices)
        {
            sum += vertex;
        }
        return sum / vertices.Length;
    }

    private static float GetAngle(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        float angle = Vector3.Angle(Vector3.right, direction);
        Vector3 cross = Vector3.Cross(Vector3.right, direction);
        if (cross.y < 0) 
        {
            angle = 360 - angle;
        }

        return angle;
    }
}
