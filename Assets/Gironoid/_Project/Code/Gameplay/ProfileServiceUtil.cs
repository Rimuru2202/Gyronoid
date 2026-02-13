using System;
using System.Reflection;
using Gironoid._Project.Code.Core;
using Gironoid._Project.Code.Core.Profile;
using Gironoid._Project.Code.Core.Services;

namespace Gironoid._Project.Code.Gameplay
{
    public static class ProfileServiceUtil
    {
        public static PlayerProfile GetProfileSafe()
        {
            if (!GironoidApp.IsReady) return null;

            try
            {
                var ps = GironoidApp.ProfileService;
                if (ps == null) return null;

                // чаще всего: ps.Profile
                var t = ps.GetType();
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                var pProp = t.GetProperty("Profile", flags);
                if (pProp != null && pProp.CanRead)
                    return pProp.GetValue(ps) as PlayerProfile;

                var curProp = t.GetProperty("Current", flags);
                if (curProp != null && curProp.CanRead)
                    return curProp.GetValue(ps) as PlayerProfile;

                var f = t.GetField("Profile", flags);
                if (f != null)
                    return f.GetValue(ps) as PlayerProfile;
            }
            catch { }

            return null;
        }

        public static bool TryApply(Action<PlayerProfile> mutator, bool flushToServer)
        {
            if (!GironoidApp.IsReady) return false;

            var ps = GironoidApp.ProfileService;
            if (ps == null || mutator == null)
                return false;

            var t = ps.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            try
            {
                // Apply(Action<PlayerProfile>, bool)
                var m = t.GetMethod("Apply", flags);
                if (m != null)
                {
                    var pars = m.GetParameters();
                    if (pars.Length == 2 &&
                        pars[0].ParameterType == typeof(Action<PlayerProfile>) &&
                        pars[1].ParameterType == typeof(bool))
                    {
                        m.Invoke(ps, new object[] { mutator, flushToServer });
                        return true;
                    }

                    // Apply(Action<PlayerProfile>)
                    if (pars.Length == 1 && pars[0].ParameterType == typeof(Action<PlayerProfile>))
                    {
                        m.Invoke(ps, new object[] { mutator });
                        return true;
                    }
                }

                // ApplyProfile(Action<PlayerProfile>, bool) — на всякий
                var m2 = t.GetMethod("ApplyProfile", flags);
                if (m2 != null)
                {
                    var pars = m2.GetParameters();
                    if (pars.Length == 2 &&
                        pars[0].ParameterType == typeof(Action<PlayerProfile>) &&
                        pars[1].ParameterType == typeof(bool))
                    {
                        m2.Invoke(ps, new object[] { mutator, flushToServer });
                        return true;
                    }

                    if (pars.Length == 1 && pars[0].ParameterType == typeof(Action<PlayerProfile>))
                    {
                        m2.Invoke(ps, new object[] { mutator });
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public static bool ShouldFlushToCloud()
        {
            try
            {
                if (GironoidApp.IsReady && GironoidApp.Yandex != null)
                    return GironoidApp.Yandex.CanUseCloud;
            }
            catch { }

            return false;
        }
    }
}
