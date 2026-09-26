using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Rpc.RemoteConfig;

namespace PopupSystem.Tests.EditMode.Fakes
{
    /// <summary>The catalog FakeRemoteConfigApi serves: tests of the reward flows need the same item ids the
    /// production managers hand out.</summary>
    public static class ProductionCatalog
    {
        public static ItemCatalog Catalog { get; } = FakeRemoteConfigApi.CreateDemoConfig().Catalog;
    }
}
