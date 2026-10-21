using System.Collections.Generic;
using UnityEngine;

namespace Archer
{
    /// <summary>
    /// Shared materials and merged kit geometry for the goblin roles.
    ///
    /// The kit used to build a fresh material set for every monster, so a
    /// raid with three runners churned through a dozen runtime materials.
    /// One material per look keeps the state changes down and lets the
    /// batcher group the pieces that share it.
    /// </summary>
    public static class GoblinGear
    {
        static Material _red, _dark, _leather, _metal, _wood, _iron;

        /// <summary>Scout kit cloth: bandana, bandolier, scarf.</summary>
        public static Material Red => _red != null ? _red
            : _red = Prim.Material(new Color(0.78f, 0.20f, 0.16f), 0.25f);
        /// <summary>Dark hood cloth.</summary>
        public static Material Dark => _dark != null ? _dark
            : _dark = Prim.Material(new Color(0.14f, 0.13f, 0.15f), 0.15f);
        /// <summary>Worn leather for straps and pouches.</summary>
        public static Material Leather => _leather != null ? _leather
            : _leather = Prim.Material(new Color(0.27f, 0.18f, 0.12f), 0.1f);
        /// <summary>Blade steel.</summary>
        public static Material Metal => _metal != null ? _metal
            : _metal = Prim.Material(new Color(0.35f, 0.39f, 0.41f), 0.3f, 0.45f);
        /// <summary>Shield board wood.</summary>
        public static Material Wood => _wood != null ? _wood
            : _wood = Prim.Material(new Color(0.36f, 0.26f, 0.16f), 0.2f);
        /// <summary>Shield rim and boss.</summary>
        public static Material Iron => _iron != null ? _iron
            : _iron = Prim.Material(new Color(0.35f, 0.39f, 0.41f), 0.3f, 0.45f);

        /// <summary>
        /// Bakes a group of primitives that share a material and a parent bone
        /// into a single mesh, so a piece of kit costs one draw call instead of
        /// one per primitive. The source objects are removed.
        /// </summary>
        public static GameObject Merge(string name, Transform parent, Material material,
            IReadOnlyList<GameObject> parts)
        {
            var instances = new List<CombineInstance>(parts.Count);
            foreach (var part in parts)
            {
                if (part == null) continue;
                var filter = part.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var renderer = part.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = false;   // hide before it is destroyed
                instances.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    transform = parent.worldToLocalMatrix * part.transform.localToWorldMatrix
                });
            }
            if (instances.Count == 0) return null;

            var combined = new Mesh { name = name };
            combined.CombineMeshes(instances.ToArray(), true, true);
            combined.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = combined;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
