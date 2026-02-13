using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;
using Gironoid._Project.Code.Core.Services;
using Gironoid._Project.Code.Core.Yandex;

namespace Gironoid._Project.Code.Core.Boot
{
    [DisallowMultipleComponent]
    public sealed class GironoidBoot : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private GameConfig _config;

        [Header("Yandex (optional)")]
        [SerializeField] private YandexBridgeBehaviour _yandex;

        [Header("First scene")]
        [SerializeField] private string _startScene = "10_Menu";

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            if (_config == null)
            {
                Debug.LogError("[GironoidBoot] GameConfig not assigned.");
                return;
            }

            if (_yandex == null)
                _yandex = FindObjectOfType<YandexBridgeBehaviour>();

            if (_yandex == null)
            {
                var go = new GameObject("YandexBridge");
                _yandex = go.AddComponent<YandexBridgeBehaviour>();
            }
        }

        private IEnumerator Start()
        {
            // IMPORTANT:
            // Даем YG/SDK шанс инициализироваться и подтянуть облако,
            // иначе вы часто загружаете дефолт и "перетираете" профиль при F5.
            float t = 0f;
            float timeout = 6f;

            // подождем хотя бы кадр, чтобы другие Start/Awake успели отработать
            yield return null;

            while (t < timeout)
            {
                if (_yandex != null && _yandex.IsSdkReady)
                    break;

                t += Time.unscaledDeltaTime;
                yield return null;
            }

            var repo = new ProfileRepository();
            var energy = new EnergyService();
            var profileService = new ProfileService(_config, _yandex, energy, repo);

            // Load profile now (после ожидания SDK)
            profileService.LoadOrCreate();

            var hangar = new HangarService(_config, profileService);

            GironoidApp.Install(_config, profileService, energy, _yandex, hangar);

            if (!string.IsNullOrEmpty(_startScene))
                SceneManager.LoadScene(_startScene);
        }
    }
}
