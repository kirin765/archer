using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Archer.EditorTools
{
    public static class GoblinAnimationSetup
    {
        const string WalkPath = "Assets/Archer/Animations/Mixamo/Orc Walk.fbx";
        const string AttackPath = "Assets/Archer/Animations/Mixamo/Sword And Shield Slash.fbx";
        const string AvatarPath = "Assets/Archer/Animations/Goblin_Humanoid.asset";
        // The authored rig stays outside Resources: only the decimated LOD
        // prefab is allowed into a build, otherwise the 55 MB source model
        // rides along in every APK.
        const string RigPath = "Assets/Archer/Models/Monsters/Goblin_Visual_Rig.prefab";
        [MenuItem("Archer/Rebuild Goblin Animation Prefab")]
        public static void Build()
        {
            ImportMotion(WalkPath, "Goblin_OrcWalk", true);
            ImportMotion(AttackPath, "Goblin_SwordAndShieldSlash", false);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Archer/Models/Monsters/Goblin_Raider_rig_target.prefab");
            var root = new GameObject("Goblin_Visual");
            try
            {
                var rig = UnityEngine.Object.Instantiate(source, root.transform);
                rig.name = "Rig";
                var bones = rig.GetComponentsInChildren<Transform>();
                string[] human = { "Hips", "Spine", "Chest", "Neck", "Head", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes" };
                string[] names = { "Hip", "Spine01", "Spine02", "NeckTwist01", "Head", "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand", "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand", "L_Thigh", "L_Calf", "L_Foot", "L_ToeBase", "R_Thigh", "R_Calf", "R_Foot", "R_ToeBase" };
                var description = new HumanDescription
                {
                    human = human.Select((name, i) => new HumanBone { humanName = name, boneName = names[i], limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                    skeleton = bones.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false
                };
                foreach (var name in names)
                    if (!bones.Any(t => t.name == name)) throw new InvalidOperationException("Missing goblin bone: " + name);
                var avatar = AvatarBuilder.BuildHumanAvatar(rig, description);
                if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Goblin Humanoid avatar is invalid.");
                avatar.name = "Goblin_Humanoid";
                var existing = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
                if (existing == null) AssetDatabase.CreateAsset(avatar, AvatarPath);
                else { EditorUtility.CopySerialized(avatar, existing); UnityEngine.Object.DestroyImmediate(avatar); avatar = existing; }
                var animator = rig.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                var motion = rig.AddComponent<GoblinMotion>();
                motion.Walk = AssetDatabase.LoadAllAssetsAtPath(WalkPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                motion.Attack = AssetDatabase.LoadAllAssetsAtPath(AttackPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                motion.ImpactTime = 0.4f; // Slash reaches the target at 0.6s in the 1.5s source.
                if (motion.Walk == null || motion.Walk.length <= 0 || motion.Attack == null || motion.Attack.length <= 0)
                    throw new InvalidOperationException("Missing goblin motions.");
                PrefabUtility.SaveAsPrefabAsset(root, RigPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Goblin humanoid avatar and animated rig prefab ready. " +
                          "Run 'Archer/Optimize Goblin Mesh' to build the shippable LOD prefab.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ImportMotion(string path, string name, bool loop)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Missing animation FBX: " + path + "");
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            var clips = importer.clipAnimations;
            if (clips.Length == 0) clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = name;
                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.lockRootRotation = true;
                clip.lockRootPositionXZ = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionXZ = true;
                clip.keepOriginalPositionY = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }
}
