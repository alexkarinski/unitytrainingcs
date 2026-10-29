using CsAim.Core;
using CsAim.Data;
using UnityEngine;

namespace CsAim.Viewmodel
{
    public enum WeaponKind { Rifle, Carbine, Sniper, Pistol }

    /// <summary>Handles to the animated parts of a built viewmodel.</summary>
    public sealed class ViewmodelRig
    {
        public WeaponKind kind;
        public Transform weapon;        // the gun, origin at the trigger
        public Transform magazine;
        public Vector3 magazineHome;
        public Transform bolt;          // charging handle / bolt / slide
        public Vector3 boltHome;
        public Transform suppressor;    // null if the weapon can't take one
        public Transform muzzle;        // flash position
        public Transform ejectPort;     // shells
        public Transform rightHand, leftHand;
        public Vector3 rightHome, leftHome;
        public Vector3 rightElbow, leftElbow;   // in rig space
        public Transform rightForearm, leftForearm;
        public Transform rightSleeve, leftSleeve;
        public float kickBack = 1.6f;   // per-shot recoil animation strength
        public float kickPitch = 4f;
    }

    /// <summary>
    /// Builds stylised first-person weapons and gloved arms from primitives, in Source units
    /// (an AK is ~35 units long). Original geometry; no game assets are used.
    /// Everything goes on the viewmodel layer with colliders removed immediately (a deferred Destroy
    /// would leave a shell's collider alive for the rest of the frame, in front of the next bullet).
    /// </summary>
    public static class ViewmodelBuilder
    {
        public const int Layer = 31;

        static readonly Color Metal = new Color(0.11f, 0.11f, 0.12f);
        static readonly Color MetalLight = new Color(0.22f, 0.22f, 0.24f);
        static readonly Color Wood = new Color(0.42f, 0.22f, 0.10f);
        static readonly Color Polymer = new Color(0.16f, 0.16f, 0.15f);
        static readonly Color Olive = new Color(0.27f, 0.33f, 0.21f);
        static readonly Color Glove = new Color(0.10f, 0.10f, 0.10f);
        static readonly Color GloveKnuckle = new Color(0.18f, 0.17f, 0.16f);
        static readonly Color Sleeve = new Color(0.24f, 0.27f, 0.20f);
        static readonly Color Brass = new Color(0.78f, 0.6f, 0.25f);

        public static WeaponKind KindOf(WeaponDefinition def)
        {
            string id = def.weaponId;
            if (id.Contains("awp") || id.Contains("ssg08") || id.Contains("scar20") || id.Contains("g3sg1")) return WeaponKind.Sniper;
            if (id.Contains("deagle") || id.Contains("usp") || id.Contains("glock") || id.Contains("p250") || id.Contains("hkp2000") ||
                id.Contains("fiveseven") || id.Contains("tec9") || id.Contains("cz75") || id.Contains("elite") || id.Contains("revolver"))
                return WeaponKind.Pistol;
            if (id.Contains("m4a1") || id.Contains("famas") || id.Contains("aug") || id.Contains("mp") || id.Contains("ump") || id.Contains("p90"))
                return WeaponKind.Carbine;
            return WeaponKind.Rifle;
        }

        public static ViewmodelRig Build(WeaponDefinition def, Transform parent)
        {
            var rig = new ViewmodelRig { kind = KindOf(def) };
            var weapon = new GameObject("Weapon").transform;
            weapon.SetParent(parent, false);
            rig.weapon = weapon;

            switch (rig.kind)
            {
                case WeaponKind.Sniper: BuildSniper(rig, weapon); break;
                case WeaponKind.Pistol: BuildPistol(rig, weapon, def.weaponId); break;
                case WeaponKind.Carbine: BuildCarbine(rig, weapon, def.weaponId); break;
                default: BuildAk(rig, weapon); break;
            }

            rig.magazineHome = rig.magazine.localPosition;
            if (rig.bolt != null) rig.boltHome = rig.bolt.localPosition;

            BuildArms(rig, parent);
            SetLayerRecursive(parent.gameObject);
            return rig;
        }

