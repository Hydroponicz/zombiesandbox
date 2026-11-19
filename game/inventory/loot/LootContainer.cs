using System.Collections.Generic;
using Godot;
using Godot.Collections;
using ZombieSandbox.Core;
using ZombieSandbox.Core.Interaction;

namespace ZombieSandbox.Inventory;

/// <summary>
/// Something you search: a crate, a locker, a body. Opening it shows its grid in the inventory screen;
/// its items are revealed one at a time (bigger ones take longer; the Scavenging skill speeds it up).
/// Contents are rolled from a loot table the first time it's opened and remembered (persistent).
/// </summary>
public partial class LootContainer : StaticBody3D, IInteractable, IPersistent
{
    /// <summary>Unique within the level (saved state is keyed on it).</summary>
    [Export] public string ContainerId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "Crate";
    [Export] public Vector2I GridSize { get; set; } = new(5, 4);
    [Export] public LootTable? Loot { get; set; }

    public ItemGrid Grid { get; private set; } = null!;
    public string PersistentId => ContainerId;
    /// <summary>The item being searched right now, and how far along (0..1).</summary>
    public Item? Searching => _hidden.Count > 0 ? _hidden[0] : null;
    public float SearchFraction { get; private set; }
    public bool FullySearched => _generated && _hidden.Count == 0;

    private readonly List<Item> _hidden = new();
    private bool _generated;
    private readonly RandomNumberGenerator _rng = new();

    public string InteractPrompt => FullySearched ? $"Open {DisplayName}" : $"Search {DisplayName}";

    public override void _Ready()
    {
        Grid ??= new ItemGrid(GridSize.X, GridSize.Y);
        _rng.Randomize();
    }

    public bool CanInteract(Node user) => user.GetNodeOrNull<CharacterInventory>("Inventory") != null;

    public void Interact(Node user)
    {
        if (user is Player.PlayerController player)
            player.OpenContainer(this);
    }

    /// <summary>First opening: roll the loot (all of it hidden until searched).</summary>
    public void Generate()
    {
        if (_generated)
            return;
        _generated = true;
        if (Loot == null)
            return;
        foreach (Item item in Loot.Roll(_rng))
            AddHidden(item);
    }

    /// <summary>Puts an item in (hidden until searched). False if it doesn't fit.</summary>
    public bool AddHidden(Item item)
    {
        Grid ??= new ItemGrid(GridSize.X, GridSize.Y);
        if (!Grid.FindSpot(item, out int x, out int y, out bool rotated) || !Grid.TryPlace(item, x, y, rotated))
            return false;
        _hidden.Add(item);
        return true;
    }

    public bool IsHidden(Item item) => _hidden.Contains(item);

    /// <summary>Seconds to find one item: ≈1.5 s at Scavenging 0 → ≈0.4 s at 10, longer for big items.</summary>
    public static float SearchSeconds(Item item, int scavenging)
    {
        float base_ = Mathf.Lerp(1.5f, 0.4f, scavenging / 10f);
        int cells = item.Data.Size.X * item.Data.Size.Y;
        return base_ * Mathf.Min(0.75f + 0.25f * cells, 2.5f);
    }

    /// <summary>Keep searching (while open). Returns the item that was just revealed, if any.</summary>
    public Item? Search(float dt, int scavenging)
    {
        // Something already taken out (it can't be while hidden, but be safe).
        while (_hidden.Count > 0 && Grid.Find(_hidden[0]) == null)
            _hidden.RemoveAt(0);
        if (_hidden.Count == 0)
        {
            SearchFraction = 0f;
            return null;
        }
        Item current = _hidden[0];
        SearchFraction += dt / SearchSeconds(current, scavenging);
        if (SearchFraction < 1f)
            return null;
        SearchFraction = 0f;
        _hidden.RemoveAt(0);
        return current;
    }

    /// <summary>A body getting up (it rose as a zombie): whatever's left falls on the ground.</summary>
    public void SpillAndRemove()
    {
        int i = 0;
        foreach (ItemGrid.Placement p in new List<ItemGrid.Placement>(Grid.Items))
        {
            Vector3 offset = new(0.35f * Mathf.Cos(i * 2.1f), 0.3f, 0.35f * Mathf.Sin(i * 2.1f));
            WorldItem.Spawn(GetTree().CurrentScene, p.Item, GlobalPosition + offset, Vector3.Zero);
            i++;
        }
        Grid.Clear();
        _hidden.Clear();
        QueueFree();
    }

    /// <summary>A searchable body at a spot (a dead NPC): an invisible box on the items layer.</summary>
    public static LootContainer CreateBody(Node parent, Vector3 position, string name, Vector2I gridSize)
    {
        var body = new LootContainer
        {
            DisplayName = name,
            GridSize = gridSize,
            CollisionLayer = 1u << 2, // Items: interactable, not something to bump into.
            CollisionMask = 0,
        };
        body.Grid = new ItemGrid(gridSize.X, gridSize.Y);
        body._generated = true;
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.8f, 0.4f, 1.6f) }, Position = new Vector3(0f, 0.2f, 0f) });
        parent.AddChild(body);
        body.GlobalPosition = position;
        return body;
    }

    public Dictionary SaveState()
    {
        var hidden = new Array<string>();
        foreach (Item item in _hidden)
            hidden.Add(item.Id);
        return new Dictionary { ["generated"] = _generated, ["grid"] = Grid.SaveState(), ["hidden"] = hidden };
    }

    public void LoadState(Dictionary state)
    {
        _generated = state["generated"].AsBool();
        Grid.LoadState(state["grid"].AsGodotDictionary());
        _hidden.Clear();
        foreach (string id in state["hidden"].AsGodotArray<string>())
        {
            foreach (ItemGrid.Placement p in Grid.Items)
            {
                if (p.Item.Id == id)
                    _hidden.Add(p.Item);
            }
        }
        SearchFraction = 0f;
    }
}
