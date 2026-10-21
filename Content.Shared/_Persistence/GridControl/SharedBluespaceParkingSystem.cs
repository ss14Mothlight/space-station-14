using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Containers;

namespace Content.Shared._Persistence.GridControl;

/// <summary>
/// Sets up anchor keys' ID slot and sprite. Ported from SS14-Persistence.
/// </summary>
public abstract partial class SharedBluespaceParkingSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _itemSlots = null!;
    [Dependency] protected SharedAppearanceSystem Appearance = null!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BSPAnchorKeyComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<BSPAnchorKeyComponent, ComponentRemove>((uid, comp, _) => _itemSlots.RemoveItemSlot(uid, comp.PrivilegedIdSlot));
        SubscribeLocalEvent<BSPAnchorKeyComponent, EntInsertedIntoContainerMessage>((uid, comp, _) => OnIdSlotChanged((uid, comp)));
        SubscribeLocalEvent<BSPAnchorKeyComponent, EntRemovedFromContainerMessage>((uid, comp, _) => OnIdSlotChanged((uid, comp)));
    }

    private void OnComponentInit(Entity<BSPAnchorKeyComponent> ent, ref ComponentInit args)
    {
        _itemSlots.AddItemSlot(ent, BSPAnchorKeyComponent.PrivilegedIdCardSlotId, ent.Comp.PrivilegedIdSlot);
        UpdateAppearance(ent);
    }

    protected virtual void OnIdSlotChanged(Entity<BSPAnchorKeyComponent> ent)
    {
        UpdateAppearance(ent);
    }

    protected void UpdateAppearance(Entity<BSPAnchorKeyComponent> ent)
    {
        var hasId = ent.Comp.PrivilegedIdSlot.HasItem;
        var hasGrid = !string.IsNullOrEmpty(ent.Comp.SavedFilename);
        var state = (hasId, hasGrid) switch
        {
            (true, true) => BSPAnchorKeyVisualState.IdStored,
            (true, false) => BSPAnchorKeyVisualState.IdUnstored,
            (false, true) => BSPAnchorKeyVisualState.NoIdStored,
            _ => BSPAnchorKeyVisualState.NoIdUnstored,
        };

        Appearance.SetData(ent, BSPAnchorKeyVisuals.State, state);
    }
}
