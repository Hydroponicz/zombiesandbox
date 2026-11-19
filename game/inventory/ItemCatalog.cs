using System.Collections.Generic;
using Godot;

namespace ZombieSandbox.Inventory;

/// <summary>
/// Looks up item data by what it is: e.g. the item for a weapon scene (an NPC's gun becomes loot).
/// Scans the items folder once.
/// </summary>
public static class ItemCatalog
{
    private const string ItemsFolder = "res://game/inventory/items";
    private static Dictionary<string, ItemData>? _byWeaponScene;

    /// <summary>The item whose weapon scene is <paramref name="scenePath"/> (null if none).</summary>
    public static ItemData? ForWeaponScene(string scenePath)
    {
        if (_byWeaponScene == null)
        {
            _byWeaponScene = new Dictionary<string, ItemData>();
            foreach (string file in DirAccess.GetFilesAt(ItemsFolder))
            {
                // Exported builds list resources as "name.tres.remap".
                string name = file.EndsWith(".remap") ? file[..^6] : file;
                if (!name.EndsWith(".tres"))
                    continue;
                if (GD.Load<ItemData>($"{ItemsFolder}/{name}") is { WeaponScene: { } scene } data)
                    _byWeaponScene[scene.ResourcePath] = data;
            }
        }
        return _byWeaponScene.TryGetValue(scenePath, out ItemData? found) ? found : null;
    }
}
