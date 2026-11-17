using System.Collections.Generic;
using Godot;
using Godot.Collections;
using ZombieSandbox.Core;

namespace ZombieSandbox.Inventory;

/// <summary>One rectangle of storage cells (a pocket, a rig pouch, a backpack's main compartment).</summary>
public sealed class ItemGrid : ISaveState
{
    public sealed class Placement
    {
        public Item Item = null!;
        public int X;
        public int Y;
        public bool Rotated;
        public Vector2I Size => SizeOf(Item, Rotated);
    }

    public int Width { get; }
    public int Height { get; }
    private readonly List<Placement> _items = new();
    public IReadOnlyList<Placement> Items => _items;

    public ItemGrid(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public static Vector2I SizeOf(Item item, bool rotated) =>
        rotated ? new Vector2I(item.Data.Size.Y, item.Data.Size.X) : item.Data.Size;

    /// <summary>Would an item of this size fit with its top-left corner at (x, y)? (Ignoring one item, e.g. the one being moved.)</summary>
    public bool Fits(int x, int y, Vector2I size, Item? ignore = null)
    {
        if (x < 0 || y < 0 || x + size.X > Width || y + size.Y > Height)
            return false;
        foreach (Placement p in _items)
        {
            if (p.Item == ignore)
                continue;
            Vector2I s = p.Size;
            if (x < p.X + s.X && p.X < x + size.X && y < p.Y + s.Y && p.Y < y + size.Y)
                return false;
        }
        return true;
    }

    public bool TryPlace(Item item, int x, int y, bool rotated)
    {
        if (!Fits(x, y, SizeOf(item, rotated)))
            return false;
        _items.Add(new Placement { Item = item, X = x, Y = y, Rotated = rotated });
        return true;
    }

    /// <summary>Top-up existing stacks, then the first free spot (upright first, then turned).</summary>
    public bool TryAdd(Item item)
    {
        if (TryMergeIntoStacks(item))
            return true;
        return FindSpot(item, out int x, out int y, out bool rotated) && TryPlace(item, x, y, rotated);
    }

    public bool FindSpot(Item item, out int x, out int y, out bool rotated)
    {
        for (int r = 0; r < 2; r++)
        {
            rotated = r == 1;
            if (rotated && item.Data.Size.X == item.Data.Size.Y)
                break;
            Vector2I size = SizeOf(item, rotated);
            for (y = 0; y <= Height - size.Y; y++)
            {
                for (x = 0; x <= Width - size.X; x++)
                {
                    if (Fits(x, y, size))
                        return true;
                }
            }
        }
        x = y = 0;
        rotated = false;
        return false;
    }

    /// <summary>Adds as much of a stack as fits into existing stacks of the same item. True if all of it went in.</summary>
    public bool TryMergeIntoStacks(Item item)
    {
        if (item.Data.MaxStack <= 1)
            return false;
        foreach (Placement p in _items)
        {
            if (item.Count <= 0)
                break;
            Item.MergeInto(item, p.Item);
        }
        return item.Count <= 0;
    }

    public Placement? At(int x, int y)
    {
        foreach (Placement p in _items)
        {
            Vector2I s = p.Size;
            if (x >= p.X && x < p.X + s.X && y >= p.Y && y < p.Y + s.Y)
                return p;
        }
        return null;
    }

    public Placement? Find(Item item)
    {
        foreach (Placement p in _items)
        {
            if (p.Item == item)
                return p;
        }
        return null;
    }

    public void Clear() => _items.Clear();

    public bool Remove(Item item)
    {
        Placement? p = Find(item);
        return p != null && _items.Remove(p);
    }

    public float WeightLb
    {
        get
        {
            float total = 0f;
            foreach (Placement p in _items)
                total += p.Item.WeightLb;
            return total;
        }
    }

    public bool Contains(Item item)
    {
        foreach (Placement p in _items)
        {
            if (p.Item == item || p.Item.Contains(item))
                return true;
        }
        return false;
    }

    public Dictionary SaveState()
    {
        var items = new Array<Dictionary>();
        foreach (Placement p in _items)
        {
            Dictionary entry = p.Item.SaveState();
            entry["x"] = p.X;
            entry["y"] = p.Y;
            entry["rotated"] = p.Rotated;
            items.Add(entry);
        }
        return new Dictionary { ["items"] = items };
    }

    public void LoadState(Dictionary state)
    {
        _items.Clear();
        foreach (Variant v in state["items"].AsGodotArray())
        {
            Dictionary entry = v.AsGodotDictionary();
            Item? item = Item.Load(entry);
            if (item != null)
                _items.Add(new Placement { Item = item, X = entry["x"].AsInt32(), Y = entry["y"].AsInt32(), Rotated = entry["rotated"].AsBool() });
        }
    }
}