        // ------------------------------------------------------------------ weapons

        static void BuildAk(ViewmodelRig rig, Transform w)
        {
            Box(w, "Receiver", new Vector3(0, 0.2f, 2f), new Vector3(2.1f, 2.8f, 13f), Metal);
            Box(w, "DustCover", new Vector3(0, 1.75f, 1.5f), new Vector3(1.9f, 0.7f, 11f), MetalLight);
            Box(w, "Handguard", new Vector3(0, 0.1f, 12.2f), new Vector3(2.4f, 2.3f, 7.5f), Wood);
            Box(w, "GasTube", new Vector3(0, 1.55f, 12.4f), new Vector3(1.6f, 1.1f, 7f), Wood);
            Cylinder(w, "Barrel", new Vector3(0, 0.5f, 19.5f), 0.85f, 8f, Metal);
            Box(w, "FrontSight", new Vector3(0, 1.9f, 22.6f), new Vector3(0.35f, 2.2f, 0.5f), Metal);
            Box(w, "RearSight", new Vector3(0, 2.15f, 7.8f), new Vector3(1.2f, 0.5f, 1.2f), Metal);
            Box(w, "Stock", new Vector3(0, -1.2f, -9.5f), new Vector3(1.9f, 3.2f, 10f), Wood, new Vector3(-6f, 0, 0));
            Box(w, "Grip", new Vector3(0, -3.0f, -2.2f), new Vector3(1.6f, 4.2f, 2.2f), Wood, new Vector3(16f, 0, 0));
            Box(w, "TriggerGuard", new Vector3(0, -1.7f, 0.6f), new Vector3(0.4f, 0.4f, 3.2f), Metal);

            var mag = new GameObject("Magazine").transform;
            mag.SetParent(w, false);
            mag.localPosition = new Vector3(0, -1.2f, 3.6f);
            Box(mag, "MagTop", new Vector3(0, -1.6f, 0f), new Vector3(1.5f, 3.4f, 2.9f), MetalLight, new Vector3(-8f, 0, 0));
            Box(mag, "MagBottom", new Vector3(0, -4.6f, 1.3f), new Vector3(1.5f, 3.4f, 2.7f), MetalLight, new Vector3(-26f, 0, 0));
            rig.magazine = mag;

            rig.bolt = Box(w, "ChargingHandle", new Vector3(1.35f, 0.9f, 4.2f), new Vector3(0.9f, 0.5f, 0.9f), MetalLight);
            rig.muzzle = Anchor(w, "Muzzle", new Vector3(0, 0.5f, 24f));
            rig.ejectPort = Anchor(w, "Eject", new Vector3(1.2f, 1.0f, 3.0f));

            rig.rightHome = new Vector3(0.2f, -3.0f, -2.4f);
            rig.leftHome = new Vector3(0f, -0.9f, 12.5f);
            rig.kickBack = 1.7f;
            rig.kickPitch = 4.5f;
        }

