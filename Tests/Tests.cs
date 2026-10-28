// Logic tests for the CS2 mechanics, run with: dotnet run --project Tests
using System;
using CsAim.Core;
using CsAim.Player;
using CsAim.Weapons;
using CsAim.Data;
using UnityEngine;

static class Tests
{
    static WeaponDefinition Ak(bool measured)
    {
        var w = new WeaponDefinition { weaponId = "weapon_ak47", clipSize = 30, reloadTime = 2.43f, isFullAuto = true, recoilSeed = 223 };
        w.primary = new WeaponModeStats { cycleTime = 0.1f, maxPlayerSpeed = 215, spread = 0.6f, inaccuracyStand = 6.41f, inaccuracyCrouch = 4.81f,
            inaccuracyFire = 7.8f, inaccuracyMove = 175f, inaccuracyJump = 140f, recoilAngle = 0, recoilAngleVariance = 70, recoilMagnitude = 30 };
        w.measuredPatternCsv = measured ? "0 0\n0.5 0.1\n1.2 0.0\n2.0 -0.3" : "";
        return w;
    }

    static void PathTests()
    {
        var p = new CsAim.Training.PathTrainer { ToleranceDeg = 0.7f };
        p.Reset(30);
        Check(p.TargetLength == 10, "progressive path starts at 10 bullets");

        // Perfect compensation: mouse exactly cancels recoil for 10 bullets -> completed, length grows
        CsAim.Training.PathTrainer.Outcome o = default;
        for (int i = 0; i < 10; i++)
        {
            var recoil = new Vector2(0.6f * i, 0.1f * i);
            o = p.OnShot(i, -recoil, recoil, new Vector2(0.2f, 0f), i * 0.1f);
        }
        Check(o == CsAim.Training.PathTrainer.Outcome.Completed && p.CleanSprays == 1, "perfect 10-bullet spray completes");
        Check(p.TargetLength == 15, "clean attempt raises target to 15");
        Check(Math.Abs(p.PathAccuracy - 100f) < 0.01f, $"perfect path accuracy 100% (got {p.PathAccuracy:0.0})");

        // No compensation: bullet offset grows past tolerance -> fails and reports the bullet
        int failedAt = -1;
        for (int i = 0; i < 15; i++)
        {
            var recoil = new Vector2(0.6f * i, 0f);
            if (p.OnShot(i, Vector2.zero, recoil, Vector2.zero, 10 + i * 0.1f) == CsAim.Training.PathTrainer.Outcome.Failed) { failedAt = i; break; }
        }
        Check(failedAt == 2, $"uncompensated spray fails on bullet 3 (index {failedAt}) when off by 1.2 deg");
        Check(!p.Active && p.Attempts == 2, "failure ends attempt; next shot starts a new one");
        Check(p.PathAccuracy < 100f && p.MeanErrorDeg > 0f, "accuracy drops after a sloppy attempt");

        // Spread must not cause a fail, only the controllable part
        var q = new CsAim.Training.PathTrainer { ToleranceDeg = 0.7f }; q.Reset(30);
        var r0 = q.OnShot(0, Vector2.zero, Vector2.zero, new Vector2(3f, 3f), 0);
        Check(r0 == CsAim.Training.PathTrainer.Outcome.Continue && q.OnTargetRate == 0f, "big spread: not a fail, but counted off-target");

        // Crosshair target is 55% of the mirrored recoil (camera shows 45%)
        var ct = CsAim.Training.PathTrainer.CrosshairTarget(new Vector2(4f, -2f));
        Check(Math.Abs(ct.x + 2.2f) < 1e-4 && Math.Abs(ct.y - 1.1f) < 1e-4, $"crosshair target {ct}");
    }

