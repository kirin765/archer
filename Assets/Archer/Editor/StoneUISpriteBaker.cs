using System.IO;
using UnityEditor;
using UnityEngine;

namespace Archer.Editor
{
    /// <summary>
    /// Bakes the StylizedStoneUI slabs into 9-sliced uGUI sprites. The pack is
    /// authored for the Built-in pipeline, so the bake uses temporary URP/Lit
    /// materials instead of the imported Standard ones.
    /// </summary>
    public static class StoneUISpriteBaker
    {
        const string OutputFolder = "Assets/Archer/Resources/UI/Stone";
        const string PlayPrefab = "Assets/StylizedStoneUI_Free/Prefabs/Button_Play.prefab";
        const string OkPrefab = "Assets/StylizedStoneUI_Free/Prefabs/Button_Ok.prefab";

        [MenuItem("Archer/Bake Stone UI Sprites")]
        public static void Bake()
        {
            Directory.CreateDirectory(OutputFolder);
            BakeOne("panel", PlayPrefab, "play_board", 1024,
                new Color(0.50f, 0.47f, 0.42f), new Vector4(64, 64, 64, 64));
            BakeOne("button", OkPrefab, "ok_board", 512,
                new Color(0.70f, 0.68f, 0.64f), new Vector4(36, 12, 36, 12));
            BakeOne("button_primary", PlayPrefab, "play_board", 512,
                new Color(0.98f, 0.86f, 0.66f), new Vector4(36, 12, 36, 12));
            AssetDatabase.Refresh();
            Debug.Log("[StoneUISpriteBaker] Baked stone panel and button sprites into " + OutputFolder);
        }

        static void BakeOne(string name, string prefabPath, string boardName, int size, Color color, Vector4 border)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new InvalidDataException("Missing prefab: " + prefabPath);

            var preview = new PreviewRenderUtility();
            Material material = null;
            try
            {
                var instance = preview.InstantiatePrefabInScene(prefab);
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                Transform board = null;
                foreach (var t in instance.GetComponentsInChildren<Transform>())
                    if (t.name == boardName) { board = t; break; }
                if (board == null) throw new InvalidDataException("Missing board: " + boardName);

                foreach (var r in instance.GetComponentsInChildren<Renderer>())
                    r.enabled = r.transform == board;

                var target = board.GetComponent<Renderer>();
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                material = new Material(shader) { name = "StoneUI bake " + name };
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", 0.32f);
                material.SetFloat("_Metallic", 0f);
                target.sharedMaterial = material;

                var b = target.bounds;
                var cam = preview.camera;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.allowHDR = false;
                cam.allowMSAA = true;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 200f;
                cam.transform.position = b.center + Vector3.back * (b.extents.magnitude * 2f + 1f);
                cam.transform.rotation = Quaternion.identity;
                cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.x) * 1.06f;

                SetLight(preview.lights[0], 1.15f, Color.white, Quaternion.Euler(18f, -22f, 0f));
                SetLight(preview.lights[1], 0.45f, new Color(0.88f, 0.88f, 0.88f), Quaternion.Euler(-12f, 156f, 0f));

                preview.BeginPreview(new Rect(0f, 0f, size, size), GUIStyle.none);
                cam.Render();
                var rt = preview.EndPreview() as RenderTexture;
                if (rt == null) throw new InvalidDataException("Preview render failed for " + name);

                var path = OutputFolder + "/" + name + ".png";
                WriteCropped(rt, path);
                ConfigureImporter(path, border);
            }
            finally
            {
                preview.Cleanup();
                if (material != null) Object.DestroyImmediate(material);
            }
        }

        static void SetLight(Light light, float intensity, Color color, Quaternion rotation)
        {
            if (light == null) return;
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.transform.rotation = rotation;
            light.shadows = LightShadows.None;
        }

        static void WriteCropped(RenderTexture rt, string path)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var full = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            full.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            full.Apply();
            RenderTexture.active = previous;

            int minX = rt.width, minY = rt.height, maxX = -1, maxY = -1;
            var pixels = full.GetPixels32();
            for (int y = 0; y < rt.height; y++)
                for (int x = 0; x < rt.width; x++)
                {
                    if (pixels[y * rt.width + x].a < 12) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }

            if (maxX < minX || maxY < minY) throw new InvalidDataException("Empty bake: " + path);

            int w = maxX - minX + 1, h = maxY - minY + 1;
            var cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
            cropped.SetPixels(full.GetPixels(minX, minY, w, h));
            cropped.Apply();
            File.WriteAllBytes(path, cropped.EncodeToPNG());
            Object.DestroyImmediate(cropped);
            Object.DestroyImmediate(full);
        }

        static void ConfigureImporter(string path, Vector4 border)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
    }
}
