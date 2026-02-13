using System;
using System.Reflection;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;

namespace Gironoid._Project.Code.Core.Services
{
    public sealed class EnergyService
    {
        private static long ResolveRegenIntervalMs(object energyCfg, int capacitorLevel)
        {
            if (energyCfg == null) return 0;

            var t = energyCfg.GetType();

            // ---------- 1) Methods ----------
            // long/int GetRegenIntervalMs(int)
            var miMs = t.GetMethod("GetRegenIntervalMs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (miMs != null)
            {
                try
                {
                    var v = miMs.Invoke(energyCfg, new object[] { capacitorLevel });
                    if (v is long l) return l;
                    if (v is int i) return i;
                    if (v is float f) return (long)f;
                }
                catch { /* ignore */ }
            }

            // int/float GetRegenIntervalSeconds(int)
            var miSec = t.GetMethod("GetRegenIntervalSeconds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (miSec != null)
            {
                try
                {
                    var v = miSec.Invoke(energyCfg, new object[] { capacitorLevel });
                    if (v is int i) return (long)i * 1000L;
                    if (v is long l) return l * 1000L;
                    if (v is float f) return (long)(f * 1000f);
                }
                catch { /* ignore */ }
            }

            // ---------- 2) Fields/Properties (Seconds) ----------
            // BaseRegenIntervalSeconds (int)
            int baseSec = 0;

            var piBaseSec = t.GetProperty("BaseRegenIntervalSeconds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (piBaseSec != null)
            {
                try
                {
                    var v = piBaseSec.GetValue(energyCfg);
                    if (v is int i) baseSec = i;
                    else if (v is float f) baseSec = (int)f;
                }
                catch { /* ignore */ }
            }

            if (baseSec <= 0)
            {
                var fiBaseSec = t.GetField("BaseRegenIntervalSeconds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fiBaseSec != null)
                {
                    try
                    {
                        var v = fiBaseSec.GetValue(energyCfg);
                        if (v is int i) baseSec = i;
                        else if (v is float f) baseSec = (int)f;
                    }
                    catch { /* ignore */ }
                }
            }

            // CapacitorRegenIntervalSecondsByLevel (int[])
            int[] byLevel = null;

            var piByLevel = t.GetProperty("CapacitorRegenIntervalSecondsByLevel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (piByLevel != null)
            {
                try { byLevel = piByLevel.GetValue(energyCfg) as int[]; } catch { /* ignore */ }
            }

            if (byLevel == null)
            {
                var fiByLevel = t.GetField("CapacitorRegenIntervalSecondsByLevel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fiByLevel != null)
                {
                    try { byLevel = fiByLevel.GetValue(energyCfg) as int[]; } catch { /* ignore */ }
                }
            }

            int sec = 0;

            if (byLevel != null && byLevel.Length > 0)
            {
                int idx = capacitorLevel;
                if (idx < 0) idx = 0;
                if (idx >= byLevel.Length) idx = byLevel.Length - 1;
                sec = byLevel[idx];
            }
            else
            {
                sec = baseSec;
            }

            // Safety MinRegenIntervalSeconds
            int minSec = 0;
            var piMin = t.GetProperty("MinRegenIntervalSeconds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (piMin != null)
            {
                try { if (piMin.GetValue(energyCfg) is int i) minSec = i; } catch { /* ignore */ }
            }
            if (minSec <= 0)
            {
                var fiMin = t.GetField("MinRegenIntervalSeconds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fiMin != null)
                {
                    try { if (fiMin.GetValue(energyCfg) is int i) minSec = i; } catch { /* ignore */ }
                }
            }

            if (minSec > 0 && sec > 0 && sec < minSec) sec = minSec;

            if (sec <= 0)
            {
                // ---------- 3) Legacy (Minutes) ----------
                var piMinLegacy = t.GetProperty("RegenIntervalMinutes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (piMinLegacy != null)
                {
                    try
                    {
                        var v = piMinLegacy.GetValue(energyCfg);
                        if (v is int i) return (long)i * 60_000L;
                        if (v is float f) return (long)(f * 60_000f);
                    }
                    catch { /* ignore */ }
                }

                var fiMinLegacy = t.GetField("RegenIntervalMinutes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fiMinLegacy != null)
                {
                    try
                    {
                        var v = fiMinLegacy.GetValue(energyCfg);
                        if (v is int i) return (long)i * 60_000L;
                        if (v is float f) return (long)(f * 60_000f);
                    }
                    catch { /* ignore */ }
                }

                return 0;
            }

            return (long)sec * 1000L;
        }

        public int GetEnergyCap(PlayerProfile p, EnergyConfig cfg)
        {
            if (p == null || cfg == null) return 0;
            return cfg.GetEnergyCap(p.TankLevel);
        }

        public void ApplyRegen(PlayerProfile p, EnergyConfig cfg, long nowMs)
        {
            if (p == null || cfg == null) return;
            if (nowMs <= 0) return;

            int cap = cfg.GetEnergyCap(p.TankLevel);
            if (cap <= 0) return;

            if (p.Energy >= cap)
            {
                if (p.LastEnergyServerTimeMs <= 0) p.LastEnergyServerTimeMs = nowMs;
                return;
            }

            long intervalMs = ResolveRegenIntervalMs(cfg, p.CapacitorLevel);
            if (intervalMs <= 0) return;

            long last = p.LastEnergyServerTimeMs;
            if (last <= 0) last = nowMs;

            // Защита от “отката времени”
            if (nowMs < last)
            {
                p.LastEnergyServerTimeMs = nowMs;
                return;
            }

            long delta = nowMs - last;
            if (delta < intervalMs) return;

            long ticks = delta / intervalMs;
            if (ticks <= 0) return;

            int add = ticks > int.MaxValue ? int.MaxValue : (int)ticks;

            int newEnergy = p.Energy + add;
            if (newEnergy > cap) newEnergy = cap;

            long consumed = (long)add * intervalMs;
            long newLast = last + consumed;

            p.Energy = newEnergy;
            p.LastEnergyServerTimeMs = newLast;
        }

        public long GetMsUntilNextEnergy(PlayerProfile p, EnergyConfig cfg, long nowMs)
        {
            if (p == null || cfg == null) return 0;
            if (nowMs <= 0) return 0;

            int cap = cfg.GetEnergyCap(p.TankLevel);
            if (cap <= 0) return 0;
            if (p.Energy >= cap) return 0;

            long intervalMs = ResolveRegenIntervalMs(cfg, p.CapacitorLevel);
            if (intervalMs <= 0) return 0;

            long last = p.LastEnergyServerTimeMs;
            if (last <= 0) last = nowMs;

            if (nowMs < last) return intervalMs;

            long passed = nowMs - last;
            long mod = passed % intervalMs;
            long left = intervalMs - mod;
            if (left < 0) left = 0;
            return left;
        }
    }
}
