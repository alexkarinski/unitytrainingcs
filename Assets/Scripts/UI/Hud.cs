using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsAim.Core;
using CsAim.Data;
using CsAim.Player;
using CsAim.Training;
using UnityEngine;

namespace CsAim.UI
{
    /// <summary>Immediate-mode HUD and settings menu. No assets required.</summary>
    public sealed class Hud : MonoBehaviour
    {
        PlayerController _player;
        ScenarioManager _scenarios;

        bool _menuOpen;
        bool _recoilCrosshair;
        bool _dynamicCrosshair = true;
        string _sensText, _zoomText, _importPath, _importStatus = string.Empty;
        GUIStyle _label, _big, _box;
        Vector2 _weaponScroll;

        const string SensKey = "csaim.sensitivity";
        const string ZoomKey = "csaim.zoom_ratio";

        public void Init(PlayerController player, ScenarioManager scenarios)
        {
            _player = player;
            _scenarios = scenarios;
            player.sensitivity = PlayerPrefs.GetFloat(SensKey, player.sensitivity);
            player.zoomSensitivityRatio = PlayerPrefs.GetFloat(ZoomKey, player.zoomSensitivityRatio);
            _sensText = player.sensitivity.ToString(CultureInfo.InvariantCulture);
            _zoomText = player.zoomSensitivityRatio.ToString(CultureInfo.InvariantCulture);
            _importPath = PlayerPrefs.GetString(WeaponLibrary.PathPrefKey, ItemsGameImporter.DefaultItemsGamePath);
            SetMenu(false);
        }

        void SetMenu(bool open)
        {
            _menuOpen = open;
            _player.InputEnabled = !open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) SetMenu(!_menuOpen);
            // Clicking back into the game window re-captures the mouse.
            if (!_menuOpen && Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
                SetMenu(false);
            if (!_menuOpen && Input.GetKeyDown(KeyCode.F1)) _scenarios.Begin(_scenarios.Current);
        }