        static void BuildCarbine(ViewmodelRig rig, Transform w, string id)
        {
            Box(w, "UpperReceiver", new Vector3(0, 0.5f, 2.5f), new Vector3(2.0f, 2.2f, 12f), Metal);
            Box(w, "LowerReceiver", new Vector3(0, -1.0f, 1.5f), new Vector3(1.9f, 1.8f, 8f), Polymer);
            Box(w, "Rail", new Vector3(0, 1.85f, 3f), new Vector3(1.0f, 0.5f, 11f), MetalLight);
            Box(w, "CarryHandle", new Vector3(0, 2.6f, 1.5f), new Vector3(0.9f, 1.2f, 4f), Metal);
            Box(w, "Handguard", new Vector3(0, 0.4f, 12f), new Vector3(2.5f, 2.5f, 8f), Polymer);
            Cylinder(w, "Barrel", new Vector3(0, 0.5f, 19f), 0.7f, 6f, Metal);
            Box(w, "FrontSight", new Vector3(0, 2.0f, 16.6f), new Vector3(0.4f, 2.4f, 0.6f), Metal);
            Box(w, "BufferTube", new Vector3(0, 0.0f, -6f), new Vector3(1.3f, 1.3f, 6f), Metal);
            Box(w, "Stock", new Vector3(0, -0.6f, -10.5f), new Vector3(1.8f, 3.4f, 4.5f), Polymer);
            Box(w, "Grip", new Vector3(0, -3.0f, -2.2f), new Vector3(1.5f, 4.2f, 2.1f), Polymer, new Vector3(18f, 0, 0));
            Box(w, "TriggerGuard", new Vector3(0, -2.0f, 0.6f), new Vector3(0.4f, 0.4f, 3.0f), Metal);

            var mag = new GameObject("Magazine").transform;
            mag.SetParent(w, false);
            mag.localPosition = new Vector3(0, -1.8f, 4f);
            Box(mag, "Mag", new Vector3(0, -2.6f, 0.3f), new Vector3(1.3f, 5.5f, 2.6f), MetalLight, new Vector3(-6f, 0, 0));
            rig.magazine = mag;

            rig.bolt = Box(w, "ChargingHandle", new Vector3(0, 1.75f, -2.6f), new Vector3(1.4f, 0.4f, 0.8f), MetalLight);
            rig.muzzle = Anchor(w, "Muzzle", new Vector3(0, 0.5f, 22.1f));
            rig.ejectPort = Anchor(w, "Eject", new Vector3(1.1f, 0.8f, 3.5f));

            if (id.Contains("silencer"))
            {
                rig.suppressor = Cylinder(w, "Suppressor", new Vector3(0, 0.5f, 25.5f), 1.5f, 7f, Polymer);
                rig.suppressor.GetComponent<Renderer>().sharedMaterial.color = new Color(0.08f, 0.08f, 0.08f);
            }

            rig.rightHome = new Vector3(0.2f, -3.0f, -2.4f);
            rig.leftHome = new Vector3(0f, -0.9f, 12.5f);
            rig.kickBack = 1.4f;
            rig.kickPitch = 3.5f;
        }

        static void BuildSniper(ViewmodelRig rig, Transform w)
        {
            Box(w, "Receiver", new Vector3(0, 0.4f, 2.5f), new Vector3(2.2f, 2.4f, 13f), Olive);
            Box(w, "Forend", new Vector3(0, 0.0f, 13f), new Vector3(2.6f, 2.6f, 9f), Olive);
            Cylinder(w, "Barrel", new Vector3(0, 0.6f, 25f), 1.0f, 15f, Metal);
            Box(w, "Stock", new Vector3(0, -0.8f, -9.5f), new Vector3(2.2f, 4.2f, 11f), Olive);
            Box(w, "Cheek", new Vector3(0, 1.8f, -8f), new Vector3(2.0f, 1.2f, 6f), Olive);
            Box(w, "Grip", new Vector3(0, -3.1f, -2.6f), new Vector3(1.7f, 4.4f, 2.3f), Olive, new Vector3(14f, 0, 0));
            Cylinder(w, "ScopeTube", new Vector3(0, 3.5f, 3.5f), 1.4f, 13f, Metal);
            Cylinder(w, "ScopeFront", new Vector3(0, 3.5f, 10.8f), 2.3f, 2.5f, Metal);
            Cylinder(w, "ScopeRear", new Vector3(0, 3.5f, -3.4f), 1.9f, 1.8f, Metal);
            Box(w, "ScopeMount", new Vector3(0, 2.2f, 3.5f), new Vector3(0.8f, 1.2f, 6f), Metal);

            var mag = new GameObject("Magazine").transform;
            mag.SetParent(w, false);
            mag.localPosition = new Vector3(0, -1.2f, 3.5f);
            Box(mag, "Mag", new Vector3(0, -1.2f, 0f), new Vector3(1.5f, 2.6f, 3.6f), Metal);
            rig.magazine = mag;

            var bolt = new GameObject("Bolt").transform;
            bolt.SetParent(w, false);
            bolt.localPosition = new Vector3(1.3f, 0.9f, -0.6f);
            Box(bolt, "Handle", new Vector3(0.9f, -0.3f, 0f), new Vector3(1.8f, 0.4f, 0.4f), MetalLight, new Vector3(0, 0, -25f));
            Sphere(bolt, "Knob", new Vector3(1.9f, -0.8f, 0f), 0.75f, MetalLight);
            rig.bolt = bolt;

            rig.muzzle = Anchor(w, "Muzzle", new Vector3(0, 0.6f, 32.6f));
            rig.ejectPort = Anchor(w, "Eject", new Vector3(1.2f, 1.0f, 1.5f));
            rig.rightHome = new Vector3(0.2f, -3.1f, -2.8f);
            rig.leftHome = new Vector3(0f, -1.2f, 13.5f);
            rig.kickBack = 3.2f;
            rig.kickPitch = 9f;
        }

