using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;
using Gironoid._Project.Code.Core.Yandex;

namespace Gironoid._Project.Code.Core.Services
{
    public static class GironoidApp
    {
        public static GameConfig Config { get; private set; }
        public static ProfileService ProfileService { get; private set; }
        public static EnergyService Energy { get; private set; }
        public static YandexBridgeBehaviour Yandex { get; private set; }
        public static HangarService Hangar { get; private set; }

        public static PlayerProfile Profile => ProfileService != null ? ProfileService.Profile : null;

        public static void Install(GameConfig config, ProfileService profileService, EnergyService energy, YandexBridgeBehaviour yandex, HangarService hangar)
        {
            Config = config;
            ProfileService = profileService;
            Energy = energy;
            Yandex = yandex;
            Hangar = hangar;
        }

        public static bool IsReady =>
            Config != null &&
            ProfileService != null &&
            Profile != null &&
            Energy != null &&
            Yandex != null;
    }
}