using System.Collections.Generic;
using Godot;
using ZombieSandbox.Combat;
using ZombieSandbox.Core.Input;
using ZombieSandbox.Player;

namespace ZombieSandbox.Inventory;

/// <summary>
/// The Tarkov-style inventory screen (Tab / gamepad View). The game keeps running underneath and the
/// player can still walk. Three panels: gear slots, storage grids (rig, pockets, backpack), and the
/// ground around you.
/// Mouse: drag &amp; drop, R rotates while dragging, Ctrl+click quick-moves, Delete or dragging off
/// the grids drops. Gamepad: d-pad cursor, A pick up / place, X rotate (or quick-move when empty-handed),
/// LB/RB switch panels, B cancel / close.
/// Draws everything itself and reads input in _Input so nothing leaks to the game while it's open.
/// </summary>
public partial class InventoryScreen : Control
{
    private const float GroundRadius = 2.5f;
    private const int GroundWidth = 6;
    private const int GroundHeight = 10;

    private enum AreaKind { Grid, Slot, Ground }

    /// <summary>A clickable region: a grid (cells) or a gear slot (one box).</summary>
    private sealed class Area
    {
        public AreaKind Kind;
        public ItemGrid? Grid;
        public GearSlot Slot;
        public Rect2 Rect;
        public int Panel;
        public int Columns => Grid?.Width ?? 1;
        public int Rows => Grid?.Height ?? 1;
    }

    private PlayerController _player = null!;
    private CharacterInventory _inventory = null!;
    private Font _font = null!;
    private float _cell = 48f;

    private readonly List<Area> _areas = new();
    private readonly List<(string Text, Vector2 Position)> _titles = new();
    private readonly List<Rect2> _panels = new();
    private Rect2 _weaponRect;

    // The ground panel: a virtual grid of what lies around you.
    private readonly ItemGrid _groundGrid = new(GroundWidth, GroundHeight);
    private ItemGrid _ground = null!;
    private readonly Dictionary<Item, Node> _groundSources = new();
    private float _groundRefresh;

    // The item in hand (being moved).
    private Item? _held;
    private bool _heldRotated;
    private Vector2I _grabOffset;
    private AreaKind _originKind;
    private ItemGrid? _originGrid;
    private Vector2I _originCell;
    private bool _originRotated;
    private GearSlot _originSlot;
    private bool _mouseDragging;

    // Gamepad cursor.
    private int _cursorArea;
    private Vector2I _cursorCell;

    private bool _wasOpen;

    public override void _Ready()
    {
        _player = (PlayerController)Owner; // (Its inventory is looked up in _Process: the player readies after us.)
        _font = ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        _ground = _groundGrid;
    }

    private bool UsingGamepad => GameInput.Instance.LastDevice == InputDevice.Gamepad;

    public override void _Process(double delta)
    {
        _inventory = _player.Inventory;
        bool open = _player.InventoryOpen && !_player.IsDead;
        if (open != _wasOpen)
        {
            _wasOpen = open;
            if (open)
                OnOpened();
            else
                OnClosed();
        }
        Visible = open;
        if (!open)
            return;
        // Mouse pointer only when using the mouse.
        Input.MouseModeEnum wanted = UsingGamepad ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != wanted)
            Input.MouseMode = wanted;

