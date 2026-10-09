using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Persistence.GridControl;

/// <summary>
/// Sets up the ID slots of the faction grid tools.
/// </summary>
public sealed partial class SharedGridControlSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _itemSlots = null!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridConfigComponent, ComponentInit>((uid, comp, _) =>
            _itemSlots.AddItemSlot(uid, GridConfigComponent.PrivilegedIdCardSlotId, comp.PrivilegedIdSlot));
        SubscribeLocalEvent<GridConfigComponent, ComponentRemove>((uid, comp, _) =>
            _itemSlots.RemoveItemSlot(uid, comp.PrivilegedIdSlot));

        SubscribeLocalEvent<StationCreatorComponent, ComponentInit>((uid, comp, _) =>
            _itemSlots.AddItemSlot(uid, StationCreatorComponent.PrivilegedIdCardSlotId, comp.PrivilegedIdSlot));
        SubscribeLocalEvent<StationCreatorComponent, ComponentRemove>((uid, comp, _) =>
            _itemSlots.RemoveItemSlot(uid, comp.PrivilegedIdSlot));

        SubscribeLocalEvent<StationTaggerComponent, ComponentInit>((uid, comp, _) =>
            _itemSlots.AddItemSlot(uid, StationTaggerComponent.PrivilegedIdCardSlotId, comp.PrivilegedIdSlot));
        SubscribeLocalEvent<StationTaggerComponent, ComponentRemove>((uid, comp, _) =>
            _itemSlots.RemoveItemSlot(uid, comp.PrivilegedIdSlot));
    }
}
