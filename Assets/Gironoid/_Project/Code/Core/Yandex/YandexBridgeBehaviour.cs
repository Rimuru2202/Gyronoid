using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Gironoid._Project.Code.Core.Yandex
{
    [DisallowMultipleComponent]
    public sealed class YandexBridgeBehaviour : MonoBehaviour
    {
        private Type _yandexGameType;
        private bool _resolved;

        // NEW: готовность облака/SDK (чтобы не затирать данные на старте)
        [Header("Cloud readiness")]
        [SerializeField, Range(0.5f, 10f)] private float _cloudReadyTimeoutSeconds = 2.5f;

        [Header("Cloud save debounce")]
        [SerializeField, Range(0.05f, 2f)] private float _saveDebounceSeconds = 0.25f;

        private bool _cloudReady;
        private bool _loadRequested;
        private float _cloudWaitTimer;
        private bool _sawSavesDataObject;

        // pending cloud save (если Save пришёл раньше готовности облака)
        private bool _hasPendingSave;
        private string _pendingJson;
        private long _pendingUpdated;
        private bool _pendingFlush;

        // debounce SaveProgress, чтобы не спамить
        private bool _debouncedSaveRequested;
        private float _debounceTimer;

        public bool IsAuthorized => TryResolve() && IsAuthorizedSafe();

        // Облако только при авторизации (гость = без облака)
        public bool CanUseCloud => IsAuthorized;

        // NEW: когда можно безопасно считать, что облачные данные уже загружены (или точно отсутствуют)
        public bool IsCloudReady => !CanUseCloud || _cloudReady;

        public bool HasCloudProfile
        {
            get
            {
                if (!CanUseCloud) return false;
                if (!TryGetCloudFields(out var json, out _)) return false;
                return !string.IsNullOrEmpty(json);
            }
        }

        public bool IsSdkReady
        {
            get
            {
                if (!TryResolve()) return false;

                // На разных версиях YG может называться по-разному.
                if (TryGetStaticBool("SDKEnabled", out var v1)) return v1;
                if (TryGetStaticBool("SDKEnable", out var v2)) return v2;
                if (TryGetStaticBool("SDKInit", out var v3)) return v3;
                if (TryGetStaticBool("InitializedSDK", out var v4)) return v4;

                // если флага нет — считаем "готов", иначе вы никогда не стартанете
                return true;
            }
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            TryResolve();
        }

        private void Update()
        {
            // Отслеживаем готовность облака
            if (!TryResolve())
                return;

            if (!CanUseCloud || !IsSdkReady)
            {
                _cloudReady = false;
                _loadRequested = false;
                _cloudWaitTimer = 0f;
                _sawSavesDataObject = false;
                return;
            }

            if (!_loadRequested)
            {
                _loadRequested = true;
                TryCallLoadProgress(); // запросим загрузку данных из облака
            }

            _cloudWaitTimer += Time.unscaledDeltaTime;

            var savesObj = GetSavesDataObject();
            if (savesObj != null)
                _sawSavesDataObject = true;

            // Считаем облако готовым:
            // - либо мы увидели объект savesData (значит SDK поднял структуру)
            // - либо истёк таймаут (значит данных, вероятно, нет/не придут — но мы не зависаем навсегда)
            if (!_cloudReady)
            {
                if (_sawSavesDataObject)
                    _cloudReady = true;
                else if (_cloudWaitTimer >= Mathf.Max(0.5f, _cloudReadyTimeoutSeconds))
                    _cloudReady = true;
            }

            // Если был pending save и облако стало готово — применяем
            if (_cloudReady && _hasPendingSave)
            {
                if (TrySetCloudFields(_pendingJson, _pendingUpdated))
                {
                    if (_pendingFlush)
                    {
                        TryCallSaveProgressImmediate();
                    }
                    else
                    {
                        RequestDebouncedSaveProgress();
                    }
                }

                _hasPendingSave = false;
                _pendingJson = null;
                _pendingUpdated = 0;
                _pendingFlush = false;
            }

            // Debounce SaveProgress
            if (_debouncedSaveRequested)
            {
                _debounceTimer += Time.unscaledDeltaTime;
                if (_debounceTimer >= Mathf.Max(0.05f, _saveDebounceSeconds))
                {
                    _debouncedSaveRequested = false;
                    _debounceTimer = 0f;
                    TryCallSaveProgressImmediate();
                }
            }
        }

        public long GetServerTimeMsSafe()
        {
            if (TryResolve())
            {
                try
                {
                    var mi = _yandexGameType.GetMethod("ServerTime", BindingFlags.Public | BindingFlags.Static);
                    if (mi != null)
                    {
                        var v = mi.Invoke(null, null);
                        if (v is long l) return l;
                        if (v is int i) return i;
                    }

                    mi = _yandexGameType.GetMethod("GetServerTime", BindingFlags.Public | BindingFlags.Static);
                    if (mi != null)
                    {
                        var v = mi.Invoke(null, null);
                        if (v is long l) return l;
                        if (v is int i) return i;
                    }
                }
                catch { }
            }

            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public bool TryLoadProfileFromCloud(out string json, out long updatedUtcMs)
        {
            json = null;
            updatedUtcMs = 0;

            if (!CanUseCloud) return false;

            // Если облако ещё не готово — не делаем “ложный” вывод “там пусто”
            if (!IsCloudReady) return false;

            return TryGetCloudFields(out json, out updatedUtcMs) && !string.IsNullOrEmpty(json);
        }

        // Совместимость: старый вызов без flush
        public void SaveProfileToCloud(string json, long updatedUtcMs)
        {
            SaveProfileToCloud(json, updatedUtcMs, flush: true);
        }

        // NEW: параметр flush (чтобы компилировались ваши вызовы flush: ...)
        public void SaveProfileToCloud(string json, long updatedUtcMs, bool flush)
        {
            if (!CanUseCloud) return;

            // Если облако ещё не готово — откладываем (иначе можно затереть реальный профиль, который ещё догружается)
            if (!IsCloudReady)
            {
                QueuePendingSave(json, updatedUtcMs, flush);
                return;
            }

            if (!TrySetCloudFields(json, updatedUtcMs))
                return;

            if (flush)
                TryCallSaveProgressImmediate();
            else
                RequestDebouncedSaveProgress();
        }

        // NEW: чтобы компилился ваш HangarScreenController (SaveProfileToCloudBlocking)
        // “best-effort”: ждём готовности облака (или таймаут), затем дергаем SaveProgress.
        public IEnumerator SaveProfileToCloudBlocking(string json, long updatedUtcMs, Action<bool> onDone, float timeoutSeconds = 8f)
        {
            if (!CanUseCloud)
            {
                onDone?.Invoke(false);
                yield break;
            }

            float t = 0f;
            float timeout = Mathf.Max(0.25f, timeoutSeconds);

            // ждём готовности облака
            while (!IsCloudReady && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!IsCloudReady)
            {
                // не удалось дождаться
                QueuePendingSave(json, updatedUtcMs, flush: true);
                onDone?.Invoke(false);
                yield break;
            }

            if (!TrySetCloudFields(json, updatedUtcMs))
            {
                onDone?.Invoke(false);
                yield break;
            }

            TryCallSaveProgressImmediate();

            // у YG нет стабильного публичного ack — просто ждём чуть-чуть
            float wait = 0.25f;
            float w = 0f;
            while (w < wait)
            {
                w += Time.unscaledDeltaTime;
                yield return null;
            }

            onDone?.Invoke(true);
        }

        private void QueuePendingSave(string json, long updatedUtcMs, bool flush)
        {
            // сохраняем самый свежий вариант
            if (!_hasPendingSave || updatedUtcMs >= _pendingUpdated)
            {
                _hasPendingSave = true;
                _pendingJson = json ?? "";
                _pendingUpdated = updatedUtcMs;
                _pendingFlush = flush || _pendingFlush;
            }
        }

        private void RequestDebouncedSaveProgress()
        {
            _debouncedSaveRequested = true;
            _debounceTimer = 0f;
        }

        private bool TryResolve()
        {
            if (_resolved) return _yandexGameType != null;
            _resolved = true;

            _yandexGameType =
                Type.GetType("YG.YandexGame, Assembly-CSharp")
                ?? Type.GetType("YG.YandexGame, Assembly-CSharp-firstpass")
                ?? Type.GetType("YG.YandexGame");

            if (_yandexGameType == null)
            {
                try
                {
                    var asms = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < asms.Length; i++)
                    {
                        var t = asms[i].GetType("YG.YandexGame");
                        if (t != null)
                        {
                            _yandexGameType = t;
                            break;
                        }
                    }
                }
                catch { }
            }

            return _yandexGameType != null;
        }

        private bool IsAuthorizedSafe()
        {
            if (_yandexGameType == null) return false;

            try
            {
                var fi = _yandexGameType.GetField("auth", BindingFlags.Public | BindingFlags.Static);
                if (fi != null && fi.FieldType == typeof(bool))
                    return (bool)fi.GetValue(null);

                var pi = _yandexGameType.GetProperty("auth", BindingFlags.Public | BindingFlags.Static);
                if (pi != null && pi.PropertyType == typeof(bool))
                    return (bool)pi.GetValue(null);

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetStaticBool(string name, out bool value)
        {
            value = false;
            if (_yandexGameType == null) return false;

            try
            {
                var fi = _yandexGameType.GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (fi != null && fi.FieldType == typeof(bool))
                {
                    value = (bool)fi.GetValue(null);
                    return true;
                }

                var pi = _yandexGameType.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                if (pi != null && pi.PropertyType == typeof(bool))
                {
                    value = (bool)pi.GetValue(null);
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryGetCloudFields(out string json, out long updatedUtcMs)
        {
            json = null;
            updatedUtcMs = 0;
            if (_yandexGameType == null) return false;

            try
            {
                var savesObj = GetSavesDataObject();
                if (savesObj == null) return false;

                var st = savesObj.GetType();
                var fJson = st.GetField("GironoidProfileJson", BindingFlags.Public | BindingFlags.Instance);
                var fUpd = st.GetField("GironoidProfileUpdatedUtcMs", BindingFlags.Public | BindingFlags.Instance);

                if (fJson == null || fUpd == null) return false;

                json = fJson.GetValue(savesObj) as string;
                var upd = fUpd.GetValue(savesObj);

                if (upd is long l) updatedUtcMs = l;
                else if (upd is int i) updatedUtcMs = i;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TrySetCloudFields(string json, long updatedUtcMs)
        {
            if (_yandexGameType == null) return false;

            try
            {
                var savesObj = GetSavesDataObject();
                if (savesObj == null) return false;

                var st = savesObj.GetType();
                var fJson = st.GetField("GironoidProfileJson", BindingFlags.Public | BindingFlags.Instance);
                var fUpd = st.GetField("GironoidProfileUpdatedUtcMs", BindingFlags.Public | BindingFlags.Instance);
                if (fJson == null || fUpd == null) return false;

                fJson.SetValue(savesObj, json ?? "");
                fUpd.SetValue(savesObj, updatedUtcMs);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private object GetSavesDataObject()
        {
            if (_yandexGameType == null) return null;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

            var savesField = _yandexGameType.GetField("savesData", flags);
            if (savesField != null)
                return savesField.GetValue(null);

            var savesProp = _yandexGameType.GetProperty("savesData", flags);
            if (savesProp != null && savesProp.CanRead)
                return savesProp.GetValue(null);

            return null;
        }

        private void TryCallLoadProgress()
        {
            if (_yandexGameType == null) return;

            try
            {
                // Частый вариант в YG
                var mi = _yandexGameType.GetMethod("LoadProgress", BindingFlags.Public | BindingFlags.Static);
                if (mi != null && mi.GetParameters().Length == 0)
                {
                    mi.Invoke(null, null);
                    return;
                }

                // Иногда встречается GetData()
                mi = _yandexGameType.GetMethod("GetData", BindingFlags.Public | BindingFlags.Static);
                if (mi != null && mi.GetParameters().Length == 0)
                {
                    mi.Invoke(null, null);
                    return;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void TryCallSaveProgressImmediate()
        {
            if (_yandexGameType == null) return;

            try
            {
                var mi = _yandexGameType.GetMethod("SaveProgress", BindingFlags.Public | BindingFlags.Static);
                if (mi != null)
                {
                    var ps = mi.GetParameters();
                    if (ps.Length == 0)
                    {
                        mi.Invoke(null, null);
                        return;
                    }

                    if (ps.Length == 1 && ps[0].ParameterType == typeof(bool))
                    {
                        mi.Invoke(null, new object[] { true });
                        return;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
