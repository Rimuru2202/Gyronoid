using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;

namespace Gironoid._Project.Code.Core.Services
{
    public sealed class HangarService
    {
        private readonly GameConfig _cfg;
        private readonly ProfileService _profile;

        public HangarService(GameConfig cfg, ProfileService profile)
        {
            _cfg = cfg;
            _profile = profile;
        }

        public bool EnsureStarterKit(bool flushToServer = false)
        {
            if (_profile == null || _profile.Profile == null) return false;

            var changed = false;

            _profile.Apply(p =>
            {
                changed = HangarLogic.EnsureStarterKit(p, _cfg);
            }, flushToServer);

            return changed;
        }

        public HangarLogic.Result PurchaseShip(string shipId, int costTokens, bool flushToServer = false)
        {
            if (_profile == null || _profile.Profile == null) return HangarLogic.Result.Fail("Профиль не готов.");

            HangarLogic.Result result = default;

            _profile.Apply(p =>
            {
                result = HangarLogic.TryPurchaseShip(p, _cfg, shipId, costTokens);
            }, flushToServer);

            return result;
        }

        public HangarLogic.Result CraftItem(string definitionId, int ironCost, int tokenCost, out ItemInstance crafted, bool flushToServer = false)
        {
            crafted = null;
            if (_profile == null || _profile.Profile == null) return HangarLogic.Result.Fail("Профиль не готов.");

            HangarLogic.Result result = default;
            ItemInstance local = null;

            _profile.Apply(p =>
            {
                result = HangarLogic.TryCraftItem(
                    p,
                    _cfg,
                    definitionId,
                    ironCost,
                    tokenCost,
                    out local,
                    successChanceOverride: -1f);
            }, flushToServer);

            crafted = local;
            return result;
        }

        public HangarLogic.Result CraftItemWithChanceOverride(
            string definitionId,
            int ironCost,
            int tokenCost,
            float successChanceOverride,
            out ItemInstance crafted,
            bool flushToServer = false)
        {
            crafted = null;
            if (_profile == null || _profile.Profile == null) return HangarLogic.Result.Fail("Профиль не готов.");

            HangarLogic.Result result = default;
            ItemInstance local = null;

            _profile.Apply(p =>
            {
                result = HangarLogic.TryCraftItem(
                    p,
                    _cfg,
                    definitionId,
                    ironCost,
                    tokenCost,
                    out local,
                    successChanceOverride: successChanceOverride);
            }, flushToServer);

            crafted = local;
            return result;
        }

        public HangarLogic.Result Dismantle(string instanceId, out HangarLogic.DismantleGain gain, bool flushToServer = false)
        {
            gain = default;
            if (_profile == null || _profile.Profile == null) return HangarLogic.Result.Fail("Профиль не готов.");

            HangarLogic.Result result = default;
            HangarLogic.DismantleGain localGain = default;

            _profile.Apply(p =>
            {
                result = HangarLogic.TryDismantle(p, _cfg, instanceId, out localGain);
            }, flushToServer);

            gain = localGain;
            return result;
        }

        public HangarLogic.Result Equip(string shipId, int slotIndex, ItemType type, string instanceId, bool flushToServer = false)
        {
            if (_profile == null || _profile.Profile == null) return HangarLogic.Result.Fail("Профиль не готов.");

            HangarLogic.Result result = default;

            _profile.Apply(p =>
            {
                result = HangarLogic.TryEquip(p, _cfg, shipId, slotIndex, type, instanceId);
            }, flushToServer);

            return result;
        }

        // Совместимая перегрузка (если UI вызывал Equip с другим порядком параметров)
        public HangarLogic.Result Equip(string shipId, ItemType type, int slotIndex, string instanceId)
            => Equip(shipId, slotIndex, type, instanceId, flushToServer: false);
    }
}
