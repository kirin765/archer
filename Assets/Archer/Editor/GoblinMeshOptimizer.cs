using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Archer.EditorTools
{
    /// <summary>
    /// The imported goblin is ~1.4M triangles per instance, which is far beyond
    /// what a mobile GPU can draw five times per frame. This builds a clustered
    /// low-poly LOD from the same mesh, preserving bindposes and bone weights so
    /// it can be swapped into the existing rig without touching the avatar,
    /// animations or materials.
    /// </summary>
    public static class GoblinMeshOptimizer
    {
        const string SourcePrefab = "Assets/Archer/Resources/Prefabs/Goblin_Visual.prefab";
        const string ReducedMesh = "Assets/Archer/Models/Monsters/Goblin_LowPoly.asset";
        const string ReducedPrefab = "Assets/Archer/Resources/Prefabs/Goblin_Visual_Low.prefab";

        [MenuItem("Archer/Optimize Goblin Mesh")]
        public static string Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            if (prefab == null) return "ERROR: source prefab missing";
            var source = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
            if (source == null || source.sharedMesh == null) return "ERROR: skinned mesh missing";

            var original = source.sharedMesh;
            var reduced = Decimate(original, 140);

            AssetDatabase.DeleteAsset(ReducedMesh);
            AssetDatabase.CreateAsset(reduced, ReducedMesh);
            AssetDatabase.SaveAssets();

            // Clone the prefab so the avatar, bones, Animator, GoblinMotion and
            // material references all stay intact; only the mesh changes.
            var root = Object.Instantiate(prefab);
            try
            {
                var smr = root.GetComponentInChildren<SkinnedMeshRenderer>();
                smr.sharedMesh = reduced;
                PrefabUtility.SaveAsPrefabAsset(root, ReducedPrefab);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();

            int before = original.triangles.Length / 3;
            int after = reduced.triangles.Length / 3;
            string msg = $"goblin {before} -> {after} tris ({after * 100f / Mathf.Max(1, before):0.0}%), verts {original.vertexCount} -> {reduced.vertexCount}";
            Debug.Log("[GoblinMeshOptimizer] " + msg);
            return msg;
        }

        static Mesh Decimate(Mesh source, int resolution)
        {
            var vertices = source.vertices;
            var normals = source.normals;
            var uv = source.uv;
            var weights = source.boneWeights;
            var triangles = source.triangles;
            var bounds = source.bounds;

            float cell = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) / Mathf.Max(8, resolution);
            var lookup = new Dictionary<long, int>(vertices.Length);
            var map = new int[vertices.Length];
            var clusters = new List<Cluster>(vertices.Length / 8);

            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                long x = (long)Mathf.Floor((v.x - bounds.min.x) / cell);
                long y = (long)Mathf.Floor((v.y - bounds.min.y) / cell);
                long z = (long)Mathf.Floor((v.z - bounds.min.z) / cell);
                long key = (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
                if (!lookup.TryGetValue(key, out int index))
                {
                    index = clusters.Count;
                    lookup[key] = index;
                    clusters.Add(new Cluster
                    {
                        Position = Vector3.zero,
                        Normal = Vector3.zero,
                        Uv = i < uv.Length ? uv[i] : Vector2.zero
                    });
                }
                map[i] = index;
                var c = clusters[index];
                c.Position += v;
                if (i < normals.Length) c.Normal += normals[i];
                c.Count++;
                if (i < weights.Length)
                {
                    var w = weights[i];
                    c.AddBone(w.boneIndex0, w.weight0);
                    c.AddBone(w.boneIndex1, w.weight1);
                    c.AddBone(w.boneIndex2, w.weight2);
                    c.AddBone(w.boneIndex3, w.weight3);
                }
            }

            var reducedVerts = new Vector3[clusters.Count];
            var reducedNormals = new Vector3[clusters.Count];
            var reducedUv = new Vector2[clusters.Count];
            var reducedWeights = new BoneWeight[clusters.Count];
            for (int i = 0; i < clusters.Count; i++)
            {
                var c = clusters[i];
                reducedVerts[i] = c.Position / Mathf.Max(1, c.Count);
                reducedNormals[i] = c.Normal.sqrMagnitude > 0.0001f ? c.Normal.normalized : Vector3.up;
                reducedUv[i] = c.Uv;
                reducedWeights[i] = c.ToBoneWeight();
            }

            var reducedTris = new List<int>(triangles.Length / 3);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = map[triangles[i]], b = map[triangles[i + 1]], c = map[triangles[i + 2]];
                if (a == b || b == c || a == c) continue;
                reducedTris.Add(a); reducedTris.Add(b); reducedTris.Add(c);
            }

            var result = new Mesh
            {
                name = source.name + " (low)",
                indexFormat = reducedVerts.Length > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            result.SetVertices(reducedVerts);
            result.SetNormals(reducedNormals);
            result.SetUVs(0, reducedUv);
            result.boneWeights = reducedWeights;
            result.bindposes = source.bindposes;
            result.SetTriangles(reducedTris, 0, true);
            result.RecalculateBounds();
            return result;
        }

        class Cluster
        {
            public Vector3 Position, Normal;
            public Vector2 Uv;
            public int Count;
            readonly Dictionary<int, float> Bones = new();

            public void AddBone(int index, float weight)
            {
                if (weight <= 0f) return;
                Bones.TryGetValue(index, out float sum);
                Bones[index] = sum + weight;
            }

            public BoneWeight ToBoneWeight()
            {
                // Keep the four heaviest influences, renormalized.
                int i0 = -1, i1 = -1, i2 = -1, i3 = -1;
                float w0 = 0, w1 = 0, w2 = 0, w3 = 0;
                foreach (var pair in Bones)
                {
                    float w = pair.Value;
                    if (w > w0) { i3 = i2; w3 = w2; i2 = i1; w2 = w1; i1 = i0; w1 = w0; i0 = pair.Key; w0 = w; }
                    else if (w > w1) { i3 = i2; w3 = w2; i2 = i1; w2 = w1; i1 = pair.Key; w1 = w; }
                    else if (w > w2) { i3 = i2; w3 = w2; i2 = pair.Key; w2 = w; }
                    else if (w > w3) { i3 = pair.Key; w3 = w; }
                }
                float total = w0 + w1 + w2 + w3;
                if (total <= 0f) return new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                return new BoneWeight
                {
                    boneIndex0 = Mathf.Max(0, i0), weight0 = w0 / total,
                    boneIndex1 = Mathf.Max(0, i1), weight1 = w1 / total,
                    boneIndex2 = Mathf.Max(0, i2), weight2 = w2 / total,
                    boneIndex3 = Mathf.Max(0, i3), weight3 = w3 / total
                };
            }
        }
    }
}
