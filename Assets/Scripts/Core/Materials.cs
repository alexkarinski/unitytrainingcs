using UnityEngine;

namespace CsAim.Core
{
    /// <summary>
    /// Runtime material factory. Shader.Find only works in a player build for shaders that are
    /// referenced or "Always Included", so we fall back to shaders that always ship:
    /// the default primitive material's shader (Standard) and Sprites/Default.
    /// </summary>
    public static class Materials
    {
        static Shader _lit, _unlit;

        public static Material Lit(Color c)
        {
            if (_lit == null)
            {
                _lit = Shader.Find("Standard");
                if (_lit == null)
                {
                    var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _lit = probe.GetComponent<Renderer>().sharedMaterial.shader;
                    Object.Destroy(probe);
                }
            }
            return new Material(_lit) { color = c };
        }

        public static Material Unlit(Color c)
        {
            if (_unlit == null) _unlit = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            return new Material(_unlit) { color = c };
        }
    }
}
