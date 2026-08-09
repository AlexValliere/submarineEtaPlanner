using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace SubmarineEtaPlanner.Fuel;

internal sealed unsafe class DalamudGameFuelInventoryReader : IGameFuelInventoryReader
{
    private const uint CeruleumTankItemId = 10155;

    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IFramework framework;

    public DalamudGameFuelInventoryReader(
        IClientState clientState,
        IPlayerState playerState,
        IFramework framework)
    {
        this.clientState = clientState;
        this.playerState = playerState;
        this.framework = framework;
    }

    public CurrentCharacterFuelData? TryRead()
    {
        if (!this.framework.IsInFrameworkUpdateThread)
            throw new InvalidOperationException("Character fuel must be read on the framework thread.");

        if (!this.clientState.IsLoggedIn || !this.playerState.IsLoaded)
            return null;

        var characterId = this.playerState.ContentId;
        if (characterId == 0)
            return null;

        var homeWorld = this.playerState.HomeWorld;
        if (!homeWorld.IsValid)
            return null;

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager is null)
            return null;

        var freeCompanyProxy = InfoProxyFreeCompany.Instance();
        if (freeCompanyProxy is null || freeCompanyProxy->Id == 0)
            return null;

        var ceruleumTanks = inventoryManager->GetInventoryItemCount(
            CeruleumTankItemId,
            isHq: false,
            checkEquipped: false,
            checkArmory: false);
        if (ceruleumTanks < 0)
            throw new InvalidOperationException("The inventory manager returned an invalid item count.");

        return new CurrentCharacterFuelData(
            characterId,
            freeCompanyProxy->Id,
            this.playerState.CharacterName,
            homeWorld.Value.Name.ExtractText(),
            ceruleumTanks);
    }
}