        _groundRefresh -= (float)delta;
        if (_groundRefresh <= 0f && (_held == null || _originKind != AreaKind.Ground))
            RefreshGround();
        Layout();
        QueueRedraw();
    }

    private void OnOpened()
    {
        RefreshGround();
        Layout();
        _cursorArea = 0;
        _cursorCell = Vector2I.Zero;
    }

    private void OnClosed()
    {
        if (_held != null)
            ReturnHeld();
        _mouseDragging = false;
    }

    // ---------- Ground ----------

    private void RefreshGround()
    {
        _groundRefresh = 0.5f;
        _groundGrid.Clear();
        _groundSources.Clear();

        Vector3 here = _player.GlobalPosition;
        foreach (Node node in GetTree().GetNodesInGroup(WorldItem.Group))
        {
            if (node is WorldItem { Item: { } item } world && !world.IsQueuedForDeletion()
                && world.GlobalPosition.DistanceTo(here) <= GroundRadius && PlaceOnGround(item))
                _groundSources[item] = world;
        }
        if (_inventory.MagazineData != null)
        {
            foreach (Node node in GetTree().GetNodesInGroup(DroppedMagazine.Group))
            {
                if (node is DroppedMagazine { Magazine: { } magazine } dropped && !dropped.IsQueuedForDeletion()
                    && dropped.GlobalPosition.DistanceTo(here) <= GroundRadius)
                {
                    Item item = Item.FromMagazine(_inventory.MagazineData, magazine);
                    if (PlaceOnGround(item))
                        _groundSources[item] = dropped;
                }
            }
        }
    }

    private bool PlaceOnGround(Item item) =>
        _groundGrid.FindSpot(item, out int x, out int y, out bool rotated) && _groundGrid.TryPlace(item, x, y, rotated);

    /// <summary>Takes an item that's on the ground out of the world.</summary>
    private void TakeFromWorld(Item item)
    {
        if (!_groundSources.Remove(item, out Node? source))
            return;
        if (source is WorldItem world)
            world.Take();
        else if (source is DroppedMagazine dropped)
            dropped.Take();
    }

    private void DropInWorld(Item item)
    {
        Vector3 forward = -_player.GlobalBasis.Z;
        Vector3 position = _player.GlobalPosition + Vector3.Up * 1.0f + forward * 0.5f;
        WorldItem.Spawn(GetTree().CurrentScene, item, position, forward * 1.5f + Vector3.Up * 0.5f);
        _groundRefresh = 0.15f;
    }

    // ---------- Layout ----------

    private void Layout()
    {
        Vector2 view = GetViewportRect().Size;
        _cell = Mathf.Clamp(Mathf.Min(view.Y / 20f, view.X / 30f), 26f, 60f);
        // Shrink the cells until everything you carry fits on screen.
        for (int attempt = 0; attempt < 8 && !LayoutWith(view); attempt++)
            _cell = Mathf.Max(_cell * 0.92f, 20f);

        _cursorArea = Mathf.Clamp(_cursorArea, 0, _areas.Count - 1);
        Area current = _areas[_cursorArea];
        _cursorCell = new Vector2I(Mathf.Clamp(_cursorCell.X, 0, current.Columns - 1), Mathf.Clamp(_cursorCell.Y, 0, current.Rows - 1));
    }

    /// <summary>Lays everything out at the current cell size; false if the storage panel overflows.</summary>
    private bool LayoutWith(Vector2 view)
    {
        _areas.Clear();
        _titles.Clear();
        _panels.Clear();
        float gap = _cell * 0.3f;
        float top = view.Y * 0.11f;
        float panelHeight = view.Y * 0.79f;
        float panelWidth = Mathf.Min(view.X * 0.3f, _cell * 9f);
        float left = (view.X - panelWidth * 3f - gap * 4f) * 0.5f + gap;

        // Panel 0: gear slots (two columns) and the weapon in your hands.
        var gearPanel = new Rect2(left, top, panelWidth, panelHeight);
        _panels.Add(gearPanel);
        _titles.Add(("EQUIPMENT", gearPanel.Position + new Vector2(gap, gap + 14f)));
        Vector2 cursor = gearPanel.Position + new Vector2(gap, gap * 2f + 24f);
        float slotSize = _cell * 2f;
        float rowStep = slotSize + gap + 22f;
        GearSlot[] slots = { GearSlot.Headwear, GearSlot.BodyArmor, GearSlot.Top, GearSlot.Bottoms, GearSlot.ChestRig, GearSlot.Backpack };
        for (int i = 0; i < slots.Length; i++)
        {
            Vector2 at = cursor + new Vector2(i % 2 * (slotSize + gap * 3f), i / 2 * rowStep + 18f);
            _titles.Add((SlotName(slots[i]), at - new Vector2(0f, 6f)));
            _areas.Add(new Area { Kind = AreaKind.Slot, Slot = slots[i], Rect = new Rect2(at, new Vector2(slotSize, slotSize)), Panel = 0 });
        }
        Vector2 weaponAt = cursor + new Vector2(0f, 3f * rowStep + 24f);
        _titles.Add(("Weapon (in hands)", weaponAt - new Vector2(0f, 6f)));
        _weaponRect = new Rect2(weaponAt, new Vector2(_cell * 5f, _cell * 1.5f));

        // Panel 1: storage (rig, clothing pockets, pockets, backpack).
        var storagePanel = new Rect2(left + panelWidth + gap, top, panelWidth, panelHeight);
        _panels.Add(storagePanel);
        _titles.Add(("STORAGE", storagePanel.Position + new Vector2(gap, gap + 14f)));
        float x0 = storagePanel.Position.X + gap;
        float right = storagePanel.End.X - gap;
        float y = storagePanel.Position.Y + gap * 2f + 24f;

        if (_inventory.Equipped(GearSlot.ChestRig) is { } rig)
            y = Section(rig.Data.DisplayName, rig.Grids, x0, y, right, gap);
        Item? topWear = _inventory.Equipped(GearSlot.Top);
        Item? bottoms = _inventory.Equipped(GearSlot.Bottoms);
        if (topWear != null || bottoms != null)
        {
            var clothingGrids = new List<ItemGrid>();
            if (topWear != null)
                clothingGrids.AddRange(topWear.Grids);
            if (bottoms != null)
                clothingGrids.AddRange(bottoms.Grids);
            string title = topWear != null && bottoms != null ? $"{topWear.Data.DisplayName} · {bottoms.Data.DisplayName}"
                : (topWear ?? bottoms)!.Data.DisplayName;
            y = Section(title, clothingGrids, x0, y, right, gap);
        }
        y = Section("Pockets", _inventory.Pockets, x0, y, right, gap);
        if (_inventory.Equipped(GearSlot.Backpack) is { } backpack)
            y = Section(backpack.Data.DisplayName, backpack.Grids, x0, y, right, gap);
        bool fits = y <= storagePanel.End.Y;

        // Panel 2: the ground.
        var groundPanel = new Rect2(left + (panelWidth + gap) * 2f, top, panelWidth, panelHeight);
        _panels.Add(groundPanel);
        _titles.Add(("GROUND", groundPanel.Position + new Vector2(gap, gap + 14f)));
        var groundRect = new Rect2(groundPanel.Position + new Vector2(gap, gap * 2f + 24f), new Vector2(GroundWidth, GroundHeight) * _cell);
        _areas.Add(new Area { Kind = AreaKind.Ground, Grid = _ground, Rect = groundRect, Panel = 2 });
        return fits && groundRect.End.Y <= groundPanel.End.Y;
    }

    private float Section(string title, List<ItemGrid> grids, float x0, float y, float right, float gap)
    {
        _titles.Add((title, new Vector2(x0, y + 12f)));
        return FlowGrids(grids, x0, y + 20f, right, gap) + gap;
    }

    /// <summary>Lays grids left to right, wrapping; returns the bottom.</summary>
    private float FlowGrids(List<ItemGrid> grids, float x0, float y, float right, float gap)
    {
        float x = x0;
        float rowBottom = y;
        foreach (ItemGrid grid in grids)
        {
            var size = new Vector2(grid.Width, grid.Height) * _cell;
            if (x > x0 && x + size.X > right)
            {
                x = x0;
                y = rowBottom + gap * 0.5f;
            }
            _areas.Add(new Area { Kind = AreaKind.Grid, Grid = grid, Rect = new Rect2(new Vector2(x, y), size), Panel = 1 });
            x += size.X + gap * 0.5f;
            rowBottom = Mathf.Max(rowBottom, y + size.Y);
        }
        return rowBottom;
    }

    private static string SlotName(GearSlot slot) => slot switch
    {
        GearSlot.Headwear => "Headwear",
        GearSlot.BodyArmor => "Body armor",
        GearSlot.ChestRig => "Chest rig",
        GearSlot.Backpack => "Backpack",
        GearSlot.Top => "Top",
        GearSlot.Bottoms => "Bottoms",
        _ => "",
    };

    // ---------- Hit testing ----------

    private bool CellAt(Vector2 point, out int areaIndex, out Vector2I cell)
    {
        for (int i = 0; i < _areas.Count; i++)
        {
            Area area = _areas[i];
            if (!area.Rect.HasPoint(point))
                continue;
            areaIndex = i;
            Vector2 local = (point - area.Rect.Position) / (area.Kind == AreaKind.Slot ? area.Rect.Size : new Vector2(_cell, _cell));
            cell = area.Kind == AreaKind.Slot ? Vector2I.Zero : new Vector2I((int)local.X, (int)local.Y);
            return true;
        }
        areaIndex = -1;
        cell = Vector2I.Zero;
        return false;
    }

    private Item? ItemIn(Area area, Vector2I cell) => area.Kind switch
    {
        AreaKind.Slot => _inventory.Equipped(area.Slot),
        _ => area.Grid!.At(cell.X, cell.Y)?.Item,
    };

    // ---------- Input ----------

    public override void _Input(InputEvent e)
    {
        if (!Visible)
            return;
        GameInput.Instance.Observe(e);
        if (e is InputEventMouseMotion)
            return; // Nothing else uses it while the pointer is free.
        // These belong to the player (closing the inventory, the health screen); walking is polled.
        if (e.IsAction(InputActions.Inventory) || e.IsAction(InputActions.HealthScreen))
            return;
        if (e is InputEventKey key && !key.Pressed)
            return; // Let key releases through so held movement keys don't stick.

        GetViewport().SetInputAsHandled();
        if (e is InputEventMouseButton mouse)
            HandleMouseButton(mouse);
        else if (e.IsActionPressed(InputActions.InvRotate))
        {
            if (_held != null)
                RotateHeld();
            else if (UsingGamepad)
                QuickMove(_cursorArea, _cursorCell);
        }
        else if (e.IsActionPressed(InputActions.InvDrop))
        {
            if (UsingGamepad)
                DropAt(_cursorArea, _cursorCell);
            else if (CellAt(GetGlobalMousePosition(), out int area, out Vector2I cell))
                DropAt(area, cell);
        }
        else if (e.IsActionPressed(InputActions.InvCancel))
        {
            if (_held != null)
                ReturnHeld();
            else
                _player.SetInventoryOpen(false);
        }
        else if (e.IsActionPressed(InputActions.InvSelect))
            GamepadSelect();
        else if (e.IsActionPressed(InputActions.InvUp))
            MoveCursor(Vector2.Up);
        else if (e.IsActionPressed(InputActions.InvDown))
            MoveCursor(Vector2.Down);
        else if (e.IsActionPressed(InputActions.InvLeft))
            MoveCursor(Vector2.Left);
        else if (e.IsActionPressed(InputActions.InvRight))
            MoveCursor(Vector2.Right);
        else if (e.IsActionPressed(InputActions.InvPrevPanel))
            JumpPanel(-1);
        else if (e.IsActionPressed(InputActions.InvNextPanel))
            JumpPanel(1);
    }

    private void HandleMouseButton(InputEventMouseButton mouse)
    {
        if (mouse.ButtonIndex != MouseButton.Left)
            return;
        Vector2 point = mouse.Position;
        if (mouse.Pressed)
        {
            if (!CellAt(point, out int areaIndex, out Vector2I cell))
                return;
            if (mouse.CtrlPressed)
            {
                QuickMove(areaIndex, cell);
                return;
            }
            if (_held == null && PickUp(areaIndex, cell))
                _mouseDragging = true;
        }
        else if (_mouseDragging && _held != null)
        {
            _mouseDragging = false;
            if (CellAt(point, out int areaIndex, out Vector2I cell))
            {
                if (!PlaceHeld(areaIndex, cell - (_areas[areaIndex].Kind == AreaKind.Slot ? Vector2I.Zero : _grabOffset)))
                    ReturnHeld();
            }
            else
            {
                // Dragged off the grids: drop it on the ground.
                Item item = _held;
                AreaKind origin = _originKind;
                _held = null;
                if (origin == AreaKind.Ground)
                    return; // It's already there.
                DropInWorld(item);
            }
        }
    }

    private void GamepadSelect()
    {
        if (_held == null)
            PickUp(_cursorArea, _cursorCell, cursorIsTopLeft: true);
        else if (!PlaceHeld(_cursorArea, _cursorCell))
        {
            // Invalid spot: keep holding it (B puts it back).
        }
    }

    private void RotateHeld()
    {
        if (_held != null && _held.Data.Size.X != _held.Data.Size.Y)
        {
            _heldRotated = !_heldRotated;
            _grabOffset = new Vector2I(_grabOffset.Y, _grabOffset.X);
        }
    }

    // ---------- Moving items ----------

    private bool PickUp(int areaIndex, Vector2I cell, bool cursorIsTopLeft = false)
    {
        Area area = _areas[areaIndex];
        if (area.Kind == AreaKind.Slot)
        {
            if (_inventory.Unequip(area.Slot) is not { } worn)
                return false;
            Hold(worn, AreaKind.Slot, false, Vector2I.Zero);
            _originSlot = area.Slot;
            return true;
        }

        ItemGrid.Placement? placement = area.Grid!.At(cell.X, cell.Y);
        if (placement == null)
            return false;
        Hold(placement.Item, area.Kind, placement.Rotated, cursorIsTopLeft ? Vector2I.Zero : cell - new Vector2I(placement.X, placement.Y));
        _originGrid = area.Grid;
        _originCell = new Vector2I(placement.X, placement.Y);
        area.Grid.Remove(placement.Item);
        if (area.Kind == AreaKind.Grid)
            _inventory.NotifyChanged();
        return true;
    }

    private void Hold(Item item, AreaKind origin, bool rotated, Vector2I grabOffset)
    {
        _held = item;
        _originKind = origin;
        _originRotated = rotated;
        _heldRotated = rotated;
        _grabOffset = grabOffset;
    }

    /// <summary>Tries to put the held item down with its top-left at <paramref name="cell"/>.</summary>
    private bool PlaceHeld(int areaIndex, Vector2I cell)
    {
        if (_held == null || areaIndex < 0)
            return false;
        Area area = _areas[areaIndex];
        Item item = _held;

        switch (area.Kind)
        {
            case AreaKind.Slot:
                if (item.Data.Slot != area.Slot || _inventory.Equipped(area.Slot) != null)
                    return false;
                FromOrigin(item);
                _inventory.Equip(item);
                return true;

            case AreaKind.Ground:
                if (_originKind == AreaKind.Ground)
                {
                    // Rearranging on the ground does nothing; just put it back.
                    ReturnHeld();
                    return true;
                }
                _held = null;
                DropInWorld(item);
                return true;

            default:
                ItemGrid grid = area.Grid!;
                if (item.Grids.Contains(grid))
                    return false; // Not inside itself.
                // Onto a stack of the same thing: top it up.
                Vector2I hovered = cell + _grabOffset;
                if (grid.At(hovered.X, hovered.Y) is { } target && target.Item != item && target.Item.Data == item.Data && item.Data.MaxStack > 1)
                {
                    FromOrigin(item);
                    Item.MergeInto(item, target.Item);
                    _held = item.Count > 0 ? item : null;
                    if (_held != null)
                        ReturnHeld();
                    _inventory.NotifyChanged();
                    return true;
                }
                if (!ResolveSpot(grid, cell, out Vector2I spot, out bool rotated))
                    return false;
                FromOrigin(item);
                _heldRotated = rotated;
                grid.TryPlace(item, spot.X, spot.Y, rotated);
                _inventory.NotifyChanged();
                return true;
        }
    }

    /// <summary>The held item is being placed somewhere new: finish taking it from where it was.</summary>
    private void FromOrigin(Item item)
    {
        if (_originKind == AreaKind.Ground)
            TakeFromWorld(item);
        _held = null;
    }

    /// <summary>Put the held item back exactly where it came from.</summary>
    private void ReturnHeld()
    {
        if (_held == null)
            return;
        Item item = _held;
        _held = null;
        _mouseDragging = false;
        switch (_originKind)
        {
            case AreaKind.Slot:
                _inventory.Equip(item);
                break;
            case AreaKind.Ground:
                _originGrid?.TryPlace(item, _originCell.X, _originCell.Y, _originRotated);
                break;
            default:
                if (_originGrid == null || !_originGrid.TryPlace(item, _originCell.X, _originCell.Y, _originRotated))
                {
                    if (!_inventory.TryAddAnywhere(item))
                        DropInWorld(item);
                }
                _inventory.NotifyChanged();
                break;
        }
    }

    /// <summary>Ctrl+click / gamepad X: ground → inventory (wear it if the slot is free), inventory → ground.</summary>
    private void QuickMove(int areaIndex, Vector2I cell)
    {
        if (areaIndex < 0 || _held != null)
            return;
        Area area = _areas[areaIndex];
        Item? item = ItemIn(area, cell);
        if (item == null)
            return;

        if (area.Kind == AreaKind.Ground)
        {
            if (_inventory.CanEquip(item))
            {
                _ground.Remove(item);
                TakeFromWorld(item);
                _inventory.Equip(item);
            }
            else if (_inventory.HasRoomFor(item) || item.Data.MaxStack > 1)
            {
                _ground.Remove(item);
                TakeFromWorld(item);
                if (!_inventory.TryAddAnywhere(item))
                    DropInWorld(item); // Only part of a stack fit.
            }
            else
            {
                Flash("No room");
            }
            return;
        }
        DropAt(areaIndex, cell);
    }

    private void DropAt(int areaIndex, Vector2I cell)
    {
        if (areaIndex < 0 || _held != null)
            return;
        Area area = _areas[areaIndex];
        Item? item = ItemIn(area, cell);
        if (item == null || area.Kind == AreaKind.Ground)
            return;
        if (area.Kind == AreaKind.Slot)
            _inventory.Unequip(area.Slot);
        else
        {
            area.Grid!.Remove(item);
            _inventory.NotifyChanged();
        }
        DropInWorld(item);
    }

    // ---------- Gamepad cursor ----------

    private Vector2 CellCenter(int areaIndex, Vector2I cell)
    {
        Area area = _areas[areaIndex];
        if (area.Kind == AreaKind.Slot)
            return area.Rect.GetCenter();
        return area.Rect.Position + (new Vector2(cell.X, cell.Y) + new Vector2(0.5f, 0.5f)) * _cell;
    }

    /// <summary>Moves to the nearest cell in that direction, across grids and panels.</summary>
    private void MoveCursor(Vector2 direction)
    {
        Area current = _areas[_cursorArea];
        // Inside the same grid: just step.
        if (current.Kind != AreaKind.Slot)
        {
            Vector2I next = _cursorCell + new Vector2I((int)direction.X, (int)direction.Y);
            if (next.X >= 0 && next.Y >= 0 && next.X < current.Columns && next.Y < current.Rows)
            {
                _cursorCell = next;
                return;
            }
        }
        Vector2 from = CellCenter(_cursorArea, _cursorCell);
        float best = float.MaxValue;
        for (int i = 0; i < _areas.Count; i++)
        {
            if (i == _cursorArea)
                continue;
            Area area = _areas[i];
            for (int cy = 0; cy < area.Rows; cy++)
            {
                for (int cx = 0; cx < area.Columns; cx++)
                {
                    Vector2 delta = CellCenter(i, new Vector2I(cx, cy)) - from;
                    float along = delta.Dot(direction);
                    if (along <= 1f)
                        continue;
                    float across = Mathf.Abs(delta.Cross(direction));
                    float score = along + across * 2f;
                    if (score < best)
                    {
                        best = score;
                        _cursorArea = i;
                        _cursorCell = new Vector2I(cx, cy);
                    }
                }
            }
        }
    }

    private void JumpPanel(int step)
    {
        int panel = (_areas[_cursorArea].Panel + step + 3) % 3;
        for (int i = 0; i < _areas.Count; i++)
        {
            if (_areas[i].Panel == panel)
            {
                _cursorArea = i;
                _cursorCell = Vector2I.Zero;
                return;
            }
        }
    }

    // ---------- Drawing ----------

    private string _flash = "";
    private float _flashLeft;

    private void Flash(string text)
    {
        _flash = text;
        _flashLeft = 1.5f;
    }

    public override void _Draw()
    {
        Vector2 view = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, view), new Color(0f, 0f, 0f, 0.6f));
        foreach (Rect2 panel in _panels)
        {
            DrawRect(panel, new Color(0.05f, 0.05f, 0.06f, 0.96f));
            DrawRect(panel, new Color(1f, 1f, 1f, 0.18f), filled: false, width: 1.5f);
        }
        foreach ((string text, Vector2 position) in _titles)
            DrawString(_font, position, text, HorizontalAlignment.Left, -1, 15, new Color(1f, 1f, 1f, 0.75f));

        DrawWeight(view);
        DrawRect(_weaponRect, new Color(1f, 1f, 1f, 0.05f));
        DrawRect(_weaponRect, new Color(1f, 1f, 1f, 0.3f), filled: false, width: 1.5f);
        if (_player.Weapons.Weapon is { } weapon)
        {
            DrawRect(_weaponRect.Grow(-4f), new Color(0.22f, 0.22f, 0.24f, 0.95f));
            DrawString(_font, _weaponRect.Position + new Vector2(10f, 22f), weapon.Data.DisplayName, HorizontalAlignment.Left, _weaponRect.Size.X - 14f, 15, Colors.White);
            DrawString(_font, _weaponRect.Position + new Vector2(10f, 42f), $"{weapon.Data.MassKg * 2.2046f:0.0} lb", HorizontalAlignment.Left, -1, 13, new Color(1f, 1f, 1f, 0.6f));
        }
        for (int i = 0; i < _areas.Count; i++)
            DrawArea(_areas[i]);

        Vector2 mouse = GetGlobalMousePosition();
        if (UsingGamepad)
            DrawCursor();
        else if (_held == null && CellAt(mouse, out int hoverArea, out Vector2I hoverCell) && ItemIn(_areas[hoverArea], hoverCell) is { } hovered)
            DrawTooltip(hovered, mouse + new Vector2(18f, 18f));

        if (_held != null)
            DrawHeld(mouse);

        _flashLeft -= (float)GetProcessDeltaTime();
        if (_flashLeft > 0f)
            DrawString(_font, new Vector2(view.X * 0.5f - 60f, view.Y * 0.1f), _flash, HorizontalAlignment.Left, -1, 20, new Color(1f, 0.5f, 0.4f));
        DrawHelp(view);
    }

    private void DrawWeight(Vector2 view)
    {
        float weight = _inventory.WeightLb;
        float limit = _inventory.CarryLimitLb;
        (string state, Color color) = _inventory.IsImmobile ? ("CAN'T MOVE", new Color(1f, 0.25f, 0.2f))
            : _inventory.IsHeavilyOverloaded ? ("HEAVILY OVERLOADED", new Color(1f, 0.45f, 0.2f))
            : _inventory.IsOverweight ? ("OVERWEIGHT", new Color(1f, 0.8f, 0.3f))
            : ("", Colors.White);
        string text = $"Weight  {weight:0.0} / {limit:0.0} lb   {state}";
        DrawString(_font, new Vector2(view.X * 0.5f - 160f, view.Y * 0.12f - 14f), text, HorizontalAlignment.Left, -1, 18, color);
    }

    private void DrawArea(Area area)
    {
        if (area.Kind == AreaKind.Slot)
        {
            DrawRect(area.Rect, new Color(1f, 1f, 1f, 0.05f));
            DrawRect(area.Rect, new Color(1f, 1f, 1f, 0.3f), filled: false, width: 1.5f);
            if (_inventory.Equipped(area.Slot) is { } worn)
                DrawItemBox(worn, area.Rect.Grow(-4f), 1f);
            else if (_held != null && _held.Data.Slot == area.Slot)
                DrawRect(area.Rect.Grow(-2f), new Color(0.3f, 0.9f, 0.4f, 0.25f));
            return;
        }

        ItemGrid grid = area.Grid!;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = new Rect2(area.Rect.Position + new Vector2(x, y) * _cell, new Vector2(_cell, _cell));
                DrawRect(cell, new Color(1f, 1f, 1f, 0.04f));
                DrawRect(cell, new Color(1f, 1f, 1f, 0.16f), filled: false, width: 1f);
            }
        }
        foreach (ItemGrid.Placement p in grid.Items)
        {
            var rect = new Rect2(area.Rect.Position + new Vector2(p.X, p.Y) * _cell, new Vector2(p.Size.X, p.Size.Y) * _cell);
            DrawItemBox(p.Item, rect.Grow(-2f), 1f);
        }
    }

    private void DrawItemBox(Item item, Rect2 rect, float alpha)
    {
        Color color = item.Data.Color;
        color.A = 0.9f * alpha;
        DrawRect(rect, color);
        DrawRect(rect, new Color(1f, 1f, 1f, 0.35f * alpha), filled: false, width: 1f);
        int fontSize = (int)Mathf.Clamp(_cell * 0.26f, 10f, 15f);
        DrawString(_font, rect.Position + new Vector2(4f, fontSize + 2f), item.Data.ShortName, HorizontalAlignment.Left, rect.Size.X - 6f, fontSize, new Color(1f, 1f, 1f, alpha));
        if (item.Count > 1)
            DrawString(_font, rect.End - new Vector2(rect.Size.X - 4f, 4f), item.Count.ToString(), HorizontalAlignment.Right, rect.Size.X - 8f, fontSize, new Color(1f, 1f, 0.7f, alpha));
    }

    private void DrawHeld(Vector2 mouse)
    {
        Vector2I size = ItemGrid.SizeOf(_held!, _heldRotated);
        Vector2 topLeft;
        bool valid = false;
        if (UsingGamepad)
        {
            topLeft = CellCenter(_cursorArea, _cursorCell) - new Vector2(_cell, _cell) * 0.5f;
            valid = CanPlaceAt(_cursorArea, _cursorCell);
            // Show where it would actually land (shifted / turned to fit).
            if (valid && _areas[_cursorArea].Kind == AreaKind.Grid
                && ResolveSpot(_areas[_cursorArea].Grid!, _cursorCell, out Vector2I spot, out bool turned))
            {
                size = ItemGrid.SizeOf(_held!, turned);
                topLeft = _areas[_cursorArea].Rect.Position + new Vector2(spot.X, spot.Y) * _cell;
            }
        }
        else
        {
            topLeft = mouse - (new Vector2(_grabOffset.X, _grabOffset.Y) + new Vector2(0.5f, 0.5f)) * _cell;
            if (CellAt(mouse, out int area, out Vector2I cell))
            {
                Vector2I target = _areas[area].Kind == AreaKind.Slot ? Vector2I.Zero : cell - _grabOffset;
                valid = CanPlaceAt(area, target);
                if (_areas[area].Kind == AreaKind.Grid)
                {
                    Vector2I snapSize = size;
                    if (ResolveSpot(_areas[area].Grid!, target, out Vector2I spot, out bool turned))
                    {
                        target = spot;
                        snapSize = ItemGrid.SizeOf(_held!, turned);
                    }
                    var snap = new Rect2(_areas[area].Rect.Position + new Vector2(target.X, target.Y) * _cell, new Vector2(snapSize.X, snapSize.Y) * _cell);
                    DrawRect(snap, valid ? new Color(0.3f, 0.9f, 0.4f, 0.3f) : new Color(1f, 0.25f, 0.2f, 0.3f));
                }
            }
        }
        var rect = new Rect2(topLeft, new Vector2(size.X, size.Y) * _cell);
        DrawItemBox(_held!, rect.Grow(-2f), 0.8f);
        if (UsingGamepad)
            DrawRect(rect, valid ? new Color(0.3f, 0.9f, 0.4f) : new Color(1f, 0.3f, 0.2f), filled: false, width: 2.5f);
    }

    /// <summary>
    /// Where the held item goes in a grid: exactly as dragged if it fits; otherwise shifted and/or turned
    /// so it still covers the cell under the cursor (drop a sideways magazine on a rig pouch and it stands up).
    /// </summary>
    private bool ResolveSpot(ItemGrid grid, Vector2I topLeft, out Vector2I spot, out bool rotated)
    {
        Vector2I hovered = topLeft + _grabOffset;
        for (int pass = 0; pass < 2; pass++)
        {
            rotated = pass == 0 ? _heldRotated : !_heldRotated;
            Vector2I size = ItemGrid.SizeOf(_held!, rotated);
            if (pass == 1 && size == ItemGrid.SizeOf(_held!, _heldRotated))
                break; // Square: turning changes nothing.
            if (pass == 0 && grid.Fits(topLeft.X, topLeft.Y, size))
            {
                spot = topLeft;
                return true;
            }
            for (int dy = 0; dy < size.Y; dy++)
            {
                for (int dx = 0; dx < size.X; dx++)
                {
                    spot = hovered - new Vector2I(dx, dy);
                    if (grid.Fits(spot.X, spot.Y, size))
                        return true;
                }
            }
        }
        spot = topLeft;
        rotated = _heldRotated;
        return false;
    }

    private bool CanPlaceAt(int areaIndex, Vector2I cell)
    {
        if (_held == null || areaIndex < 0)
            return false;
        Area area = _areas[areaIndex];
        return area.Kind switch
        {
            AreaKind.Slot => _held.Data.Slot == area.Slot && _inventory.Equipped(area.Slot) == null,
            AreaKind.Ground => true,
            _ => !_held.Grids.Contains(area.Grid!)
                 && ((area.Grid!.At(cell.X + _grabOffset.X, cell.Y + _grabOffset.Y) is { } t && t.Item.Data == _held.Data && _held.Data.MaxStack > 1)
                     || ResolveSpot(area.Grid, cell, out _, out _)),
        };
    }

    private void DrawCursor()
    {
        Area area = _areas[_cursorArea];
        Rect2 rect = area.Kind == AreaKind.Slot
            ? area.Rect
            : new Rect2(area.Rect.Position + new Vector2(_cursorCell.X, _cursorCell.Y) * _cell, new Vector2(_cell, _cell));
        // Over an item: outline the whole item.
        if (area.Kind != AreaKind.Slot && area.Grid!.At(_cursorCell.X, _cursorCell.Y) is { } hovered)
            rect = new Rect2(area.Rect.Position + new Vector2(hovered.X, hovered.Y) * _cell, new Vector2(hovered.Size.X, hovered.Size.Y) * _cell);
        if (_held == null)
        {
            DrawRect(rect.Grow(1f), new Color(1f, 0.85f, 0.3f), filled: false, width: 2.5f);
            if (ItemIn(area, _cursorCell) is { } item)
                DrawTooltip(item, rect.End + new Vector2(8f, 0f));
        }
    }

    private void DrawTooltip(Item item, Vector2 at)
    {
        string text = $"{item.Data.DisplayName}{(item.Count > 1 ? $" ×{item.Count}" : "")}\n{item.WeightLb:0.00} lb";
        if (item.Grids.Count > 0)
            text += $"\nStorage: {item.Grids.Count} {(item.Grids.Count == 1 ? "compartment" : "compartments")}";
        if (item.Data.Slot != GearSlot.None)
            text += $"\nWorn as: {SlotName(item.Data.Slot)}";
        string[] lines = text.Split('\n');
        var box = new Rect2(at, new Vector2(230f, lines.Length * 18f + 10f));
        Vector2 view = GetViewportRect().Size;
        if (box.End.X > view.X)
            box.Position -= new Vector2(box.Size.X + 30f, 0f);
        if (box.End.Y > view.Y)
            box.Position -= new Vector2(0f, box.Size.Y + 30f);
        DrawRect(box, new Color(0f, 0f, 0f, 0.9f));
        DrawRect(box, new Color(1f, 1f, 1f, 0.3f), filled: false, width: 1f);
        for (int i = 0; i < lines.Length; i++)
            DrawString(_font, box.Position + new Vector2(8f, 18f + i * 18f), lines[i], HorizontalAlignment.Left, -1, 14, Colors.White);
    }

    private void DrawHelp(Vector2 view)
    {
        string help = UsingGamepad
            ? "D-pad move   A pick up / place   X rotate (empty-handed: quick move)   LB/RB panels   B put back / close   View close"
            : "Drag to move   R rotate while dragging   Ctrl+click quick move   Delete or drag off the grids: drop   Tab / Esc close";
        DrawString(_font, new Vector2(view.X * 0.5f - 420f, view.Y * 0.93f), help, HorizontalAlignment.Left, -1, 14, new Color(1f, 1f, 1f, 0.7f));
    }
}
