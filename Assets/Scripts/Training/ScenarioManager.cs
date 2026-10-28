using System.Collections.Generic;
using CsAim.Combat;
using CsAim.Core;
using CsAim.Player;
using UnityEngine;

namespace CsAim.Training
{
    public enum Scenario
    {
        SprayControl,   // the guide's drill: wall, sv_showimpacts, follow the drawing
        RecoilPath,     // follow the drawing; leave the corridor and the spray restarts
        Flick,          // static one-taps, armoured + helmet
        Strafe,         // ADAD bots - counter-strafe, then shoot
        Peek,           // wide swings from cover
    }

    /// <summary>Stats for the current run of a scenario.</summary>
    public sealed class RunStats
    {
        public int shots, hits, headshots, kills;
        public int accurateShots;              // fired below 34% of max speed (counter-strafed)
        public readonly List<float> killTimes = new List<float>();
        public float startTime;

        public float Accuracy => shots == 0 ? 0f : 100f * hits / shots;
        public float HeadshotRate => hits == 0 ? 0f : 100f * headshots / hits;
        public float MovementDiscipline => shots == 0 ? 100f : 100f * accurateShots / shots;
        public float AverageKillTime
        {
            get
            {
                if (killTimes.Count == 0) return 0f;
                float s = 0f;
                foreach (var t in killTimes) s += t;
                return s / killTimes.Count;
            }
        }
    }

    public sealed class ScenarioManager : MonoBehaviour
    {
        public Scenario Current { get; private set; } = Scenario.SprayControl;
        public RunStats Stats { get; private set; } = new RunStats();
        public SprayAnalyzer Spray { get; } = new SprayAnalyzer();
        public PathTrainer Path { get; } = new PathTrainer();
        public bool IsWallScenario => Current == Scenario.SprayControl || Current == Scenario.RecoilPath;
        public ImpactMarkers Impacts { get; private set; }
        public List<Vector2> PatternPreview { get; private set; } = new List<Vector2>();
        public bool ShowPatternOnWall = true;
        public bool ArmoredTargets = true;
        public int SimultaneousTargets = 1;

        // Spray wall sits 10 m (~394 u) away, like a typical practice distance in the guide.
        public const float SprayDistance = 394f;

        PlayerController _player;
        readonly List<Target> _targets = new List<Target>();
        GameObject _sprayRig;
        LineRenderer _ghostLine, _idealLine;
        Transform _nextPoint;
        Transform _wall;
        bool _wasHolding;

        public void Init(PlayerController player, Transform sprayWall)
        {
            _player = player;
            _wall = sprayWall;
            Impacts = new GameObject("Impacts").AddComponent<ImpactMarkers>();
            player.Shot += OnShot;
            player.WeaponChanged += w => { RebuildPatternPreview(); Path.Reset(w.Def.clipSize); };
            BuildSprayRig();
            RebuildPatternPreview();
            Begin(Scenario.SprayControl);
        }

        public void Begin(Scenario s)
        {
            Current = s;
            Stats = new RunStats { startTime = Time.time };
            foreach (var t in _targets) if (t != null) Destroy(t.gameObject);
            _targets.Clear();
            Impacts.Clear();
            _player.Weapon?.ResetSpray();

            bool spray = IsWallScenario;
            _sprayRig.SetActive(spray);
            _nextPoint.gameObject.SetActive(s == Scenario.RecoilPath);
            Path.Reset(_player.Weapon != null ? _player.Weapon.Def.clipSize : 30);
            _wall.gameObject.SetActive(spray);
            Spray.MinShotsToScore = Mathf.Min(10, _player.Weapon != null ? _player.Weapon.Def.clipSize : 10);

            _player.Teleport(Vector3.zero, 0f);
            for (int i = 0; i < SimultaneousTargets && !spray; i++) SpawnTarget();
        }

        void SpawnTarget()
        {
            Vector3 eye = _player.EyePosition;
            float yaw, dist;
            TargetMotion motion;

            switch (Current)
            {
                case Scenario.Flick:
                    yaw = Random.Range(-45f, 45f);
                    dist = Random.Range(400f, 1600f);
                    motion = TargetMotion.Static;
                    break;
                case Scenario.Strafe:
                    yaw = Random.Range(-20f, 20f);
                    dist = Random.Range(600f, 1400f);
                    motion = TargetMotion.Strafe;
                    break;
                default:
                    yaw = Random.Range(-25f, 25f);
                    dist = Random.Range(700f, 1500f);
                    motion = TargetMotion.Peek;
                    break;
            }

            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 feet = new Vector3(eye.x, 0f, eye.z) + dir * dist;
            var t = Target.Create(feet, -dir, motion);
            t.HasArmor = ArmoredTargets;
            t.HasHelmet = ArmoredTargets;
            t.Killed += OnTargetKilled;
            _targets.Add(t);
        }

        void OnTargetKilled(Target t)
        {
            Stats.kills++;
            Stats.killTimes.Add(Time.time - t.SpawnTime);
            _targets.Remove(t);
            SpawnTarget();
        }

        void OnShot(ShotEvent e)
        {
            Stats.shots++;

            // CS counts you as accurate when moving slower than 34% of the weapon's max speed.
            float max = _player.Weapon.Mode.maxPlayerSpeed;
            if (_player.OnGround && _player.Speed2D <= max * CsConstants.AccurateSpeedFraction)
                Stats.accurateShots++;

            if (e.hitbox != null)
            {
                Stats.hits++;
                if (e.hitbox.group == HitGroup.Head) Stats.headshots++;
            }

            if (e.hitSomething)
                Impacts.Add(e.hit.point, e.hit.normal, e.shot.firstShotAccurate && e.shot.shotIndex == 0);

            if (Current == Scenario.SprayControl)
                Spray.OnShot(e, _player.Weapon.Def.displayName);
            else if (Current == Scenario.RecoilPath)
                OnPathShot(e);
        }

