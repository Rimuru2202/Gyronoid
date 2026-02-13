using System;
using System.IO;
using UnityEngine;

namespace Gironoid._Project.Code.Core.Profile
{
    public sealed class ProfileRepository
    {
        private readonly string _path;

#if UNITY_WEBGL && !UNITY_EDITOR
        private const string PrefKeyJson = "gironoid_profile_json_local";
        private const string PrefKeyUpdated = "gironoid_profile_updated_local";
#endif

        public ProfileRepository(string fileName = "gironoid_profile.json")
        {
            _path = Path.Combine(Application.persistentDataPath, fileName);
        }

        public bool TryLoad(out string json, out long updatedUtcMs)
        {
            json = null;
            updatedUtcMs = 0;

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                json = PlayerPrefs.GetString(PrefKeyJson, "");
                var updStr = PlayerPrefs.GetString(PrefKeyUpdated, "0");
                long.TryParse(updStr, out updatedUtcMs);
                return !string.IsNullOrEmpty(json);
            }
            catch
            {
                json = null;
                updatedUtcMs = 0;
                return false;
            }
#else
            try
            {
                if (!File.Exists(_path))
                    return false;

                json = File.ReadAllText(_path);

                var dt = File.GetLastWriteTimeUtc(_path);
                if (dt.Kind != DateTimeKind.Utc) dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                updatedUtcMs = new DateTimeOffset(dt).ToUnixTimeMilliseconds();

                return !string.IsNullOrEmpty(json);
            }
            catch
            {
                return false;
            }
#endif
        }

        public void Save(string json, long updatedUtcMs)
        {
            if (json == null) json = "";

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                PlayerPrefs.SetString(PrefKeyJson, json);
                PlayerPrefs.SetString(PrefKeyUpdated, updatedUtcMs.ToString());
                PlayerPrefs.Save();
            }
            catch
            {
                // В WebGL лучше не падать на сохранении
            }
#else
            try
            {
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, json);

                if (File.Exists(_path))
                    File.Delete(_path);

                File.Move(tmp, _path);
            }
            catch
            {
                // Если локально не смогли — не падаем.
            }
#endif
        }
    }
}
