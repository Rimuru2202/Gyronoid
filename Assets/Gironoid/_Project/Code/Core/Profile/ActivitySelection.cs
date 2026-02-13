// Assets/Gironoid/_Project/Code/Core/Profile/ActivitySelection.cs
using System;

namespace Gironoid._Project.Code.Core.Profile
{
    public enum ActivityType
    {
        None = 0,
        Level = 1,
        Challenge = 2
    }

    [Serializable]
    public struct ActivitySelection
    {
        public ActivityType Type;
        public string PlanetId;
        public string ActivityId;

        // Alias для совместимости с reflection-кодом, который ищет "Id".
        // Unity JsonUtility не сериализует свойства, поэтому это безопасно.
        public string Id
        {
            get => ActivityId;
            set => ActivityId = value;
        }

        public static ActivitySelection None => new ActivitySelection
        {
            Type = ActivityType.None,
            PlanetId = "",
            ActivityId = ""
        };
    }
}