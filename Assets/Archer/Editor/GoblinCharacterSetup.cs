using UnityEditor;
using UnityEngine;

namespace Archer.EditorTools
{
    /// <summary>
    /// Builds the shippable goblin visual from the authored character in
    /// <c>Assets/Goblin_Character</c>.
    ///
    /// The source is a low-poly character (≈4.5k triangles across ten skinned
    /// parts) with its own idle/walk/attack clips. The game drives the pose
    /// procedurally through <see cref="GoblinMotion"/>, so the imported
    /// Animator controllers are stripped and replaced with a single motion
    /// component on the rig.
    ///
    /// The prefab is normalised to one unit of height with the origin at hip
    /// height, matching the contract <see cref="Monster"/> expects when it
    /// scales and places the visual. The model faces -Z in the source, so the
    /// rig is turned to face +Z (the direction the monster walks).
    /// </summary>
    public static class GoblinCharacterSetup
    {
        const string SourceModel = "Assets/Goblin_Character/Model/Goblin.fbx";
        const string SourceSword = "Assets/Goblin_Character/Prefab/SWORD (1).prefab";
        const string SourceTexture = "Assets/Goblin_Character/Textures/COLORS.png";
        const string MaterialPath = "Assets/Archer/Materials/Goblin_Character.mat";
        const string RuntimePrefab = "Assets/Archer/Resources/Prefabs/Goblin_Visual.prefab";
        // Monster places the visual root at 0.95 * scale and scales it by 1.75
        // for a raider, so a one-unit-tall prefab with its origin at 0.543
        // above the feet lands exactly on the ground.
        const float HipHeight01 = 0.543f;

        [MenuItem("Archer/Rebuild Goblin Model (Goblin_Character)")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModel);
            if (source == null)
            {
                Debug.LogError("[GoblinCharacterSetup] Missing source model: " + SourceModel);
                return;
            }

            var root = new GameObject("Goblin_Visual");
            try
            {
                var model = Object.Instantiate(source);
                model.name = "Goblin";
                model.transform.SetParent(root.transform, false);

                // The store prefab ships a controller that cycles idle/walk/
                // attack/die on a timer; the game owns that state instead.
                foreach (var imported in model.GetComponentsInChildren<Animator>(true))
                    Object.DestroyImmediate(imported);

                var clips = LoadClips(model);
                var animator = model.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var motion = model.AddComponent<GoblinMotion>();
                motion.Idle = clips.Idle;
                motion.Walk = clips.Walk;
                motion.Attack = clips.Attack;
                motion.ImpactTime = 0.5f;
                motion.WalkRate = 1f;

                var material = BuildMaterial();
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }

                AttachSword(model, material);
                Normalise(model);

                PrefabUtility.SaveAsPrefabAsset(root, RuntimePrefab);
                AssetDatabase.SaveAssets();
                Debug.Log("[GoblinCharacterSetup] Goblin visual rebuilt at " + RuntimePrefab +
                          $" (idle {ClipLength(motion.Idle)}s, walk {ClipLength(motion.Walk)}s, attack {ClipLength(motion.Attack)}s)");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static (AnimationClip Idle, AnimationClip Walk, AnimationClip Attack) LoadClips(GameObject model)
        {
            AnimationClip idle = null, walk = null, attack = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(SourceModel))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
                if (clip.name.Contains("Idle")) idle = clip;
                else if (clip.name.Contains("WALK") || clip.name.Contains("Walk")) walk = clip;
                else if (clip.name.Contains("Attack")) attack = clip;
            }
            if (idle == null || walk == null || attack == null)
                throw new System.InvalidOperationException("Goblin source clips missing (idle/walk/attack).");
            return (idle, walk, attack);
        }

        static string ClipLength(AnimationClip clip) => clip != null ? clip.length.ToString("0.00") : "0";

        /// <summary>
        /// Puts the authored sword in the right hand. The sword mesh is authored
        /// at 1/100 scale on purpose: the rig's bones carry a 100x import scale,
        /// so a plain child of the hand bone comes out life-sized. Its material
        /// shares the goblin's COLORS atlas, so one material covers both.
        /// </summary>
        static void AttachSword(GameObject model, Material material)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceSword);
            if (source == null)
            {
                Debug.LogWarning("[GoblinCharacterSetup] Sword prefab missing: " + SourceSword);
                return;
            }
            var hand = FindBone(model.transform, "DEF-hand.R")
                ?? FindBone(model.transform, "hand.R")
                ?? FindBone(model.transform, "R_Hand");
            if (hand == null)
            {
                Debug.LogWarning("[GoblinCharacterSetup] Right hand bone not found; sword not attached.");
                return;
            }

            var sword = Object.Instantiate(source, hand);
            sword.name = "Sword";
            sword.transform.localPosition = Vector3.zero;
            sword.transform.localRotation = Quaternion.identity;
            sword.transform.localScale = Vector3.one;
            foreach (var renderer in sword.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }

        static void Normalise(GameObject model)
        {
            var bounds = CombinedBounds(model);
            float height = Mathf.Max(0.01f, bounds.size.y);
            float yaw = 180f; // source faces -Z; the monster walks toward +Z
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 rotatedCenter = rotation * bounds.center;
            float scale = 1f / height;

            model.transform.localRotation = rotation;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = new Vector3(
                rotatedCenter.x * scale,
                -bounds.min.y * scale - HipHeight01,
                rotatedCenter.z * scale);
        }

        static Bounds CombinedBounds(GameObject model)
        {
            bool first = true;
            Bounds bounds = default;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        static Material BuildMaterial()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Archer/Materials"))
                AssetDatabase.CreateFolder("Assets/Archer", "Materials");

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var material = new Material(shader) { name = "Goblin_Character" };
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceTexture);
            if (texture != null)
            {
                // 2k is far more than a goblin covers on a phone.
                var importer = (TextureImporter)AssetImporter.GetAtPath(SourceTexture);
                if (importer != null && (importer.maxTextureSize > 1024 || !importer.mipmapEnabled))
                {
                    importer.maxTextureSize = 1024;
                    importer.mipmapEnabled = true;
                    importer.SaveAndReimport();
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceTexture);
                }
                material.SetTexture("_BaseMap", texture);
                material.SetTexture("_MainTex", texture);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.3f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.3f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            material.enableInstancing = true;

            AssetDatabase.DeleteAsset(MaterialPath);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        }
    }
}
