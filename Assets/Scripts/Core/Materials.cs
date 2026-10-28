using UnityEngine;

namespace CsAim.Core
{
    /// <summary>
    /// Runtime material factory. Shader.Find only works in a player build for shaders that are
    /// referenced by an asset or "Always Included": lit materials clone a material asset,
    /// unlit ones use Sprites/Default, which every build includes.
    /// </summary>
    public static class Materials
    {
        static Material _litTemplate;
        static Shader _unlit;

        /// <summary>
        /// Lit materials are copies of Resources/Materials/Lit.mat (Standard shader). Referencing the
        /// shader from a real asset is what gets it into a WebGL build; a shader only found by name
        /// at runtime is stripped and renders magenta.
        /// </summary>
        public static Material Lit(Color c)
        {
            if (_litTemplate == null) _litTemplate = Resources.Load<Material>("Materials/Lit");
            Material m = _litTemplate != null ? new Material(_litTemplate) : new Material(Shader.Find("Standard"));
            m.color = c;
            return m;
        }

        public static Material Unlit(Color c)
        {
            if (_unlit == null) _unlit = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            return new Material(_unlit) { color = c };
        }
    }
}
