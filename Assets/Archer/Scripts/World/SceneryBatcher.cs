using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Archer
{
    /// <summary>
    /// Collapses decorative MeshRenderers into one mesh per material per
    /// z-section. Splitting along z keeps enough spatial separation for
    /// frustum culling, unlike one static batch for the whole forest.
    /// Combined meshes are generated at runtime and destroyed with the root.
    /// </summary>
    public static class SceneryBatcher
    {
        struct Key
        {
            public Material Material;
            public int Section;
        }

        /// <summary>Combine every single-material MeshRenderer under root. Returns the owning component.</summary>
        public static GeneratedMeshes Combine(GameObject root, float sectionLength = 45f)
        {
            var groups = new Dictionary<Key, List<CombineInstance>>();
            var retire = new List<GameObject>();
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                var material = renderer.sharedMaterial;
                // Only fold the simple one-material shapes scenery is built from.
                if (mesh == null || material == null) continue;
                if (mesh.subMeshCount != 1 || renderer.sharedMaterials.Length != 1) continue;
                int section = Mathf.FloorToInt(renderer.transform.position.z / sectionLength);
                var key = new Key { Material = material, Section = section };
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<CombineInstance>();
                list.Add(new CombineInstance
                {
                    mesh = mesh,
                    transform = root.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix,
                });
                retire.Add(renderer.gameObject);
            }

            var owner = root.GetComponent<GeneratedMeshes>();
            if (owner == null) owner = root.AddComponent<GeneratedMeshes>();
            foreach (var pair in groups)
            {
                var combined = new Mesh { name = $"Batched {pair.Key.Material.name} z{pair.Key.Section}", indexFormat = IndexFormat.UInt32 };
                combined.CombineMeshes(pair.Value.ToArray(), true, true);
                combined.RecalculateBounds();
                var go = new GameObject(combined.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = combined;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = pair.Key.Material;
                r.shadowCastingMode = ShadowCastingMode.On;
                owner.Track(combined);
            }
            foreach (var go in retire) Object.DestroyImmediate(go);
            // Sweep containers left empty by the retired renderers (e.g. the
            // per-tree parents). Keep anything still holding components.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child.childCount == 0 && child.GetComponents<Component>().Length == 1)
                    Object.DestroyImmediate(child.gameObject);
            }
            return owner;
        }
    }

    /// <summary>Destroys batched meshes when their scenery root is destroyed.</summary>
    public class GeneratedMeshes : MonoBehaviour
    {
        readonly List<Mesh> _owned = new();
        public void Track(Mesh mesh) { if (mesh != null) _owned.Add(mesh); }
        void OnDestroy()
        {
            foreach (var mesh in _owned) if (mesh != null) Destroy(mesh);
            _owned.Clear();
        }
    }
}
