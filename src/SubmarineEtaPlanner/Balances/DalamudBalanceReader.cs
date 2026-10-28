using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SubmarineEtaPlanner.Balances;

/// <summary>Copies scalar observations only while the corresponding native data is ready.</summary>
internal sealed unsafe class DalamudBalanceReader(
    IClientState clientState,
    IPlayerState playerState,
    IFramework framework,
    IGameGui gameGui) : ICurrentBalanceReader
{
    public BalanceReadResult? TryRead(DateTimeOffset now, ICollection<string> warnings)
    {
        if (!framework.IsInFrameworkUpdateThread)
            throw new InvalidOperationException("Gil balances must be read on the framework thread.");

        try
        {
            if (!clientState.IsLoggedIn || !playerState.IsLoaded || playerState.ContentId == 0)
                return null;
            var homeWorld = playerState.HomeWorld;
            var currentWorld = playerState.CurrentWorld;
            var localPlayer = Control.GetLocalPlayer();
            if (!homeWorld.IsValid || localPlayer is null || localPlayer->EntityId != playerState.EntityId)
                return null;
            var inventory = InventoryManager.Instance();
            if (inventory is null) return null;
            var currency = inventory->GetInventoryContainer(InventoryType.Currency);
            if (currency is null || !currency->IsLoaded || currency->Items is null || currency->Size <= 0)
                return null;
            var walletGil = inventory->GetInventoryItemCount(1, isHq: false, checkEquipped: false, checkArmory: false);
            if (walletGil < 0) return null;

            var proxy = InfoProxyFreeCompany.Instance();
            var members = InfoProxyFreeCompanyMember.Instance();
            // Keep only scalar copies, including the inputs rejected by the association checks.
            var association = new BalanceAssociationDiagnostics(
                homeWorld.RowId,
                currentWorld.IsValid ? currentWorld.RowId : null,
                localPlayer->HomeWorld,
                localPlayer->CurrentWorld,
                !string.IsNullOrEmpty(localPlayer->FreeCompanyTagString),
                proxy is null ? null : proxy->Id,
                proxy is null ? null : proxy->HomeWorldId,
                proxy is not null && !string.IsNullOrEmpty(proxy->NameString),
                members is null ? null : members->FreeCompanyId);
            // The FC proxy can retain or omit data while travelling; those states must not move a wallet.
            var (freeCompanyId, _) = association.Resolve();

            var character = new CharacterGilObservation(playerState.ContentId, freeCompanyId,
                playerState.CharacterName, homeWorld.Value.Name.ExtractText(), walletGil,
                now.ToUniversalTime(), IsLive: true);
            var chest = freeCompanyId is > 0 ? ReadReadyChest(inventory, freeCompanyId.Value, now) : null;
            return new(character, chest, association);
        }
        catch (Exception)
        {
            JsonBalanceObservationStore.Warn(warnings, "The current character wallet or FC chest gil could not be read.");
            return null;
        }
    }

    private FcChestGilObservation? ReadReadyChest(InventoryManager* inventory, ulong freeCompanyId, DateTimeOffset now)
    {
        var addon = gameGui.GetAddonByName<AtkUnitBase>("FreeCompanyChest");
        if (addon is null || !addon->IsVisible || addon->UldManager.LoadedState != AtkLoadState.Loaded ||
            !addon->IsFullyLoaded() || addon->AtkValues is null || addon->AtkValuesCount <= 10 ||
            addon->AtkValues[10].Type != AtkValueType.UInt || addon->AtkValues[10].UInt != 0)
            return null;

        var container = inventory->GetInventoryContainer(InventoryType.FreeCompanyGil);
        if (container is null || !container->IsLoaded || container->Items is null || container->Size is <= 0 or > 64)
            return null;

        long gil = 0;
        for (var slot = 0; slot < container->Size; slot++)
        {
            var item = container->GetInventorySlot(slot);
            if (item is null) return null;
            if (item->ItemId == 1) gil += item->Quantity;
        }
        if (gil > uint.MaxValue) return null;
        // Even a positive cached balance is not a fresh observation when the chest is closed.
        return new(freeCompanyId, gil, now.ToUniversalTime());
    }
}
