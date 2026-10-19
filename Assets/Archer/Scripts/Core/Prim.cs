using UnityEngine;

namespace Archer
{
    /// <summary>
    /// Small helpers for building placeholder geometry and materials at runtime.
    /// Used by the vertical slice so the whole world can be generated from code.
    /// </summary>
    public static class Prim
    {
        static Shader _lit;
        static Shader _unlit;

        public static Shader LitShader
        {
            get
            {
                if (_lit == null)
                    _lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                return _lit;
            }
        }

        public static Shader UnlitShader
        {
            get
            {
                if (_unlit == null)
                    _unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                return _unlit;
            }
        }

        public static Material Material(Color color, float smoothness = 0.1f, float metallic = 0f)
        {
            var m = new Material(LitShader) { name = $"Mat_{ColorUtility.ToHtmlStringRGB(color)}" };
            SetColor(m, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            return m;
        }

        public static Material Unlit(Color color)
        {
            var m = new Material(UnlitShader) { name = $"Unlit_{ColorUtility.ToHtmlStringRGB(color)}" };
            SetColor(m, color);
            return m;
        }

        public static Material Emissive(Color color, float intensity = 1.5f)
        {
            var m = new Material(LitShader) { name = $"Emissive_{ColorUtility.ToHtmlStringRGB(color)}" };
            SetColor(m, color);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            return m;
        }

        public static void SetColor(Material m, Color color)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            m.color = color;
        }

        public static GameObject Create(PrimitiveType type, string name, Transform parent,
            Vector3 localPosition, Vector3 localScale, Material material, bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;

            if (material != null)
            {
                var r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = material;
            }

            if (!keepCollider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) Object.Destroy(c);
            }
            return go;
        }

        public static GameObject Empty(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go;
        }
    }
}