        static void BuildPistol(ViewmodelRig rig, Transform w, string id)
        {
            bool deagle = id.Contains("deagle");
            float len = deagle ? 10f : 7.5f;
            Color slideCol = deagle ? MetalLight : Metal;

            var slide = new GameObject("Slide").transform;
            slide.SetParent(w, false);
            slide.localPosition = new Vector3(0, 0.9f, 2.6f);
            Box(slide, "SlideBody", Vector3.zero, new Vector3(1.4f, 1.5f, len), slideCol);
            Box(slide, "RearSight", new Vector3(0, 0.95f, -len * 0.45f), new Vector3(0.9f, 0.4f, 0.4f), Metal);
            Box(slide, "FrontSight", new Vector3(0, 0.9f, len * 0.45f), new Vector3(0.25f, 0.4f, 0.4f), Metal);
            rig.bolt = slide;

            Box(w, "Frame", new Vector3(0, -0.2f, 2.3f), new Vector3(1.3f, 1.0f, len - 0.8f), Polymer);
            Box(w, "Grip", new Vector3(0, -2.6f, -0.6f), new Vector3(1.35f, 4.2f, 2.2f), Polymer, new Vector3(16f, 0, 0));
            Box(w, "TriggerGuard", new Vector3(0, -1.2f, 1.4f), new Vector3(0.35f, 0.35f, 2.2f), Polymer);

            var mag = new GameObject("Magazine").transform;
            mag.SetParent(w, false);
            mag.localPosition = new Vector3(0, -2.6f, -0.6f);
            Box(mag, "MagBase", new Vector3(0, -2.2f, 0f), new Vector3(1.2f, 0.5f, 2.0f), Metal, new Vector3(16f, 0, 0));
            rig.magazine = mag;

            rig.muzzle = Anchor(w, "Muzzle", new Vector3(0, 0.9f, 2.6f + len * 0.5f + 0.2f));
            rig.ejectPort = Anchor(w, "Eject", new Vector3(0.8f, 1.3f, 3.2f));

            if (id.Contains("usp"))
            {
                rig.suppressor = Cylinder(w, "Suppressor", new Vector3(0, 0.9f, 2.6f + len * 0.5f + 3.2f), 1.25f, 6f, Polymer);
                rig.suppressor.GetComponent<Renderer>().sharedMaterial.color = new Color(0.08f, 0.08f, 0.08f);
            }

            rig.rightHome = new Vector3(0.1f, -2.8f, -0.9f);
            rig.leftHome = new Vector3(-1.4f, -3.2f, -0.2f);   // cupping the right hand
            rig.kickBack = deagle ? 2.2f : 1.1f;
            rig.kickPitch = deagle ? 9f : 5f;
        }

        // ------------------------------------------------------------------ arms

