// Minimal UnityEngine stand-in so pure-logic scripts can run under plain .NET.
using System;
namespace UnityEngine
{
    public static class Mathf
    {
        public const float PI = (float)Math.PI, Deg2Rad = PI / 180f, Rad2Deg = 180f / PI;
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Atan(float f) => (float)Math.Atan(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Exp(float f) => (float)Math.Exp(f);
        public static float Log(float f) => (float)Math.Log(f);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Abs(float f) => Math.Abs(f);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Sign(float f) => f >= 0 ? 1 : -1;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0, 1);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-6f;
        public static float MoveTowards(float c, float t, float d) => Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d;
        public static float Repeat(float t, float l) => Clamp(t - (float)Math.Floor(t / l) * l, 0, l);
        public static float DeltaAngle(float a, float b) { float d = Repeat(b - a, 360); return d > 180 ? d - 360 : d; }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public Vector2 normalized => magnitude > 1e-5f ? this / magnitude : zero;
        public override string ToString() => $"({x:0.00}, {y:0.00})";
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 left => new Vector3(-1, 0, 0);
    }
    public enum KeyCode
    {
        None, A = 97, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Alpha0 = 48, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
        BackQuote = 96, Comma = 44, Period = 46, Slash = 47, Semicolon = 59, Quote = 39, LeftBracket = 91,
        RightBracket = 93, Backslash = 92, Minus = 45, Equals = 61, F1 = 282, Space = 32, LeftControl = 306,
        RightControl = 305, LeftShift = 304, RightShift = 303, LeftAlt = 308, RightAlt = 307, Tab = 9,
        CapsLock = 301, Return = 13, Backspace = 8, Delete = 127, Insert = 277, Home = 278, End = 279,
        PageUp = 280, PageDown = 281, UpArrow = 273, DownArrow = 274, LeftArrow = 276, RightArrow = 275, Keypad0 = 256,
    }
    public static class Input
    {
        public static bool GetKey(KeyCode k) => false;
        public static bool GetKeyDown(KeyCode k) => false;
        public static bool GetMouseButton(int b) => false;
        public static bool GetMouseButtonDown(int b) => false;
        public static Vector2 mouseScrollDelta => default;
    }
    public class ScriptableObject { public string name; public static T CreateInstance<T>() where T : new() => new T(); }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class TextAreaAttribute : Attribute { public TextAreaAttribute(int a, int b) { } }
    public class CreateAssetMenuAttribute : Attribute { public string menuName, fileName; }
}
