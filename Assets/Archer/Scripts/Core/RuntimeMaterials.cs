using System.Collections.Generic;
using UnityEngine;

namespace Archer
{
    /// <summary>Destroys materials generated for a world or enemy when that scope ends.</summary>
    public class RuntimeMaterials : MonoBehaviour
    {
        readonly HashSet<Material> _owned = new();
        /// <summary>
        /// Takes ownership of every generated material under <paramref name="root"/>.
        /// Materials inside an excluded scope are shared between monsters and
        /// must survive them, so they are left alone.
        /// </summary>
        public void Capture(Transform root, Transform importedVisual = null, Transform sharedGear = null)
        {
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (importedVisual != null && renderer.transform.IsChildOf(importedVisual)) continue;
                if (sharedGear != null && renderer.transform.IsChildOf(sharedGear)) continue;
                foreach(var material in renderer.sharedMaterials)
                    if(material != null) _owned.Add(material);
            }
        }

        /// <summary>Register materials created outside the captured scope (e.g. accents on imported bones).</summary>
        public void CaptureOwned(params Material[] materials)
        {
            foreach (var material in materials)
                if (material != null) _owned.Add(material);
        }

        void OnDestroy()
        {
            foreach(var material in _owned) if(material != null) Destroy(material);
        }
    }
}
