using System;
using System.Reflection;

namespace Gironoid._Project.Code.Gameplay
{
    public static class SceneUtil
    {
        public static string ResolveSceneName(string constName, string fallback)
        {
            try
            {
                var t = Type.GetType("Gironoid._Project.Code.Core.GironoidScenes, Assembly-CSharp");
                if (t == null)
                    return fallback;

                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

                var f = t.GetField(constName, flags);
                if (f != null && f.FieldType == typeof(string))
                {
                    var v = f.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                }

                var p = t.GetProperty(constName, flags);
                if (p != null && p.PropertyType == typeof(string))
                {
                    var v = p.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                }
            }
            catch { }

            return fallback;
        }
    }
}