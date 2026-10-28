using CsAim.Core;
using UnityEngine;

namespace CsAim.UI
{
    /// <summary>
    /// CS2 crosshair from the same convars (cl_crosshairsize, gap, thickness, style, color, dot,
    /// outline, alpha, T-shape, recoil). Pixel sizes are an approximation of the game's at 1080p,
    /// scaled to the window height; colours, styles and toggles map 1:1.
    /// </summary>
    public sealed class Crosshair
    {
        public int style = 4;              // 0/2/3 dynamic, 1/4/5 static
        public float size = 2f;
        public float gap = -1f;
        public float thickness = 0.5f;
        public bool dot;
        public bool tShape;
        public bool outline = true;
        public float outlineThickness = 1f;
        public int colorPreset = 1;        // 0 red, 1 green, 2 yellow, 3 blue, 4 cyan, 5 custom
        public Color32 custom = new Color32(50, 250, 50, 255);
        public bool useAlpha = true;
        public int alpha = 255;
        public bool recoil;                // cl_crosshair_recoil
        /// <summary>Trainer extra: open the gap by the real inaccuracy cone even for static styles.</summary>
        public bool showCone;

        public bool Dynamic => style == 0 || style == 2 || style == 3;

        public void Apply(Cs2Config c)
        {
            style = Mathf.RoundToInt(c.GetFloat("cl_crosshairstyle", style));
            size = c.GetFloat("cl_crosshairsize", size);
            gap = c.GetFloat("cl_crosshairgap", gap);
            thickness = c.GetFloat("cl_crosshairthickness", thickness);
            dot = c.GetBool("cl_crosshairdot", dot);
            tShape = c.GetBool("cl_crosshair_t", tShape);
            outline = c.GetBool("cl_crosshair_drawoutline", outline);
            outlineThickness = c.GetFloat("cl_crosshair_outlinethickness", outlineThickness);
            colorPreset = Mathf.RoundToInt(c.GetFloat("cl_crosshaircolor", colorPreset));
            custom = new Color32(
                (byte)Mathf.Clamp(c.GetFloat("cl_crosshaircolor_r", custom.r), 0, 255),
                (byte)Mathf.Clamp(c.GetFloat("cl_crosshaircolor_g", custom.g), 0, 255),
                (byte)Mathf.Clamp(c.GetFloat("cl_crosshaircolor_b", custom.b), 0, 255), 255);
            useAlpha = c.GetBool("cl_crosshairusealpha", useAlpha);
            alpha = Mathf.RoundToInt(c.GetFloat("cl_crosshairalpha", alpha));
            recoil = c.GetBool("cl_crosshair_recoil", recoil);
        }

        public Color Color
        {
            get
            {
                Color32 c;
                switch (colorPreset)
                {
                    case 0: c = new Color32(250, 50, 50, 255); break;
                    case 2: c = new Color32(250, 250, 50, 255); break;
                    case 3: c = new Color32(50, 50, 250, 255); break;
                    case 4: c = new Color32(50, 250, 250, 255); break;
                    case 5: c = custom; break;
                    default: c = new Color32(50, 250, 50, 255); break;
                }
                c.a = (byte)(useAlpha ? Mathf.Clamp(alpha, 0, 255) : 255);
                return c;
            }
        }

        /// <param name="center">Screen position (GUI coordinates).</param>
        /// <param name="conePixels">Radius of the current inaccuracy+spread cone on screen.</param>
        public void Draw(Vector2 center, float conePixels)
        {
            float k = Screen.height / 1080f;
            float len = Mathf.Max(0f, size * 2f * k);
            float th = Mathf.Max(1f, Mathf.Round(thickness * 2f * k));
            float g = Mathf.Max(0f, (gap + 4f) * k);
            if (Dynamic || showCone) g += conePixels;

            Color col = Color;
            float o = outline ? Mathf.Max(1f, outlineThickness * k) : 0f;
            var black = new Color(0f, 0f, 0f, col.a);

            void Bar(float x, float y, float w, float h)
            {
                if (w <= 0f || h <= 0f) return;
                if (o > 0f) Fill(new Rect(x - o, y - o, w + 2 * o, h + 2 * o), black);
                Fill(new Rect(x, y, w, h), col);
            }

            float cx = Mathf.Round(center.x), cy = Mathf.Round(center.y), half = th * 0.5f;
            Bar(cx - g - len, cy - half, len, th);              // left
            Bar(cx + g, cy - half, len, th);                    // right
            if (!tShape) Bar(cx - half, cy - g - len, th, len); // top
            Bar(cx - half, cy + g, th, len);                    // bottom
            if (dot) Bar(cx - half, cy - half, th, th);
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
