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
                TryShowRewardedViaAgava(placement, onRewarded, onClosed, onError);

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

            for (int i = 0; i < ps.Length; i++)
            {
                Type pType = ps[i].ParameterType;

                if (pType == typeof(string))
                {
                    args[i] = placement;
                    continue;
                }

                if (pType == typeof(Action))
                {
                    // heuristic: если rewarded есть и мы ещё не вставляли onRewarded — вставим его первым Action,
                    // а onClosed — вторым. Иначе — onClosed.
                    if (onRewarded != null && !ArgsContainsAction(args, onRewarded) && !ArgsContainsAnyAction(args))
                    {
                        args[i] = onRewarded;
                    }
                    else if (onRewarded != null && !ArgsContainsAction(args, onClosed) && ArgsContainsAction(args, onRewarded))
                    {
                        args[i] = onClosed;
                    }
                    else
                    {
                        args[i] = onClosed;
                    }
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

        private static bool ArgsContainsAnyAction(object[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is Action) return true;
            }
            return false;
        }

        private static bool ArgsContainsAction(object[] args, Action a)
        {
            if (a == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (ReferenceEquals(args[i], a)) return true;
            }
            return false;
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

        private bool TryInvokeAgavaShow(MethodInfo showMethod, string placement, Action onClosed, Action onRewarded, Action<string> onError)
        {
            ParameterInfo[] ps = showMethod.GetParameters();
            object[] args = new object[ps.Length];

            // На практике у Agava чаще всего:
            // Interstitial: Show(Action onOpen, Action onClose, Action<string> onError, Action onOffline)
            // Rewarded:     Show(Action onOpen, Action onRewarded, Action onClose, Action<string> onError)
            // Но мы не "знаем" наверняка — поэтому подставляем только совместимые типы.
            for (int i = 0; i < ps.Length; i++)
            {
                Type pType = ps[i].ParameterType;

                if (pType == typeof(Action))
                {
                    // Эвристика: если rewarded задан — второй Action обычно onRewarded
                    // Но мы не можем гарантировать, поэтому:
                    // - если onRewarded ещё не вставили и это не первый Action -> вставим onRewarded
                    // - иначе onClosed
                    if (onRewarded != null && !ArgsContainsAction(args, onRewarded) && i > 0)
                        args[i] = onRewarded;
                    else
                        args[i] = onClosed;

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