    static void WeaponTests()
    {
        float dt = CsConstants.TickInterval;
        var still = new OwnerState { onGround = true };

        // Fire rate: 600 RPM -> 10 shots in one second of holding (shot at t=0 plus 9 more before 1.0s)
        var w = new WeaponController(Ak(false), 7);
        var shots = new System.Collections.Generic.List<ShotInfo>();
        w.Fired += s => shots.Add(s);
        float t = 0; for (int i = 0; i < 64; i++) { t += dt; w.Tick(t, dt, still, true, i == 0); }
        Check(shots.Count == 10, $"AK fires 10 rounds per second (got {shots.Count})");
        Check(shots[0].firstShotAccurate && shots[0].recoilDeg.magnitude < 1e-4, "first bullet: no recoil, accurate");
        Check(Math.Abs(shots[0].inaccuracy - 0.00641f) < 1e-6, "first bullet uses inaccuracy_stand/1000");
        Check(shots[5].inaccuracy > shots[1].inaccuracy, "inaccuracy_fire accumulates during spray");
        Console.WriteLine($"   seeded climb by shot 10: {shots[9].recoilDeg}");
        Check(shots[9].recoilDeg.x > 3f, "seeded recoil climbs upward");

        // Determinism: same weapon data => identical recoil pattern (spread differs by seed only)
        var w2 = new WeaponController(Ak(false), 99); var s2 = new System.Collections.Generic.List<ShotInfo>(); w2.Fired += s => s2.Add(s);
        t = 0; for (int i = 0; i < 64; i++) { t += dt; w2.Tick(t, dt, still, true, i == 0); }
        bool samePattern = true; for (int i = 0; i < 10; i++) samePattern &= (shots[i].recoilDeg - s2[i].recoilDeg).magnitude < 1e-4f;
        Check(samePattern, "recoil pattern is deterministic (learnable), independent of spread seed");

        // Release resets spray; after recovery a tap is first-shot accurate again
        for (int i = 0; i < 64; i++) { t += dt; w2.Tick(t, dt, still, false, false); }
        Check(w2.ShotsFired == 0, "releasing trigger resets shots fired");
        int before = s2.Count; t += dt; w2.Tick(t, dt, still, true, true);
        Check(s2.Count == before + 1 && s2[s2.Count - 1].firstShotAccurate, $"after 1s rest, tap is accurate again (recoil {s2[s2.Count-1].recoilDeg})");

        // Measured pattern is used verbatim
        var wm = new WeaponController(Ak(true), 1); var sm = new System.Collections.Generic.List<ShotInfo>(); wm.Fired += s => sm.Add(s);
        t = 0; for (int i = 0; i < 30; i++) { t += dt; wm.Tick(t, dt, still, true, i == 0); }
        Check(Math.Abs(sm[1].recoilDeg.x - 0.5f) < 1e-4 && Math.Abs(sm[3].recoilDeg.y + 0.3f) < 1e-4, $"measured pattern applied exactly ({sm[1].recoilDeg}, {sm[3].recoilDeg})");

        // Infinite ammo: empty mag triggers reload, refills after reload time
        var wa = new WeaponController(Ak(false), 3); int n = 0; wa.Fired += _ => n++;
        t = 0; for (int i = 0; i < 64 * 4; i++) { t += dt; wa.Tick(t, dt, still, true, i == 0); }
        Check(n == 30 && wa.Reloading, $"30-round mag then reload (fired {n})");
        for (int i = 0; i < 64 * 3; i++) { t += dt; wa.Tick(t, dt, still, false, false); }
        Check(wa.Ammo == 30 && !wa.Reloading, "magazine refilled after reload_time");

        // Semi-auto only fires on press
        var deag = Ak(false); deag.isFullAuto = false; deag.primary.cycleTime = 0.225f;
        var wd = new WeaponController(deag, 4); int nd = 0; wd.Fired += _ => nd++;
        t = 0; for (int i = 0; i < 64; i++) { t += dt; wd.Tick(t, dt, still, true, i == 0); }
        Check(nd == 1, $"semi-auto: holding fires once (got {nd})");

        // Running shot is far less accurate than standing
        var wr = new WeaponController(Ak(false), 5); ShotInfo runShot = default; wr.Fired += s => runShot = s;
        wr.Tick(dt, dt, new OwnerState { onGround = true, velocity = new Vector3(215, 0, 0) }, true, true);
        Check(runShot.inaccuracy > 0.15f, $"running AK first shot inaccuracy {runShot.inaccuracy * 1000:0} (x1000) vs 6.41 standing");
    }

    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