        void OnPathShot(in ShotEvent e)
        {
            var outcome = Path.OnShot(e.shot.shotIndex, e.eyeAngles, e.shot.recoilDeg, e.shot.spreadDeg, Time.time);
            if (outcome == PathTrainer.Outcome.Continue) return;

            // Attempt over (failed or done): stop the spray right now, refill, snap the view back to
            // where the spray started and wait for the trigger to be released before the next try.
            _player.Weapon.Interrupt();
            _player.BlockFireUntilRelease();
            _player.SetEyeAngles(Path.StartEye);
        }

        void Update()
        {
            bool holding = _player.AttackHeld;
            if (_wasHolding && !holding)
            {
                Spray.FinishCurrent();
                if (Current == Scenario.RecoilPath) Path.OnTriggerReleased(Time.time);
            }
            if (!_wasHolding && holding && Current == Scenario.RecoilPath) Impacts.Clear();
            _wasHolding = holding;

            if (Current == Scenario.RecoilPath) UpdateNextPoint();

            if (_ghostLine != null)
            {
                _ghostLine.enabled = ShowPatternOnWall;
                _idealLine.enabled = ShowPatternOnWall;
            }
        }

        // ------------------------------------------------------------------ spray wall overlay

        void BuildSprayRig()
        {
            _sprayRig = new GameObject("SprayRig");
            var aim = GameObject.CreatePrimitive(PrimitiveType.Quad);
            aim.name = "AimPoint";
            Destroy(aim.GetComponent<Collider>());
            aim.transform.SetParent(_sprayRig.transform, false);
            aim.GetComponent<Renderer>().material = Materials.Unlit(Color.white);
            aim.transform.localScale = Vector3.one * 3f;

            var np = GameObject.CreatePrimitive(PrimitiveType.Quad);
            np.name = "NextPoint";
            Destroy(np.GetComponent<Collider>());
            np.transform.SetParent(_sprayRig.transform, false);
            np.GetComponent<Renderer>().material = Materials.Unlit(new Color(1f, 0.9f, 0.1f, 0.45f));
            _nextPoint = np.transform;

            _ghostLine = MakeLine("UncompensatedPattern", new Color(0.25f, 0.55f, 1f, 0.9f));
            _idealLine = MakeLine("IdealMousePath", new Color(0.2f, 1f, 0.35f, 0.9f));
        }

        LineRenderer MakeLine(string name, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_sprayRig.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.8f;
            lr.material = Materials.Unlit(Color.white);
            lr.startColor = lr.endColor = c;
            lr.numCapVertices = 2;
            return lr;
        }

        public void RebuildPatternPreview()
        {
            if (_player == null || _player.Weapon == null) return;
            PatternPreview = SprayAnalyzer.PreviewPattern(_player.Weapon.Def);
            Spray.MinShotsToScore = Mathf.Min(10, _player.Weapon.Def.clipSize);
            LayoutWall();
        }

        /// <summary>
        /// Projects the pattern onto the wall from the standing eye position. Blue = where bullets
        /// go if you don't touch the mouse. Green = the mirrored drawing your screen crosshair should
        /// follow to keep every bullet on the white aim point (the camera itself shows 45% of the
        /// recoil, so the crosshair path is 55% of the mirrored pattern).
        /// </summary>
        void LayoutWall()
        {
            if (_wall == null) return;
            Vector3 eye = new Vector3(0f, CsConstants.StandEyeHeight, 0f);
            Vector3 aimPoint = eye + Vector3.forward * (SprayDistance - 0.5f);

            _sprayRig.transform.Find("AimPoint").position = aimPoint;
            _sprayRig.transform.Find("AimPoint").rotation = Quaternion.LookRotation(Vector3.forward);

            int n = PatternPreview.Count;
            _ghostLine.positionCount = n;
            _idealLine.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                Vector2 r = PatternPreview[i];
                _ghostLine.SetPosition(i, Project(eye, r));
                _idealLine.SetPosition(i, Project(eye, PathTrainer.CrosshairTarget(r)));
            }
        }

        /// <summary>Yellow square on the green line: where the crosshair must be for the next bullet,
        /// sized to the pass/fail corridor.</summary>
        void UpdateNextPoint()
        {
            if (PatternPreview.Count == 0) return;
            int next = Path.Active ? Mathf.Clamp(_player.Weapon.ShotsFired, 0, PatternPreview.Count - 1) : 0;
            Vector3 eye = new Vector3(0f, CsConstants.StandEyeHeight, 0f);
            _nextPoint.position = Project(eye, PathTrainer.CrosshairTarget(PatternPreview[next])) - Vector3.forward * 0.2f;
            _nextPoint.rotation = Quaternion.LookRotation(Vector3.forward);
            float size = 2f * Mathf.Tan(Path.ToleranceDeg * Mathf.Deg2Rad) * SprayDistance;
            _nextPoint.localScale = Vector3.one * size;
        }

        static Vector3 Project(Vector3 eye, Vector2 pitchYawDeg)
        {
            Vector3 dir = Quaternion.Euler(-pitchYawDeg.x, pitchYawDeg.y, 0f) * Vector3.forward;
            float t = (SprayDistance - 0.6f) / Mathf.Max(0.01f, dir.z);
            return eye + dir * t;
        }
    }
}