        static void BuildArms(ViewmodelRig rig, Transform rigRoot)
        {
            rig.rightHand = Hand(rig.weapon, "RightHand", rig.rightHome, right: true);
            rig.leftHand = Hand(rig.weapon, "LeftHand", rig.leftHome, right: false);

            // Elbows sit below and behind the screen edges, in rig space, so forearms stretch and
            // swing naturally when the gun kicks or the left hand goes for a magazine.
            bool pistol = rig.kind == WeaponKind.Pistol;
            rig.rightElbow = new Vector3(pistol ? 6f : 7f, -13f, -14f);
            rig.leftElbow = new Vector3(pistol ? -8f : -9f, -13f, pistol ? -12f : -2f);

            rig.rightForearm = Limb(rigRoot, "RightForearm", 2.5f, Glove);
            rig.leftForearm = Limb(rigRoot, "LeftForearm", 2.5f, Glove);
            rig.rightSleeve = Limb(rigRoot, "RightSleeve", 3.3f, Sleeve);
            rig.leftSleeve = Limb(rigRoot, "LeftSleeve", 3.3f, Sleeve);
        }

        static Transform Hand(Transform parent, string name, Vector3 pos, bool right)
        {
            var h = new GameObject(name).transform;
            h.SetParent(parent, false);
            h.localPosition = pos;
            float side = right ? 1f : -1f;
            Box(h, "Palm", new Vector3(0.9f * side, 0f, 0f), new Vector3(1.6f, 3.0f, 2.6f), Glove);
            Box(h, "Fingers", new Vector3(-0.5f * side, -0.2f, 0.9f), new Vector3(1.5f, 2.8f, 1.2f), GloveKnuckle);
            Box(h, "Thumb", new Vector3(-0.3f * side, 1.3f, 0.2f), new Vector3(0.9f, 0.9f, 2.2f), Glove, new Vector3(0, 20f * side, 0));
            return h;
        }

        static Transform Limb(Transform parent, string name, float thickness, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            Prepare(go, c);
            go.transform.localScale = new Vector3(thickness, 1f, thickness);
            return go.transform;
        }

        /// <summary>Stretches a capsule between two rig-space points (wrist -> elbow).</summary>
        public static void PlaceLimb(Transform limb, Vector3 from, Vector3 to, float startT, float endT, float thickness)
        {
            Vector3 a = Vector3.Lerp(from, to, startT), b = Vector3.Lerp(from, to, endT);
            Vector3 d = b - a;
            float len = Mathf.Max(0.01f, d.magnitude);
            limb.localPosition = (a + b) * 0.5f;
            limb.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
            limb.localScale = new Vector3(thickness, len * 0.5f + thickness * 0.25f, thickness);
        }

        // ------------------------------------------------------------------ effects

        public static Transform MuzzleFlash(Transform parent)
        {
            var root = new GameObject("MuzzleFlash").transform;
            root.SetParent(parent, false);
            var mat = Materials.Unlit(new Color(1f, 0.82f, 0.35f, 0.95f));
            for (int i = 0; i < 3; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(root, false);
                // One star facing the camera plus two petals lying along the barrel.
                if (i == 2)
                {
                    q.transform.localScale = new Vector3(3.5f, 3.5f, 1f);
                }
                else
                {
                    q.transform.localRotation = Quaternion.Euler(90f, i * 90f, 0f);
                    q.transform.localScale = new Vector3(2.2f, 7f, 1f);
                    q.transform.localPosition = new Vector3(0, 0, 2.5f);
                }
                var r = q.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            SetLayerRecursive(root.gameObject);
            root.gameObject.SetActive(false);
            return root;
        }

        public static Transform Shell(Transform parent)
        {
            var t = Cylinder(parent, "Shell", Vector3.zero, 0.45f, 1.5f, Brass);
            t.gameObject.layer = Layer;
            t.gameObject.SetActive(false);
            return t;
        }

        // ------------------------------------------------------------------ primitives

        static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Color c, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            Prepare(go, c);
            return go.transform;
        }

        /// <summary>Cylinder along +Z.</summary>
        static Transform Cylinder(Transform parent, string name, Vector3 center, float diameter, float length, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
            Prepare(go, c);
            return go.transform;
        }

        static void Sphere(Transform parent, string name, Vector3 pos, float d, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * d;
            Prepare(go, c);
        }

        static Transform Anchor(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static void Prepare(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Materials.Lit(c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.layer = Layer;
        }

        public static void SetLayerRecursive(GameObject go)
        {
            go.layer = Layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject);
        }
    }
}