    static int Main()
    {
        // 1. ran1 reference: Numerical Recipes ran1 with idum=-1 -> first deviate 0.41599...
        var r = new ValveUniformRandomStream(1);
        float f = r.RandomFloat();
        Console.WriteLine($"   ran1(seed 1) first = {f:0.0000000}");
        Check(Math.Abs(f - 0.4159993f) < 1e-5, "ran1 matches Numerical Recipes reference");
        var a = new ValveUniformRandomStream(223); var b = new ValveUniformRandomStream(223);
        bool same = true; for (int i = 0; i < 100; i++) same &= a.RandomFloat(-70, 70) == b.RandomFloat(-70, 70);
        Check(same, "same seed => identical stream");
        int lo = int.MaxValue, hi = int.MinValue; var ri = new ValveUniformRandomStream(5);
        for (int i = 0; i < 10000; i++) { int v = ri.RandomInt(3, 7); lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        Check(lo == 3 && hi == 7, "RandomInt stays in [low, high] and covers it");

        // 2. KeyValues normalisation + parsing both formats
        Check(KeyValues.Normalise("inaccuracy stand") == "inaccuracy_stand", "normalise VDF key");
        Check(KeyValues.Normalise("m_flInaccuracyStand") == "inaccuracy_stand", "normalise KV3 key: " + KeyValues.Normalise("m_flInaccuracyStand"));
        Check(KeyValues.Normalise("m_bIsFullAuto") == "is_full_auto", "normalise bool: " + KeyValues.Normalise("m_bIsFullAuto"));
        Check(KeyValues.Normalise("m_iMaxClip1") == "max_clip1", "normalise int: " + KeyValues.Normalise("m_iMaxClip1"));
        var vdf = KeyValues.Parse("\"items_game\" { // c\n \"prefabs\" { \"weapon_ak47_prefab\" { \"prefab\" \"rifle\" \"attributes\" { \"cycletime\" \"0.100000\" \"inaccuracy stand\" \"6.41\" } } } }");
        var attrs = vdf.FindChild("items_game").FindChild("prefabs").FindChild("weapon_ak47_prefab").FindChild("attributes");
        Check(attrs.TryGetFloat("inaccuracy_stand", out var ist) && Math.Abs(ist - 6.41f) < 1e-4, "VDF parse nested attribute");
        var kv3 = KeyValues.Parse("<!-- kv3 encoding:text:version{e21c7f3c} -->\n{\n m_flCycleTime = [ 0.1, 0.2 ]\n m_nRecoilSeed = 223\n m_bIsFullAuto = true\n}");
        var body = kv3.Children[0];
        Check(body.GetString("recoil_seed") == "223", "KV3 scalar: " + body.GetString("recoil_seed"));
        Check(body.GetString("cycle_time") != null && body.GetString("cycle_time").StartsWith("["), "KV3 array kept raw");

        // 3. Movement: AK max speed 215. Run to top speed, then counter-strafe (press opposite key)
        var m = new SourceMovement();
        float dt = CsConstants.TickInterval;
        for (int i = 0; i < 128; i++) m.Tick(dt, Vector3.right, 215f, true, false, false);
        Check(Math.Abs(m.Velocity.x - 215f) < 0.5f, $"reaches max speed 215 (got {m.Velocity.x:0.0})");
        int ticks = 0;
        while (m.Velocity.x > 215f * CsConstants.AccurateSpeedFraction && ticks < 200) { m.Tick(dt, Vector3.left, 215f, true, false, false); ticks++; }
        float csMs = ticks * dt * 1000f;
        var m2 = new SourceMovement(); for (int i = 0; i < 128; i++) m2.Tick(dt, Vector3.right, 215f, true, false, false);
        int t2 = 0; while (m2.Velocity.x > 215f * CsConstants.AccurateSpeedFraction && t2 < 200) { m2.Tick(dt, Vector3.zero, 0f, true, false, false); t2++; }
        Console.WriteLine($"   counter-strafe to accurate: {csMs:0} ms, release only: {t2 * dt * 1000f:0} ms");
        Check(csMs < t2 * dt * 1000f, "counter-strafing stops faster than releasing the key");
        Check(csMs > 50 && csMs < 150, "counter-strafe window ~100 ms like CS");
        var j = new SourceMovement(); j.Tick(dt, Vector3.zero, 0, true, true, false);
        float peak = 0, y = 0; for (int i = 0; i < 200; i++) { y += j.Velocity.y * dt; j.FinishGravity(dt); peak = Math.Max(peak, y); j.Tick(dt, Vector3.zero, 0, false, false, false); if (y < 0) break; }
        Console.WriteLine($"   jump apex height: {peak:0.0} u");
        Check(Math.Abs(peak - 57f) < 1.5f, "jump apex ~57 units");

        // 4. Accuracy: standing still = base, running adds move inaccuracy, walking at 34% = accurate
        var mode = new WeaponModeStats { inaccuracyStand = 6.41f, inaccuracyCrouch = 4.81f, inaccuracyMove = 175f, inaccuracyJump = 140f, maxPlayerSpeed = 215f };
        float still = AccuracyModel.StateInaccuracy(mode, new OwnerState { onGround = true });
        float slow = AccuracyModel.StateInaccuracy(mode, new OwnerState { onGround = true, velocity = new Vector3(215f * 0.33f, 0, 0) });
        float run = AccuracyModel.StateInaccuracy(mode, new OwnerState { onGround = true, velocity = new Vector3(215f, 0, 0) });
        float crouch = AccuracyModel.StateInaccuracy(mode, new OwnerState { onGround = true, duckAmount = 1f });
        Check(Math.Abs(still - 6.41f) < 1e-4, "standing = inaccuracy_stand");
        Check(Math.Abs(slow - still) < 1e-4, "below 34% speed = no movement penalty");
        Check(run > 150f, $"full run adds inaccuracy_move ({run:0.0})");
        Check(Math.Abs(crouch - 4.81f) < 1e-4, "crouched = inaccuracy_crouch");
        float rec = AccuracyModel.Decay(100f, 0.368f, 0.368f);
        Check(Math.Abs(rec - 10f) < 0.01f, "after recovery_time 90% of penalty recovered");

        // 5. Deviation stays inside the combined cone
        var rng = new ValveUniformRandomStream(9); float maxR = 0;
        for (int i = 0; i < 20000; i++) maxR = Math.Max(maxR, AccuracyModel.SampleDeviation(rng, 0.006f, 0.0006f).magnitude);
        Check(maxR <= 0.0066f + 1e-6, $"deviation within inaccuracy+spread ({maxR:0.00000})");

        WeaponTests();
        PathTests();
        Console.WriteLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
        return fails;
    }
}
