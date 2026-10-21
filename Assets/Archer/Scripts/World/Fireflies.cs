using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Archer
{
    /// <summary>
    /// Sparse fireflies around the forest edges. One small mesh and one
    /// additive material; drifting and twinkling run in the vertex shader,
    /// so there are no per-particle objects or lights. Anchors stay off the
    /// shooting lane so they never sit on top of enemy silhouettes.
    /// </summary>
    public class Fireflies : MonoBehaviour
    {
        Mesh _mesh;
        Material _material;
        float _appliedNight = -1f;

        public static Fireflies Create(Transform parent, int count = 34)
        {
            var go = new GameObject("Fireflies", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            var fireflies = go.AddComponent<Fireflies>();
            fireflies.Build(count);
            return fireflies;
        }

        void Build(int count)
        {
            var random = new System.Random(97);
            float Next(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
            var vertices = new Vector3[count * 4];
            var corners = new Vector2[count * 4];
            var extras = new List<Vector3>(count * 4);
            var triangles = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                float side = (i & 1) == 0 ? -1f : 1f;
                var anchor = new Vector3(side * Next(6.8f, 11.5f), Next(0.5f, 2.4f), Next(5f, 78f));
                float phase = Next(0f, 16f);
                float size = Next(0.09f, 0.18f);
                float hue = Next(0f, 1f);
                int v = i * 4;
                for (int c = 0; c < 4; c++)
                {
                    vertices[v + c] = anchor;
                    corners[v + c] = new Vector2((c & 1) == 0 ? -0.5f : 0.5f, (c & 2) == 0 ? -0.5f : 0.5f);
                    extras.Add(new Vector3(phase, size, hue));
                }
                int t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            _mesh = new Mesh { name = "Fireflies" };
            _mesh.vertices = vertices;
            _mesh.uv = corners;
            _mesh.SetUVs(1, extras);
            _mesh.triangles = triangles;
            _mesh.bounds = new Bounds(new Vector3(0f, 1.5f, 42f), new Vector3(30f, 8f, 85f));
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            var shader = Resources.Load<Shader>("LastRangerFireflies");
            if (shader == null) { enabled = false; return; }
            _material = new Material(shader) { name = "Fireflies" };
            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }

        void Update()
        {
            if (_material == null) return;
            float night = TimeOfDay.Instance != null ? Mathf.InverseLerp(0.65f, 0.79f, TimeOfDay.Instance.Time01) : 0f;
            if (Mathf.Abs(night - _appliedNight) > 0.005f)
            {
                _appliedNight = night;
                _material.SetFloat("_Night", night);
            }
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }
    }
}