        void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            _label.normal.textColor = Color.white;
            _big = new GUIStyle(_label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _box = new GUIStyle(GUI.skin.box);
            _box.normal.background = Solid(new Color(0f, 0f, 0f, 0.55f));
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void OnGUI()
        {
            if (_player == null || _player.Weapon == null) return;
            EnsureStyles();

            DrawCrosshair();
            DrawStatus();
            DrawStats();
            if (_scenarios.Current == Scenario.SprayControl) DrawSprayPanel();
            if (_menuOpen) DrawMenu();
            else
                GUI.Label(new Rect(10, Screen.height - 28, 900, 24),
                    "<color=#bbb>Esc - menu   F1 - restart   1-9 / Q - weapon   R - reload   RMB - scope/silencer   Shift - walk   Ctrl - duck</color>", _label);
        }

        // ------------------------------------------------------------------ crosshair

        float PixelsPerDegree(float deg)
        {
            float vfov = _player.Camera.fieldOfView * Mathf.Deg2Rad;
            return Screen.height * 0.5f / Mathf.Tan(vfov * 0.5f) * Mathf.Tan(deg * Mathf.Deg2Rad);
        }

        void DrawCrosshair()
        {
            var w = _player.Weapon;
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            if (_recoilCrosshair)
            {
                // cl_crosshair_recoil: draw where the next bullet goes, not where the camera points.
                Vector2 off = w.BulletRecoilDeg - w.ViewRecoilDeg;
                c += new Vector2(PixelsPerDegree(off.y), -PixelsPerDegree(off.x));
            }

            float gap = 4f;
            if (_dynamicCrosshair)
            {
                // Gap tracks the real cone: inaccuracy + spread, as an angle on screen.
                float coneDeg = Mathf.Atan(w.CurrentInaccuracy + w.CurrentSpread) * Mathf.Rad2Deg;
                gap = Mathf.Max(3f, PixelsPerDegree(coneDeg));
            }

            if (w.Def.altIsScope && w.ZoomLevel > 0)
            {
                Line(new Rect(0, c.y, Screen.width, 1), Color.black);
                Line(new Rect(c.x, 0, 1, Screen.height), Color.black);
                return;
            }

            const float len = 7f, th = 2f;
            var col = new Color(0.2f, 1f, 0.3f);
            Line(new Rect(c.x - gap - len, c.y - th * 0.5f, len, th), col);
            Line(new Rect(c.x + gap, c.y - th * 0.5f, len, th), col);
            Line(new Rect(c.x - th * 0.5f, c.y - gap - len, th, len), col);
            Line(new Rect(c.x - th * 0.5f, c.y + gap, th, len), col);
        }

        static void Line(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        // ------------------------------------------------------------------ status

        void DrawStatus()
        {
            var w = _player.Weapon;
            float max = w.Mode.maxPlayerSpeed;
            float speed = _player.Speed2D;
            bool accurate = _player.OnGround && speed <= max * CsConstants.AccurateSpeedFraction;

            GUILayout.BeginArea(new Rect(10, 10, 430, 190), _box);
            GUILayout.Label($"<b>{w.Def.displayName}</b>   {(w.Reloading ? "<color=#fc6>reloading</color>" : $"{w.Ammo} / {w.Def.clipSize}")}", _big);
            GUILayout.Label($"<color=#999>data: {w.Def.source}</color>", _label);
            GUILayout.Label($"recoil: {(w.Recoil.IsMeasured ? "measured pattern" : $"seeded table (seed {w.Def.recoilSeed})")}", _label);
            string sc = accurate ? "#5f5" : "#f55";
            GUILayout.Label($"speed <color={sc}>{speed:0}</color> u/s   (accurate ≤ {max * CsConstants.AccurateSpeedFraction:0})", _label);
            GUILayout.Label($"inaccuracy {w.CurrentInaccuracy * 1000f:0.00}   spread {w.CurrentSpread * 1000f:0.00}   (x1000, items_game units)", _label);
            GUILayout.Label($"spray: shot {w.ShotsFired}   recoil index {w.RecoilIndex:0.0}", _label);
            GUILayout.EndArea();
        }

        void DrawStats()
        {
            var s = _scenarios.Stats;
            float elapsed = Time.time - s.startTime;
            GUILayout.BeginArea(new Rect(Screen.width - 300, 10, 290, 190), _box);
            GUILayout.Label($"<b>{ScenarioTitle(_scenarios.Current)}</b>", _big);
            GUILayout.Label($"time {elapsed:0}s   kills {s.kills}", _label);
            GUILayout.Label($"accuracy {s.Accuracy:0}%   ({s.hits}/{s.shots})", _label);
            GUILayout.Label($"headshot hits {s.HeadshotRate:0}%", _label);
            GUILayout.Label($"avg time-to-kill {s.AverageKillTime * 1000f:0} ms", _label);
            GUILayout.Label($"shots while accurate {s.MovementDiscipline:0}%", _label);
            GUILayout.EndArea();
        }

        static string ScenarioTitle(Scenario s)
        {
            switch (s)
            {
                case Scenario.SprayControl: return "Spray control";
                case Scenario.Flick: return "Flick / one-tap";
                case Scenario.Strafe: return "Strafing bots";
                default: return "Peeks";
            }
        }

        // ------------------------------------------------------------------ spray analysis

        void DrawSprayPanel()
        {
            const float pw = 300, ph = 360;
            var rect = new Rect(Screen.width - pw - 10, 210, pw, ph);
            GUI.Box(rect, GUIContent.none, _box);

            var spray = _scenarios.Spray.Current ?? _scenarios.Spray.Last;
            GUI.Label(new Rect(rect.x + 8, rect.y + 4, pw, 22), "<b>Last spray</b>  (relative to first shot)", _label);

            // Plot area: 1 degree = 18 px, origin near the bottom because recoil climbs upward.
            var plot = new Rect(rect.x + 10, rect.y + 30, pw - 20, 220);
            Line(plot, new Color(1, 1, 1, 0.05f));
            Vector2 origin = new Vector2(plot.center.x, plot.yMax - 30);
            const float ppd = 18f;
            Line(new Rect(origin.x - 4, origin.y - 1, 9, 2), Color.white);
            Line(new Rect(origin.x - 1, origin.y - 4, 2, 9), Color.white);

            // Blue = the weapon's pattern with no mouse input; this is what you are cancelling.
            foreach (var r in _scenarios.PatternPreview) Dot(origin, r, ppd, new Color(0.3f, 0.6f, 1f, 0.6f), 3f, plot);

            if (spray != null)
            {
                foreach (var shot in spray.shots)
                    Dot(origin, shot.Landing, ppd, new Color(1f, 0.25f, 0.2f), 4f, plot);

                float y = plot.yMax + 6;
                GUI.Label(new Rect(rect.x + 8, y, pw, 22), $"compensation  <b>{spray.CompensationPercent:0}%</b>   (best {_scenarios.Spray.BestCompensation:0}%)", _label);
                GUI.Label(new Rect(rect.x + 8, y + 22, pw, 22), $"recoil error  {spray.MeanRecoilError:0.00}°  <color=#999>your fault</color>", _label);
                GUI.Label(new Rect(rect.x + 8, y + 44, pw, 22), $"spread        {spray.MeanSpread:0.00}°  <color=#999>random</color>", _label);
                GUI.Label(new Rect(rect.x + 8, y + 66, pw, 22), $"bullets {spray.shots.Count}", _label);
            }
            else
            {
                GUI.Label(new Rect(rect.x + 8, plot.yMax + 6, pw - 16, 80),
                    "Aim at the white square and hold LMB.\nFollow the <color=#3f6>green</color> path on the wall with your crosshair - it's the mirror of the <color=#59f>blue</color> pattern.", _label);
            }
        }

        static void Dot(Vector2 origin, Vector2 pitchYaw, float ppd, Color c, float size, Rect clip)
        {
            var p = new Vector2(origin.x + pitchYaw.y * ppd, origin.y - pitchYaw.x * ppd);
            if (!clip.Contains(p)) return;
            Line(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), c);
        }

        // ------------------------------------------------------------------ menu

        void DrawMenu()
        {
            var r = new Rect(Screen.width * 0.5f - 330, Screen.height * 0.5f - 280, 660, 560);
            GUILayout.BeginArea(r, _box);
            GUILayout.Label("<b>CS Aim Trainer</b>", _big);

            GUILayout.Label("Scenario", _label);
            GUILayout.BeginHorizontal();
            foreach (Scenario s in System.Enum.GetValues(typeof(Scenario)))
                if (GUILayout.Button(ScenarioTitle(s), GUILayout.Height(30))) { _scenarios.Begin(s); SetMenu(false); }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(250));
            GUILayout.Label("Weapon", _label);
            _weaponScroll = GUILayout.BeginScrollView(_weaponScroll, GUILayout.Height(220));
            var weapons = _player.Weapons;
            for (int i = 0; i < weapons.Count; i++)
            {
                string mark = i == _player.WeaponIndex ? "▶ " : "";
                if (GUILayout.Button($"{mark}{i + 1}. {weapons[i].displayName}")) _player.SelectWeapon(i);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            GUILayout.Label("Settings (same values as CS2)", _label);
            GUILayout.BeginHorizontal();
            GUILayout.Label("sensitivity", _label, GUILayout.Width(170));
            _sensText = GUILayout.TextField(_sensText, GUILayout.Width(80));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("zoom_sensitivity_ratio", _label, GUILayout.Width(170));
            _zoomText = GUILayout.TextField(_zoomText, GUILayout.Width(80));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Apply")) ApplySettings();

            _player.Weapon.InfiniteAmmo = GUILayout.Toggle(_player.Weapon.InfiniteAmmo, " sv_infinite_ammo 2 (refill on reload)");
            _scenarios.Impacts.Enabled = GUILayout.Toggle(_scenarios.Impacts.Enabled, " sv_showimpacts 1");
            _scenarios.ShowPatternOnWall = GUILayout.Toggle(_scenarios.ShowPatternOnWall, " show spray drawing on wall");
            _recoilCrosshair = GUILayout.Toggle(_recoilCrosshair, " cl_crosshair_recoil");
            _dynamicCrosshair = GUILayout.Toggle(_dynamicCrosshair, " crosshair gap = real inaccuracy cone");
            _scenarios.ArmoredTargets = GUILayout.Toggle(_scenarios.ArmoredTargets, " targets have kevlar + helmet");
            _player.invertY = GUILayout.Toggle(_player.invertY, " invert Y");
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Import weapon data from your CS2 install (items_game.txt or weapon_*.vdata):", _label);
            GUILayout.BeginHorizontal();
            _importPath = GUILayout.TextField(_importPath);
            if (GUILayout.Button("Import", GUILayout.Width(80))) Import();
            GUILayout.EndHorizontal();
            if (_importStatus.Length > 0) GUILayout.Label(_importStatus, _label);

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Resume", GUILayout.Height(30))) SetMenu(false);
            if (GUILayout.Button("Quit", GUILayout.Height(30))) Application.Quit();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void ApplySettings()
        {
            if (float.TryParse(_sensText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0f)
            {
                _player.sensitivity = s;
                PlayerPrefs.SetFloat(SensKey, s);
            }
            if (float.TryParse(_zoomText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var z) && z > 0f)
            {
                _player.zoomSensitivityRatio = z;
                PlayerPrefs.SetFloat(ZoomKey, z);
            }
            PlayerPrefs.Save();
        }

        void Import()
        {
            string path = _importPath.Trim().Trim('"');
            if (!File.Exists(path)) { _importStatus = "<color=#f66>File not found.</color>"; return; }

            try
            {
                List<WeaponDefinition> weapons = ItemsGameImporter.ImportFile(path);
                if (weapons.Count == 0) { _importStatus = "<color=#f66>No weapons found in that file.</color>"; return; }

                PlayerPrefs.SetString(WeaponLibrary.PathPrefKey, path);
                PlayerPrefs.Save();
                _player.Init(_player.Camera, weapons);
                _scenarios.RebuildPatternPreview();
                _importStatus = $"<color=#6f6>Imported {weapons.Count} weapons. Saved as default source.</color>";
            }
            catch (System.Exception e)
            {
                _importStatus = $"<color=#f66>Import failed: {e.Message}</color>";
            }
        }
    }
}
