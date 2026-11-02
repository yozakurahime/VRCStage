using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarLab
{
    public static class ObjLoader
    {
        public static GameObject Load(string path, Material template)
        {
            if (new FileInfo(path).Length > 128 * 1024 * 1024) throw new InvalidDataException("OBJ maior que 128 MB.");
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>();
            var vertices = new List<Vector3>(); var outputNormals = new List<Vector3>(); var outputUvs = new List<Vector2>(); var triangles = new List<int>();
            bool hasNormals = true;
            foreach (string line in File.ReadLines(path))
            {
                var s = line.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries); if (s.Length < 2 || s[0].StartsWith("#")) continue;
                if (s[0] == "v" && s.Length >= 4) positions.Add(new Vector3(F(s[1]), F(s[2]), -F(s[3])));
                else if (s[0] == "vn" && s.Length >= 4) normals.Add(new Vector3(F(s[1]), F(s[2]), -F(s[3])));
                else if (s[0] == "vt" && s.Length >= 3) uvs.Add(new Vector2(F(s[1]), F(s[2])));
                else if (s[0] == "f" && s.Length >= 4)
                {
                    if (vertices.Count + s.Length > 3000000) throw new InvalidDataException("OBJ excede o limite de 3 milhões de vértices triangulados.");
                    int start = vertices.Count;
                    for (int i = 1; i < s.Length; i++)
                    {
                        var p = s[i].Split('/'); vertices.Add(positions[Index(p[0], positions.Count)]);
                        outputUvs.Add(p.Length > 1 && p[1].Length > 0 ? uvs[Index(p[1], uvs.Count)] : Vector2.zero);
                        if (p.Length > 2 && p[2].Length > 0) outputNormals.Add(normals[Index(p[2], normals.Count)]);
                        else { outputNormals.Add(Vector3.up); hasNormals = false; }
                    }
                    for (int i = 1; i < s.Length - 2; i++) { triangles.Add(start); triangles.Add(start + i + 1); triangles.Add(start + i); }
                }
            }
            if (vertices.Count == 0) throw new InvalidDataException("OBJ sem faces.");
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path), indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, outputUvs);
            if (hasNormals) mesh.SetNormals(outputNormals); else mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(mesh.name); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
            go.AddComponent<MeshCollider>().sharedMesh = mesh; return go;
        }
        static float F(string s)
        {
            float v = float.Parse(s, CultureInfo.InvariantCulture);
            if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidDataException("Coordenada OBJ inválida."); return v;
        }
        static int Index(string s, int count)
        {
            int i = int.Parse(s, CultureInfo.InvariantCulture); int result = i > 0 ? i - 1 : count + i;
            if (result < 0 || result >= count) throw new InvalidDataException("Índice OBJ inválido."); return result;
        }
    }
}
