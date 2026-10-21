using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Archer.EditorTools
{
    /// <summary>
    /// Builds the shippable goblin.
    ///
    /// The imported model is ~1.4M triangles; five of those on screen is far
    /// beyond a mobile GPU, and the whole 55 MB source asset was being pulled
    /// into the build through the Resources prefab even though the game never
    /// drew it.
    ///
    /// This tool:
    ///   - decimates the source with the quadric simplifier in
    ///     <see cref="GoblinMeshSimplifier"/>, which keeps the UV layout, the
    ///     normals and the bone weights (and adds tangents, so the normal map
    ///     finally reaches the screen);
    ///   - writes three levels of detail and wires them into a LODGroup, so a
    ///     goblin costs what its on-screen size deserves;
    ///   - rebuilds the runtime prefab so it references only the small meshes
    ///     and the shared material, leaving the 55 MB source out of the build.
    /// </summary>
    public static class GoblinMeshOptimizer
    {
        const string RigPrefabPath = "Assets/Archer/Models/Monsters/Goblin_Visual_Rig.prefab";
        const string RuntimePrefabPath = "Assets/Archer/Resources/Prefabs/Goblin_Visual.prefab";
        const string MeshFolder = "Assets/Archer/Models/Monsters/";
        const string MaterialPath = "Assets/Archer/Materials/Goblin_Skin.mat";
        const string TextureFolder = "Assets/Archer/Models/Monsters/Goblin_Raider_Assets/selected.fbm/";
        const string LegacyMesh = "Assets/Archer/Models/Monsters/Goblin_LowPoly.asset";
        const string LegacyPrefab = "Assets/Archer/Resources/Prefabs/Goblin_Visual_Low.prefab";

        struct LodSpec
        {
            public string Name;
            public int Triangles;
        }

        static readonly LodSpec[] Lods =
        {
            new LodSpec { Name = "Goblin_LOD0", Triangles = 30000 },
            new LodSpec { Name = "Goblin_LOD1", Triangles = 11000 },
            new LodSpec { Name = "Goblin_LOD2", Triangles = 3500 },
        };

        [MenuItem("Archer/Optimize Goblin Mesh")]
        public static string Run()
        {
            // The shipped visual now comes from Assets/Goblin_Character. This
            // tool only knows the superseded Mixamo rig and would overwrite the
            // new prefab with the old goblin, so refuse rather than regress it.
            if (System.IO.File.Exists("Assets/Goblin_Character/Model/Goblin.fbx"))
                return "SKIPPED: use 'Archer/Rebuild Goblin Model (Goblin_Character)' for the current goblin.";
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (rigPrefab == null)
                return "ERROR: rig prefab missing. Run 'Archer/Rebuild Goblin Animation Prefab' first.";
            var sourceRenderer = rigPrefab.GetComponentInChildren<SkinnedMeshRenderer>();
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null)
                return "ERROR: rig prefab has no skinned mesh";
            var source = sourceRenderer.sharedMesh;

            var report = new StringBuilder();
            report.AppendLine($"source: {source.vertexCount} verts / {source.triangles.Length / 3} tris / {source.bindposes.Length} bones");

            var meshes = new List<Mesh>();
            foreach (var lod in Lods)
            {
                var mesh = GoblinMeshSimplifier.Simplify(source, GoblinMeshSimplifier.Settings.For(lod.Triangles));
                var path = MeshFolder + lod.Name + ".asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                meshes.Add(mesh);
                report.AppendLine($"{lod.Name}: {mesh.vertexCount} verts / {mesh.triangles.Length / 3} tris " +
                                  $"(target {lod.Triangles}, tangents {mesh.tangents.Length}, bones {mesh.bindposes.Length})");
            }
            AssetDatabase.SaveAssets();

            // Rebuild the runtime prefab from the rig so the avatar, bones,
            // animator, GoblinMotion and the material all stay intact.
            var root = Object.Instantiate(rigPrefab);
            try
            {
                var lod0 = root.GetComponentInChildren<SkinnedMeshRenderer>();
                var bones = lod0.bones;
                var rootBone = lod0.rootBone;
                var material = BuildMaterial(report);
                var parent = lod0.transform.parent;

                lod0.name = "GoblinMesh_LOD0";
                lod0.sharedMesh = meshes[0];
                lod0.sharedMaterial = material;
                Configure(lod0, meshes[0]);

                var renderers = new List<SkinnedMeshRenderer> { lod0 };
                for (int i = 1; i < meshes.Count; i++)
                {
                    var go = new GameObject("GoblinMesh_LOD" + i);
                    go.transform.SetParent(parent, false);
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = meshes[i];
                    smr.sharedMaterial = material;
                    smr.bones = bones;
                    smr.rootBone = rootBone;
                    Configure(smr, meshes[i]);
                    // The enemy picks the level that matches its distance, so
                    // only the closest one is drawn.
                    smr.enabled = false;
                    renderers.Add(smr);
                }

                PrefabUtility.SaveAsPrefabAsset(root, RuntimePrefabPath);
                report.AppendLine($"runtime prefab: {RuntimePrefabPath} with {renderers.Count} LOD renderers");
            }
            finally { Object.DestroyImmediate(root); }

            // The clustered mesh and its prefab are superseded by the LOD set.
            AssetDatabase.DeleteAsset(LegacyMesh);
            AssetDatabase.DeleteAsset(LegacyPrefab);
            AssetDatabase.SaveAssets();
            TuneTextures(report);

            string msg = report.ToString().TrimEnd();
            Debug.Log("[GoblinMeshOptimizer] " + msg);
            return msg;
        }

        static void Configure(SkinnedMeshRenderer smr, Mesh mesh)
        {
            // Two bone influences per vertex: the decimator already pruned the
            // weights, so the skinning cost halves without visible change.
            smr.quality = SkinQuality.Bone2;
            // The camera watches one lane; there is nothing to skin off screen.
            smr.updateWhenOffscreen = false;
            // The imported prefab shipped with stale bounds (0.29 units tall
            // for an 0.86 unit character), which would cull the goblin away.
            smr.localBounds = RendererBounds(smr, mesh);
        }

        /// <summary>
        /// The mesh vertices live in the root bone's space, which is turned a
        /// quarter turn against the renderer. Handing the renderer the mesh
        /// bounds verbatim would cull the goblin on the wrong axis, so the box
        /// is rebuilt in the renderer's own space.
        /// </summary>
        static Bounds RendererBounds(SkinnedMeshRenderer smr, Mesh mesh)
        {
            var matrix = smr.transform.worldToLocalMatrix * smr.rootBone.localToWorldMatrix;
            var b = mesh.bounds;
            var result = new Bounds();
            bool first = true;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                var p = matrix.MultiplyPoint3x4(corner);
                if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                else result.Encapsulate(p);
            }
            // Leave room for the swing of the arms and the bob of the walk.
            result.Expand(result.size.magnitude * 0.08f);
            return result;
        }

        /// <summary>
        /// Builds the runtime material.
        ///
        /// The imported material shipped as a sub-asset of the 55 MB source
        /// model, so the runtime prefab kept the whole model alive in every
        /// build even though only the decimated meshes were ever drawn. The
        /// textures are separate project assets, so a small URP/Lit material
        /// that points at them breaks the link.
        /// </summary>
        static Material BuildMaterial(StringBuilder report)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Archer/Materials"))
                AssetDatabase.CreateFolder("Assets/Archer", "Materials");

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            var copy = new Material(lit) { name = "Goblin_Skin" };

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) continue;
                if (path.Contains("Normal")) copy.SetTexture("_BumpMap", texture);
                else copy.SetTexture("_BaseMap", texture);
            }
            // Skin reads better slightly damp than the imported 0.5 gloss.
            if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", 0.34f);
            if (copy.HasProperty("_Glossiness")) copy.SetFloat("_Glossiness", 0.34f);
            if (copy.HasProperty("_Metallic")) copy.SetFloat("_Metallic", 0f);
            if (copy.HasProperty("_BumpScale")) copy.SetFloat("_BumpScale", 1f);
            if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", Color.white);
            // The decimated meshes carry their own tangents, so the normal map
            // can finally be used.
            copy.EnableKeyword("_NORMALMAP");

            AssetDatabase.DeleteAsset(MaterialPath);
            AssetDatabase.CreateAsset(copy, MaterialPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            report.AppendLine($"material: {MaterialPath} shader={material.shader?.name} " +
                              $"base={material.GetTexture("_BaseMap")?.name} " +
                              $"normal={material.GetTexture("_BumpMap")?.name}");
            return material;
        }

        /// <summary>
        /// The goblin is never more than a few hundred pixels tall, so 2048
        /// textures were pure waste. Mipmaps also fix the shimmer the goblin
        /// showed while walking at distance.
        /// </summary>
        static void TuneTextures(StringBuilder report)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                bool changed = false;
                if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
                if (importer.maxTextureSize > 1024) { importer.maxTextureSize = 1024; changed = true; }
                if (changed)
                {
                    importer.SaveAndReimport();
                    report.AppendLine($"texture: {path} -> mipmaps on, max 1024");
                }
            }
        }
    }
}
