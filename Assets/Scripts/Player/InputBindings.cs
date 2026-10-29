using System.Collections.Generic;
using UnityEngine;

namespace CsAim.Player
{
    public enum InputAction
    {
        Attack, Attack2, Jump, Duck, Walk, Forward, Back, Left, Right, Reload,
        LastInv, InvNext, InvPrev, Inspect, SwitchHands,
        Slot1, Slot2, Slot3, Slot4, Slot5, Slot6, Slot7, Slot8, Slot9,
    }

    /// <summary>
    /// Key bindings using CS2's own key and command names, so a user_keys / config.cfg "bind"
    /// can be applied directly. Defaults are CS2's stock bindings (user_keys_default.vcfg).
    /// </summary>
    public sealed class InputBindings
    {
        // A key is either a KeyCode, a mouse button (0-4) or a wheel direction.
        struct Key
        {
            public KeyCode code;
            public int mouse;        // -1 none
            public int wheel;        // 0 none, +1 up, -1 down
        }

        readonly Dictionary<InputAction, List<Key>> _map = new Dictionary<InputAction, List<Key>>();

        public InputBindings() { ResetToDefaults(); }

        public void ResetToDefaults()
        {
            _map.Clear();
            Bind("MOUSE1", "+attack");
            Bind("MOUSE2", "+attack2");
            Bind("SPACE", "+jump");
            Bind("CTRL", "+duck");
            Bind("SHIFT", "+sprint");
            Bind("w", "+forward");
            Bind("s", "+back");
            Bind("a", "+left");
            Bind("d", "+right");
            Bind("r", "+reload");
            Bind("q", "lastinv");
            Bind("f", "+lookatweapon");
            Bind("h", "switchhands");
            Bind("MWHEELDOWN", "invnext");
            Bind("MWHEELUP", "invprev");
            for (int i = 1; i <= 9; i++) Bind(i.ToString(), "slot" + i);
        }

        /// <summary>Applies one CS2 bind. Unknown keys/commands are ignored. Returns true if used.</summary>
        public bool Bind(string keyName, string command)
        {
            if (!TryParseKey(keyName, out var key)) return false;

            // Rebinding a key replaces whatever it did before, as in the game.
            foreach (var list in _map.Values) list.RemoveAll(k => Same(k, key));

            bool used = false;
            foreach (var part in command.Split(';'))
            {
                if (!TryParseCommand(part.Trim().ToLowerInvariant(), out var action)) continue;
                if (!_map.TryGetValue(action, out var keys)) _map[action] = keys = new List<Key>();
                keys.Add(key);
                used = true;
            }
            return used;
        }

        public void ClearAll() => _map.Clear();

        public void Unbind(string keyName)
        {
            if (!TryParseKey(keyName, out var key)) return;
            foreach (var list in _map.Values) list.RemoveAll(k => Same(k, key));
        }

        public bool Held(InputAction a)
        {
            if (!_map.TryGetValue(a, out var keys)) return false;
            foreach (var k in keys)
            {
                if (k.mouse >= 0 && Input.GetMouseButton(k.mouse)) return true;
                if (k.wheel != 0 && WheelThisFrame(k.wheel)) return true;
                if (k.code != KeyCode.None && Input.GetKey(k.code)) return true;
            }
            return false;
        }

        public bool Pressed(InputAction a)
        {
            if (!_map.TryGetValue(a, out var keys)) return false;
            foreach (var k in keys)
            {
                if (k.mouse >= 0 && Input.GetMouseButtonDown(k.mouse)) return true;
                if (k.wheel != 0 && WheelThisFrame(k.wheel)) return true;
                if (k.code != KeyCode.None && Input.GetKeyDown(k.code)) return true;
            }
            return false;
        }

        public string Describe(InputAction a)
        {
            if (!_map.TryGetValue(a, out var keys) || keys.Count == 0) return "-";
            var k = keys[0];
            if (k.mouse >= 0) return "MOUSE" + (k.mouse + 1);
            if (k.wheel != 0) return k.wheel > 0 ? "MWHEELUP" : "MWHEELDOWN";
            return k.code.ToString();
        }

        static bool WheelThisFrame(int dir) => dir > 0 ? Input.mouseScrollDelta.y > 0f : Input.mouseScrollDelta.y < 0f;

        static bool Same(Key a, Key b) => a.code == b.code && a.mouse == b.mouse && a.wheel == b.wheel;

