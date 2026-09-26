namespace PopupSystem.Game.Domain.Inventory
{
    /// <summary>Ids of the demo catalog's items. The daily reward and the offer grant them, and the fake remote
    /// config defines them, so they live here rather than on the fake: gameplay code must not depend on any
    /// <c>Fake*</c> type (Docs/feature-maps/game-services.md). A real remote config must keep serving these ids,
    /// or the two rewards must move to config too.</summary>
    public static class DemoItemIds
    {
        public const string Sword = "sword";
        public const string HealthPotion = "health-potion";
        public const string Arrows = "arrows";
        public const string Chest = "chest";
        public const string Ore = "ore";
    }
}
