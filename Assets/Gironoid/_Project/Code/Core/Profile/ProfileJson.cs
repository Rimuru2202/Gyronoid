using UnityEngine;

namespace Gironoid._Project.Code.Core.Profile
{
    public static class ProfileJson
    {
        public static string ToJson(PlayerProfile p)
        {
            if (p == null) return "";
            return JsonUtility.ToJson(p);
        }

        public static PlayerProfile FromJsonSafe(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                var p = JsonUtility.FromJson<PlayerProfile>(json);
                p?.Normalize();
                return p;
            }
            catch
            {
                return null;
            }
        }
    }
}