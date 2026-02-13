using UnityEngine;
using Gironoid._Project.Code.Core;
using Gironoid._Project.Code.Core.Services;
namespace Gironoid._Project.Code.UI.Shared
{
    [DisallowMultipleComponent]
    public sealed class ProfileServiceTicker : MonoBehaviour
    {
        [SerializeField] private bool _useUnscaledTime = true;

        // Защита от гигантских dt (вкладка была свернута/подлагало WebGL).
        [SerializeField, Range(0.05f, 1.0f)]
        private float _maxDeltaSeconds = 0.33f;

        private void Update()
        {
            if (!GironoidApp.IsReady) return;

            var svc = GironoidApp.ProfileService;
            if (svc == null) return;

            var dt = _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (dt < 0f) dt = 0f;
            if (_maxDeltaSeconds > 0f && dt > _maxDeltaSeconds) dt = _maxDeltaSeconds;

            svc.Tick(dt);
        }
    }
}