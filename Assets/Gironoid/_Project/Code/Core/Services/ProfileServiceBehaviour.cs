using UnityEngine;
using Gironoid._Project.Code.Core;
using Gironoid._Project.Code.Core.Profile;
using Gironoid._Project.Code.Core.Services;

namespace Gironoid._Project.Code.Core.Services
{
    /// <summary>
    /// Лёгкая MonoBehaviour-обёртка для доступа к ProfileService из инспектора/сцены.
    /// НЕ создаёт сервис, просто проксирует GironoidApp.ProfileService.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProfileServiceBehaviour : MonoBehaviour
    {
        public ProfileService Service => GironoidApp.ProfileService;
        public PlayerProfile Profile => GironoidApp.Profile;
    }
}