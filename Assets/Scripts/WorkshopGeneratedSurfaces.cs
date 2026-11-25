using System.Collections.Generic;
using UnityEngine;

namespace Monlit
{
    // The generated round owns its native rendering assets, even when a new round starts in the same scene.
    [DisallowMultipleComponent]
    public sealed class WorkshopGeneratedSurfaces : MonoBehaviour
    {
        private readonly List<Object> owned = new List<Object>();

        public T Keep<T>(T asset) where T : Object
        {
            owned.Add(asset);
            return asset;
        }

        private void OnDestroy()
        {
            foreach (var asset in owned)
            {
                if (!asset) continue;
                if (Application.isPlaying) Destroy(asset);
                else DestroyImmediate(asset);
            }
        }
    }
}
