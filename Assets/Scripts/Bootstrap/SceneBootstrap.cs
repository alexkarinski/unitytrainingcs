using CsAim.Core;
using CsAim.Data;
using CsAim.Player;
using CsAim.Training;
using CsAim.UI;
using UnityEngine;

namespace CsAim.Bootstrap
{
    /// <summary>
    /// Builds the whole training range from code, so the project needs no hand-made assets:
    /// open Assets/Scenes/Main.unity and press Play. Everything is in Source units.
    /// </summary>
    public sealed class SceneBootstrap : MonoBehaviour
    {
        void Awake()
        {
            // Lowest input latency: no vsync, uncapped frame rate.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            var cam = BuildCamera();
            BuildLighting();
            BuildArena(out var sprayWall);

            var playerGo = new GameObject("Player");
            playerGo.AddComponent<CharacterController>();
            var player = playerGo.AddComponent<PlayerController>();
            player.Init(cam, WeaponLibrary.Load());

            var scenarios = new GameObject("Scenarios").AddComponent<ScenarioManager>();
            scenarios.Init(player, sprayWall);

            var hud = new GameObject("HUD").AddComponent<Hud>();
            hud.Init(player, scenarios);
        }

        static Camera BuildCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 20000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.52f, 0.62f, 0.72f);
            go.AddComponent<AudioListener>();
            return cam;
        }

        static void BuildLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
        }

        static void BuildArena(out Transform sprayWall)
        {
            // Floor with a 64-unit grid: helps you feel distances and how far a counter-strafe slides.
            var floor = Box("Floor", new Vector3(0, -4, 1500), new Vector3(8000, 8, 8000), GridMaterial(new Color(0.42f, 0.42f, 0.4f), 64f, 8000f));

            var wallMat = Materials.Lit(new Color(0.6f, 0.58f, 0.52f));
            Box("WallN", new Vector3(0, 256, 5500), new Vector3(8000, 512, 16), wallMat);
            Box("WallS", new Vector3(0, 256, -2500), new Vector3(8000, 512, 16), wallMat);
            Box("WallE", new Vector3(4000, 256, 1500), new Vector3(16, 512, 8000), wallMat);
            Box("WallW", new Vector3(-4000, 256, 1500), new Vector3(16, 512, 8000), wallMat);

            // Cover boxes for the peek scenario (crate-sized: 64 units).
            var crateMat = Materials.Lit(new Color(0.45f, 0.35f, 0.22f));
            for (int i = -3; i <= 3; i++)
                Box("Crate", new Vector3(i * 260f, 32f, 1100f + (i & 1) * 250f), new Vector3(64, 64, 64), crateMat);

            // Spray wall: front face exactly ScenarioManager.SprayDistance from the spawn.
            float d = ScenarioManager.SprayDistance;
            var sprayMat = GridMaterial(new Color(0.85f, 0.85f, 0.82f), 16f, 640f);
            sprayWall = Box("SprayWall", new Vector3(0, 200, d + 4f), new Vector3(640, 400, 8), sprayMat).transform;

            floor.isStatic = true;
        }

        static GameObject Box(string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Material GridMaterial(Color baseColor, float cell, float extent)
        {
            const int px = 64;
            var tex = new Texture2D(px, px, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var line = baseColor * 0.75f;
            line.a = 1f;
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                    tex.SetPixel(x, y, x == 0 || y == 0 ? line : baseColor);
            tex.Apply();

            var mat = Materials.Lit(Color.white);
            mat.mainTexture = tex;
            mat.mainTextureScale = new Vector2(extent / cell, extent / cell);
            mat.SetFloat("_Glossiness", 0.1f);
            return mat;
        }
    }
}
