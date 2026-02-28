// Assets/Gironoid/_Project/Code/Core/Services/AdsServiceBehaviour.cs
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Gironoid._Project.Code.Core.Services
{
    /// <summary>
    /// Единая точка показа рекламы (Interstitial + Rewarded) для экрана магазина.
    /// - Пытается вызвать вашу обвязку YandexBridgeBehaviour через reflection (если она есть).
    /// - Если не находит — пытается вызвать Agava.YandexGames (если плагин установлен) тоже через reflection.
    /// - Если ничего нет — безопасно фейлится и пишет в лог.
    ///
    /// Важно: этот компонент ничего не ломает в вашем API и не требует изменений в существующих файлах.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AdsServiceBehaviour : MonoBehaviour
    {
        [Header("Interstitial cooldown")]
        [SerializeField] private bool _useCooldown = true;
        [SerializeField] private float _minSecondsBetweenInterstitials = 35f;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs = false;

        private float _lastInterstitialRealtime;

        public bool CanShowInterstitial
        {
            get
            {
                if (!_useCooldown) return true;
                return Time.realtimeSinceStartup - _lastInterstitialRealtime >= Mathf.Max(0f, _minSecondsBetweenInterstitials);
            }
        }

        /// <summary>
        /// Показ interstitial. Если показ невозможен — вызовет onError.
        /// </summary>
        public void ShowInterstitial(string placement, Action onClosed, Action<string> onError)
        {
            if (_useCooldown && !CanShowInterstitial)
            {
                onError?.Invoke("interstitial_cooldown");
                return;
            }

            bool started =
                TryShowInterstitialViaUserBridge(placement, onClosed, onError) ||
                TryShowInterstitialViaAgava(placement, onClosed, onError);

            if (!started)
            {
                LogVerbose($"Interstitial NOT available. placement={placement}");
                onError?.Invoke("interstitial_not_available");
                return;
            }

            _lastInterstitialRealtime = Time.realtimeSinceStartup;
            LogVerbose($"Interstitial started. placement={placement}");
        }

        /// <summary>
        /// Показ rewarded. Если показ невозможен — вызовет onError.
        /// </summary>
        public void ShowRewarded(string placement, Action onRewarded, Action onClosed, Action<string> onError)
        {
            bool started =
                TryShowRewardedViaUserBridge(placement, onRewarded, onClosed, onError) ||
                TryShowRewardedViaAgava(placement, onRewarded, onClosed, onError) ||
                TryShowRewardedViaYg2(placement, onRewarded, onClosed, onError);

            if (!started)
            {
                LogVerbose($"Rewarded NOT available. placement={placement}");
                onError?.Invoke("rewarded_not_available");
            }
            else
            {
                LogVerbose($"Rewarded started. placement={placement}");
            }
        }

        // -------------------------- Your bridge (YandexBridgeBehaviour) via reflection --------------------------

        private bool TryShowInterstitialViaUserBridge(string placement, Action onClosed, Action<string> onError)
        {
            object bridge = FindBridgeInstance();
            if (bridge == null) return false;

            Type t = bridge.GetType();

            // Популярные варианты имён методов
            string[] candidates = new[]
            {
                "ShowInterstitial",
                "ShowInterstitialAd",
                "ShowFullscreenAd",
                "ShowFullScreenAd",
                "ShowInterstitialAdv"
            };

            foreach (string name in candidates)
            {
                MethodInfo[] methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == name)
                    .ToArray();

                if (methods.Length == 0) continue;

                foreach (MethodInfo m in methods)
                {
                    if (TryInvokeBridgeMethod(bridge, m, placement, onClosed, null, onError))
                        return true;
                }
            }

            return false;
        }

        private bool TryShowRewardedViaUserBridge(string placement, Action onRewarded, Action onClosed, Action<string> onError)
        {
            object bridge = FindBridgeInstance();
            if (bridge == null) return false;

            Type t = bridge.GetType();

            string[] candidates = new[]
            {
                "ShowRewarded",
                "ShowRewardedAd",
                "ShowVideoAd",
                "ShowRewardedVideo",
                "ShowRewardedAdv"
            };

            foreach (string name in candidates)
            {
                MethodInfo[] methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == name)
                    .ToArray();

                if (methods.Length == 0) continue;

                foreach (MethodInfo m in methods)
                {
                    if (TryInvokeBridgeMethod(bridge, m, placement, onClosed, onRewarded, onError))
                        return true;
                }
            }

            return false;
        }

        private object FindBridgeInstance()
        {
            // Ищем объект в сцене по имени типа "YandexBridgeBehaviour"
            // (ваш проект уже упоминал этот класс).
            MonoBehaviour[] all = FindObjectsOfType<MonoBehaviour>(true);
            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour mb = all[i];
                if (mb == null) continue;

                Type t = mb.GetType();
                if (t.Name == "YandexBridgeBehaviour")
                {
                    return mb;
                }
            }

            return null;
        }

        /// <summary>
        /// Универсальная попытка вызвать метод bridge-а с разными сигнатурами:
        /// - может принимать placement (string) или не принимать
        /// - может принимать onClosed (Action)
        /// - для rewarded может принимать onRewarded (Action)
        /// - может принимать onError (Action&lt;string&gt;)
        /// </summary>
        private bool TryInvokeBridgeMethod(object target, MethodInfo method, string placement, Action onClosed, Action onRewarded, Action<string> onError)
        {
            ParameterInfo[] ps = method.GetParameters();
            object[] args = new object[ps.Length];
            bool rewardedMode = onRewarded != null;
            int actionOrdinal = 0;
            bool rewardAssigned = false;

            for (int i = 0; i < ps.Length; i++)
            {
                Type pType = ps[i].ParameterType;
                string pName = ps[i].Name ?? string.Empty;

                if (pType == typeof(string))
                {
                    args[i] = placement;
                    continue;
                }

                if (pType == typeof(Action))
                {
                    Action mapped = null;

                    if (rewardedMode)
                    {
                        if (LooksLikeRewardCallbackName(pName))
                        {
                            mapped = onRewarded;
                            rewardAssigned = true;
                        }
                        else if (LooksLikeCloseCallbackName(pName))
                        {
                            mapped = onClosed;
                        }
                        else if (LooksLikeOpenCallbackName(pName))
                        {
                            mapped = null;
                        }
                        else if (actionOrdinal == 1 && !rewardAssigned)
                        {
                            // Conservative fallback: reward only on the 2nd Action (typical: onOpen, onRewarded, onClose).
                            mapped = onRewarded;
                            rewardAssigned = true;
                        }
                        else
                        {
                            mapped = onClosed;
                        }
                    }
                    else
                    {
                        mapped = LooksLikeOpenCallbackName(pName) ? null : onClosed;
                    }

                    args[i] = mapped;
                    actionOrdinal++;
                    continue;
                }

                if (pType == typeof(Action<string>))
                {
                    args[i] = onError;
                    continue;
                }

                // Иногда встречаются коллбеки типа Action<bool> или кастомные делегаты.
                // В этом модуле мы не "угадываем" их — оставляем null, чтобы не упасть по несовместимости.
                args[i] = null;
            }

            try
            {
                method.Invoke(target, args);
                return true;
            }
            catch (Exception e)
            {
                LogVerbose($"Bridge invoke failed: {method.DeclaringType?.Name}.{method.Name} => {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        private static bool LooksLikeRewardCallbackName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("reward", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeCloseCallbackName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("done", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("finish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("complete", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeOpenCallbackName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("show", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // -------------------------- Agava.YandexGames via reflection --------------------------

        private bool TryShowInterstitialViaAgava(string placement, Action onClosed, Action<string> onError)
        {
            // Agava.YandexGames.InterstitialAd.Show(...)
            Type t = ResolveType(new[]
            {
                "Agava.YandexGames.InterstitialAd, Agava.YandexGames",
                "Agava.YandexGames.InterstitialAd, Agava.YandexGames.Sdk",
                "Agava.YandexGames.InterstitialAd, Agava.YandexGames.Plugin"
            });

            if (t == null) return false;

            MethodInfo[] shows = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Show")
                .ToArray();

            if (shows.Length == 0) return false;

            // Поддержим разные сигнатуры: (Action onOpen, Action onClose, Action<string> onError, Action onOffline) и похожие.
            foreach (MethodInfo m in shows)
            {
                if (TryInvokeAgavaShow(m, placement, onClosed, null, onError))
                    return true;
            }

            return false;
        }

        private bool TryShowRewardedViaAgava(string placement, Action onRewarded, Action onClosed, Action<string> onError)
        {
            // Agava.YandexGames.VideoAd.Show(...)
            Type t = ResolveType(new[]
            {
                "Agava.YandexGames.VideoAd, Agava.YandexGames",
                "Agava.YandexGames.VideoAd, Agava.YandexGames.Sdk",
                "Agava.YandexGames.VideoAd, Agava.YandexGames.Plugin"
            });

            if (t == null) return false;

            MethodInfo[] shows = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Show")
                .ToArray();

            if (shows.Length == 0) return false;

            foreach (MethodInfo m in shows)
            {
                if (TryInvokeAgavaShow(m, placement, onClosed, onRewarded, onError))
                    return true;
            }

            return false;
        }

        // -------------------------- PluginYG2 (YG.YG2) via reflection --------------------------

        private bool TryShowRewardedViaYg2(string placement, Action onRewarded, Action onClosed, Action<string> onError)
        {
            Type yg2Type = ResolveType(new[]
            {
                "YG.YG2, Assembly-CSharp",
                "YG.YG2, Assembly-CSharp-firstpass",
                "YG.YG2"
            });

            if (yg2Type == null)
                return false;

            string rewardId = string.IsNullOrWhiteSpace(placement) ? "shop_reward_tokens" : placement.Trim();

            bool rewardedIssued = false;
            Action issueRewardOnce = () =>
            {
                if (rewardedIssued) return;
                rewardedIssued = true;
                onRewarded?.Invoke();
            };

            Action onOpenRewarded = null;
            Action onCloseRewarded = null;
            Action onErrorRewarded = null;
            Action<string> onRewardAdv = null;

            bool cleaned = false;
            void Cleanup()
            {
                if (cleaned) return;
                cleaned = true;

                TryRemoveStaticAction(yg2Type, "onOpenRewardedAdv", onOpenRewarded);
                TryRemoveStaticAction(yg2Type, "onCloseRewardedAdv", onCloseRewarded);
                TryRemoveStaticAction(yg2Type, "onErrorRewardedAdv", onErrorRewarded);
                TryRemoveStaticActionString(yg2Type, "onRewardAdv", onRewardAdv);
            }

            onOpenRewarded = () => { };
            onCloseRewarded = () =>
            {
                onClosed?.Invoke();
                Cleanup();
            };
            onErrorRewarded = () =>
            {
                onError?.Invoke("rewarded_error");
                Cleanup();
            };
            onRewardAdv = id =>
            {
                if (!string.IsNullOrEmpty(rewardId) &&
                    !string.IsNullOrEmpty(id) &&
                    !string.Equals(id, rewardId, StringComparison.Ordinal))
                {
                    return;
                }

                issueRewardOnce();
            };

            TryAddStaticAction(yg2Type, "onOpenRewardedAdv", onOpenRewarded);
            TryAddStaticAction(yg2Type, "onCloseRewardedAdv", onCloseRewarded);
            TryAddStaticAction(yg2Type, "onErrorRewardedAdv", onErrorRewarded);
            TryAddStaticActionString(yg2Type, "onRewardAdv", onRewardAdv);

            if (!TryInvokeYg2RewardedShow(yg2Type, rewardId, issueRewardOnce))
            {
                Cleanup();
                return false;
            }

            return true;
        }

        private bool TryInvokeYg2RewardedShow(Type yg2Type, string rewardId, Action onRewarded)
        {
            var shows = yg2Type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "RewardedAdvShow")
                .ToArray();

            if (shows.Length == 0)
                return false;

            for (int i = 0; i < shows.Length; i++)
            {
                var m = shows[i];
                var ps = m.GetParameters();
                if (ps.Length == 2 &&
                    ps[0].ParameterType == typeof(string) &&
                    ps[1].ParameterType == typeof(Action))
                {
                    try
                    {
                        m.Invoke(null, new object[] { rewardId, onRewarded });
                        return true;
                    }
                    catch (Exception e)
                    {
                        LogVerbose($"YG2 rewarded invoke failed (with callback): {e.GetType().Name}: {e.Message}");
                    }
                }
            }

            for (int i = 0; i < shows.Length; i++)
            {
                var m = shows[i];
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                {
                    try
                    {
                        m.Invoke(null, new object[] { rewardId });
                        return true;
                    }
                    catch (Exception e)
                    {
                        LogVerbose($"YG2 rewarded invoke failed: {e.GetType().Name}: {e.Message}");
                    }
                }
            }

            return false;
        }

        private static bool TryAddStaticAction(Type type, string fieldName, Action handler)
        {
            if (type == null || handler == null || string.IsNullOrEmpty(fieldName))
                return false;

            try
            {
                var f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                if (f == null || f.FieldType != typeof(Action))
                    return false;

                var current = (Action)f.GetValue(null);
                current += handler;
                f.SetValue(null, current);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryRemoveStaticAction(Type type, string fieldName, Action handler)
        {
            if (type == null || handler == null || string.IsNullOrEmpty(fieldName))
                return;

            try
            {
                var f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                if (f == null || f.FieldType != typeof(Action))
                    return;

                var current = (Action)f.GetValue(null);
                if (current == null) return;
                current -= handler;
                f.SetValue(null, current);
            }
            catch
            {
                // ignore
            }
        }

        private static bool TryAddStaticActionString(Type type, string fieldName, Action<string> handler)
        {
            if (type == null || handler == null || string.IsNullOrEmpty(fieldName))
                return false;

            try
            {
                var f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                if (f == null || f.FieldType != typeof(Action<string>))
                    return false;

                var current = (Action<string>)f.GetValue(null);
                current += handler;
                f.SetValue(null, current);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryRemoveStaticActionString(Type type, string fieldName, Action<string> handler)
        {
            if (type == null || handler == null || string.IsNullOrEmpty(fieldName))
                return;

            try
            {
                var f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                if (f == null || f.FieldType != typeof(Action<string>))
                    return;

                var current = (Action<string>)f.GetValue(null);
                if (current == null) return;
                current -= handler;
                f.SetValue(null, current);
            }
            catch
            {
                // ignore
            }
        }

        private bool TryInvokeAgavaShow(MethodInfo showMethod, string placement, Action onClosed, Action onRewarded, Action<string> onError)
        {
            ParameterInfo[] ps = showMethod.GetParameters();
            object[] args = new object[ps.Length];
            bool rewardedMode = onRewarded != null;
            int actionOrdinal = 0;

            // На практике у Agava чаще всего:
            // Interstitial: Show(Action onOpen, Action onClose, Action<string> onError, Action onOffline)
            // Rewarded:     Show(Action onOpen, Action onRewarded, Action onClose, Action<string> onError)
            // Здесь используем строгий порядок и никогда не маппим награду на onOpen.
            for (int i = 0; i < ps.Length; i++)
            {
                Type pType = ps[i].ParameterType;

                if (pType == typeof(Action))
                {
                    if (!rewardedMode)
                    {
                        // Interstitial: [0]=onOpen, [1]=onClose, [2+]=ignored
                        args[i] = actionOrdinal == 1 ? onClosed : null;
                    }
                    else
                    {
                        // Rewarded: [0]=onOpen, [1]=onRewarded, [2]=onClose, [3+]=ignored
                        if (actionOrdinal == 1) args[i] = onRewarded;
                        else if (actionOrdinal == 2) args[i] = onClosed;
                        else args[i] = null;
                    }

                    actionOrdinal++;

                    continue;
                }

                if (pType == typeof(Action<string>))
                {
                    args[i] = onError;
                    continue;
                }

                if (pType == typeof(string))
                {
                    // если вдруг у кого-то placement прокинут строкой
                    args[i] = placement;
                    continue;
                }

                args[i] = null;
            }

            try
            {
                showMethod.Invoke(null, args);
                return true;
            }
            catch (Exception e)
            {
                LogVerbose($"Agava invoke failed: {showMethod.DeclaringType?.Name}.{showMethod.Name} => {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        private static Type ResolveType(string[] assemblyQualifiedNames)
        {
            for (int i = 0; i < assemblyQualifiedNames.Length; i++)
            {
                Type t = Type.GetType(assemblyQualifiedNames[i], false);
                if (t != null) return t;
            }
            return null;
        }

        private void LogVerbose(string msg)
        {
            if (_verboseLogs)
                Debug.Log($"[AdsServiceBehaviour] {msg}", this);
        }
    }
}
