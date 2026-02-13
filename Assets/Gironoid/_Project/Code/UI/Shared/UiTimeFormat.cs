namespace Gironoid._Project.Code.UI.Shared
{
    public static class UiTimeFormat
    {
        public static string MsToMmSs(long ms)
        {
            if (ms <= 0) return "00:00";
            var totalSec = (int)(ms / 1000);
            var m = totalSec / 60;
            var s = totalSec - (m * 60);
            if (m > 99) m = 99;
            return (m < 10 ? "0" : "") + m + ":" + (s < 10 ? "0" : "") + s;
        }
    }
}