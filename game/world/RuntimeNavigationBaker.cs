using Godot;

namespace ZombieSandbox.World;

/// <summary>
/// Bakes this region's navigation mesh from its child geometry when the level loads, so level edits
/// never leave stale pathing data. (Streaming chunks will bake or load their own in step 8.)
/// CSG level geometry builds itself a frame after loading, so the bake waits for that.
/// Note: project setting navigation/world/region_use_async_iterations is off — with it on, a mesh
/// assigned right after load never reached the server (Godot 4.7.2). Revisit for chunk streaming.
/// </summary>
public partial class RuntimeNavigationBaker : NavigationRegion3D
{
    public bool IsBaked { get; private set; }

    /// <summary>True once a level's navigation has been baked and the navigation map has picked it up.</summary>
    public static bool MapReady { get; private set; }

    public override async void _Ready()
    {
        // Collision shapes only: reading visual meshes back from the GPU at runtime is slow.
        NavigationMesh.GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        // Bake into a fresh mesh and assign it: re-baking the region's own resource in place
        // doesn't reliably reach the navigation server.
        var mesh = (NavigationMesh)NavigationMesh.Duplicate();
        var source = new NavigationMeshSourceGeometryData3D();
        NavigationServer3D.ParseSourceGeometryData(mesh, source, this);
        NavigationServer3D.BakeFromSourceGeometryData(mesh, source);
        NavigationMesh = mesh;
        IsBaked = true;
        // The navigation map syncs on the next physics frame.
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        MapReady = true;
    }

    public override void _ExitTree() => MapReady = false;
}