        public static bool TryParseCommand(string cmd, out InputAction action)
        {
            action = default;
            switch (cmd)
            {
                case "+attack": action = InputAction.Attack; return true;
                case "+attack2": case "+zoom": action = InputAction.Attack2; return true;
                case "+jump": action = InputAction.Jump; return true;
                case "+duck": action = InputAction.Duck; return true;
                case "+sprint": case "+speed": action = InputAction.Walk; return true;   // CS2 calls walk "+sprint"
                case "+forward": action = InputAction.Forward; return true;
                case "+back": action = InputAction.Back; return true;
                case "+left": case "+moveleft": action = InputAction.Left; return true;
                case "+right": case "+moveright": action = InputAction.Right; return true;
                case "+reload": action = InputAction.Reload; return true;
                case "lastinv": action = InputAction.LastInv; return true;
                case "invnext": action = InputAction.InvNext; return true;
                case "invprev": action = InputAction.InvPrev; return true;
                case "+lookatweapon": action = InputAction.Inspect; return true;
                case "switchhands": case "switchhandsleft": case "switchhandsright": action = InputAction.SwitchHands; return true;
            }
            if (cmd.StartsWith("slot") && int.TryParse(cmd.Substring(4), out int n) && n >= 1 && n <= 9)
            {
                action = InputAction.Slot1 + (n - 1);
                return true;
            }
            return false;
        }

        static bool TryParseKey(string name, out Key key)
        {
            key = new Key { code = KeyCode.None, mouse = -1, wheel = 0 };
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.Trim().Trim('"').ToUpperInvariant();

            if (n.StartsWith("MOUSE") && int.TryParse(n.Substring(5), out int mb) && mb >= 1 && mb <= 5)
            {
                key.mouse = mb - 1;
                return true;
            }
            if (n == "MWHEELUP") { key.wheel = 1; return true; }
            if (n == "MWHEELDOWN") { key.wheel = -1; return true; }

            if (n.Length == 1)
            {
                char c = n[0];
                if (c >= 'A' && c <= 'Z') { key.code = KeyCode.A + (c - 'A'); return true; }
                if (c >= '0' && c <= '9') { key.code = KeyCode.Alpha0 + (c - '0'); return true; }
                switch (c)
                {
                    case '`': key.code = KeyCode.BackQuote; return true;
                    case ',': key.code = KeyCode.Comma; return true;
                    case '.': key.code = KeyCode.Period; return true;
                    case '/': key.code = KeyCode.Slash; return true;
                    case ';': key.code = KeyCode.Semicolon; return true;
                    case '\'': key.code = KeyCode.Quote; return true;
                    case '[': key.code = KeyCode.LeftBracket; return true;
                    case ']': key.code = KeyCode.RightBracket; return true;
                    case '\\': key.code = KeyCode.Backslash; return true;
                    case '-': key.code = KeyCode.Minus; return true;
                    case '=': key.code = KeyCode.Equals; return true;
                }
                return false;
            }

            if (n.Length >= 2 && n[0] == 'F' && int.TryParse(n.Substring(1), out int f) && f >= 1 && f <= 15)
            {
                key.code = KeyCode.F1 + (f - 1);
                return true;
            }

            switch (n)
            {
                case "SPACE": key.code = KeyCode.Space; return true;
                case "CTRL": case "LCTRL": key.code = KeyCode.LeftControl; return true;
                case "RCTRL": key.code = KeyCode.RightControl; return true;
                case "SHIFT": case "LSHIFT": key.code = KeyCode.LeftShift; return true;
                case "RSHIFT": key.code = KeyCode.RightShift; return true;
                case "ALT": case "LALT": key.code = KeyCode.LeftAlt; return true;
                case "RALT": key.code = KeyCode.RightAlt; return true;
                case "TAB": key.code = KeyCode.Tab; return true;
                case "CAPSLOCK": key.code = KeyCode.CapsLock; return true;
                case "ENTER": key.code = KeyCode.Return; return true;
                case "BACKSPACE": key.code = KeyCode.Backspace; return true;
                case "DEL": key.code = KeyCode.Delete; return true;
                case "INS": key.code = KeyCode.Insert; return true;
                case "HOME": key.code = KeyCode.Home; return true;
                case "END": key.code = KeyCode.End; return true;
                case "PGUP": key.code = KeyCode.PageUp; return true;
                case "PGDN": key.code = KeyCode.PageDown; return true;
                case "UPARROW": key.code = KeyCode.UpArrow; return true;
                case "DOWNARROW": key.code = KeyCode.DownArrow; return true;
                case "LEFTARROW": key.code = KeyCode.LeftArrow; return true;
                case "RIGHTARROW": key.code = KeyCode.RightArrow; return true;
                case "SEMICOLON": key.code = KeyCode.Semicolon; return true;
            }
            if (n.StartsWith("KP_") && n.Length == 4 && char.IsDigit(n[3]))
            {
                key.code = KeyCode.Keypad0 + (n[3] - '0');
                return true;
            }
            return false;
        }
    }
}
