using System.Collections.Generic;
using UnityEngine;

namespace Archer
{
    /// <summary>Destroys materials generated for a world or enemy when that scope ends.</summary>
    public class RuntimeMaterials : MonoBehaviour
    {
        readonly HashSet<Material> _owned = new();
        public void Capture(Transform root, Transform importedVisual = null)
        {
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (importedVisual == null || !renderer.transform.IsChildOf(importedVisual))
                foreach(var material in renderer.sharedMaterials)
                    if(material != null) _owned.Add(material);
        }
        void OnDestroy()
        {
            foreach(var material in _owned) if(material != null) Destroy(material);
        }
    }
}
