using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CsAim.Core;
using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using UnityEngine;

namespace CsAim.Viewmodel
{
    /// <summary>
    /// Loads a player-supplied glTF weapon model and fits it onto the procedural viewmodel:
    /// the longest axis becomes the barrel (+Z), it is scaled to the procedural gun's length and
    /// centred on it, then the player's manual corrections are applied. The procedural gun's
    /// meshes are hidden afterwards; hands, muzzle and shell anchors keep working.
    /// </summary>
    public static class CustomModelLoader
    {
        public static async Task<GameObject> LoadAsync(byte[] glb, string gltfPath, Transform parent)
        {
            var import = new GltfImport(materialGenerator: new LitMaterialGenerator());
            bool ok = glb != null
                ? await import.Load(glb)
                : await import.Load(new Uri(gltfPath, UriKind.Absolute));
            if (!ok) return null;

            var root = new GameObject("CustomModel");
            root.transform.SetParent(parent, false);
            var content = new GameObject("Content").transform;
            content.SetParent(root.transform, false);
            if (!await import.InstantiateMainSceneAsync(content))
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            ViewmodelBuilder.SetLayerRecursive(root);
            return root;
        }

        /// <summary>
        /// Positions <paramref name="model"/> (as returned by <see cref="LoadAsync"/>) to match the
        /// procedural gun's bounds in weapon space, then applies <paramref name="fit"/>.
        /// </summary>
        public static void Fit(GameObject model, Transform weapon, Bounds target, ModelFit fit)
        {
            var root = model.transform;
            var content = root.GetChild(0);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            content.localPosition = Vector3.zero;
            content.localScale = Vector3.one;
            content.localRotation = Quaternion.identity;

            // 1. Longest axis -> +Z (barrel), then the player's extra rotation.
            Bounds raw = BoundsIn(content, root);
            Vector3 e = raw.size;
            Quaternion align = e.x >= e.y && e.x >= e.z ? Quaternion.Euler(0f, -90f, 0f)
                             : e.y >= e.x && e.y >= e.z ? Quaternion.Euler(90f, 0f, 0f)
                             : Quaternion.identity;
            content.localRotation = Quaternion.Euler(fit.euler) * align;

            // 2. Scale to the procedural gun's length.
            Bounds aligned = BoundsIn(content, root);
            float length = Mathf.Max(0.0001f, aligned.size.z);
            float s = target.size.z / length * (fit.scale > 0f ? fit.scale : 1f);
            content.localScale = Vector3.one * s;

            // 3. Centre on the procedural gun, plus the manual offset.
            Bounds scaled = BoundsIn(content, root);
            content.localPosition = target.center - scaled.center + fit.offset;
        }

        /// <summary>Bounds of all renderers under <paramref name="t"/>, expressed in <paramref name="space"/>.</summary>
        public static Bounds BoundsIn(Transform t, Transform space, Func<Renderer, bool> include = null)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (include != null && !include(r)) continue;
                Bounds lb;
                Matrix4x4 m;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    lb = smr.localBounds;
                    m = space.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    lb = mf.sharedMesh.bounds;
                    m = space.worldToLocalMatrix * r.transform.localToWorldMatrix;
                }
                // Transform the 8 corners so rotation is accounted for.
                Vector3 c = lb.center, x = lb.extents;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -x.x : x.x, (i & 2) == 0 ? -x.y : x.y, (i & 4) == 0 ? -x.z : x.z));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }
    }

    /// <summary>
    /// glTF materials on top of our Standard material asset (which every build contains), so the
    /// model renders correctly in WebGL without shipping glTFast's own shaders.
    /// </summary>
    sealed class LitMaterialGenerator : IMaterialGenerator
    {
        ICodeLogger _logger;
        Material _default;

        public Material GetDefaultMaterial(bool pointsSupport = false) =>
            _default != null ? _default : (_default = Materials.Lit(new Color(0.6f, 0.6f, 0.6f)));

        public Material GenerateMaterial(GLTFast.Schema.MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false)
        {
            var pbr = gltfMaterial.PbrMetallicRoughness;
            Color color = pbr != null ? pbr.BaseColor : Color.white;
            var m = Materials.Lit(color);
            m.name = gltfMaterial.name;

            var info = pbr?.BaseColorTexture;
            if (info != null && info.index >= 0 && info.index < gltf.TextureCount)
            {
                var tex = gltf.GetTexture(info.index);
                if (tex != null) m.mainTexture = tex;
            }
            if (pbr != null)
            {
                // Rough approximation of the metallic/roughness model in Standard's terms.
                m.SetFloat("_Metallic", Mathf.Clamp01(pbr.metallicFactor) * 0.6f);
                m.SetFloat("_Glossiness", 1f - Mathf.Clamp01(pbr.roughnessFactor) * 0.8f);
            }
            return m;
        }

        public void SetLogger(ICodeLogger logger) => _logger = logger;
    }
}
