using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>Where the rest of the game finds "the" build editor: the buttons, the context menu, the inventory and the keyboard go through here. There is one editor, <see cref="BuildEditorController"/>.</summary>
public static class BuildHost
{
    public const string NodeName = "BuildEditorController";

    public static BuildEditorController? Editor(Node world) => world.GetNodeOrNull<BuildEditorController>(NodeName);

    /// <summary>Makes sure the world holds the build editor (it is created the first time it is needed).</summary>
    public static BuildEditorController Ensure(Node world)
    {
        if (Editor(world) is { } existing) return existing;
        var editor = new BuildEditorController { Name = NodeName }; world.AddChild(editor); return editor;
    }

    public static bool IsActive(Node world) => Editor(world)?.Active ?? false;

    public static void SetActive(Node world, bool active) => Editor(world)?.SetActive(active);

    public static void EditObject(Node world, Guid id, BuildGizmoMode? mode) => Editor(world)?.EditObject(id, mode);

    public static void RunCommand(Node world, string command) => Editor(world)?.RunCommand(command);

    public static void SelectFromContext(Node world, Guid id) => Editor(world)?.SelectFromContext(id);

    public static void DeleteOwnedObject(Node world, Guid id) => Editor(world)?.DeleteOwnedObject(id);

    public static void StartPlacing(Node world)
    {
        if (Editor(world) is { } editor) { editor.SetActive(true); editor.SetTool(BuildTool.Create); }
    }

    public static void CreateMeshFromAsset(Node world, Guid assetId, string name, Vector3 position) => Editor(world)?.CreateMeshFromAsset(assetId, name, position);

    /// <summary>A construction of the inventory: the preview follows the mouse and a click places it (one that cannot be copied is placed by the server where <paramref name="fallback"/> is).</summary>
    public static void PreviewPackage(Node world, Guid itemId, Vector3 fallback) => Editor(world)?.Packages.PreviewPackage(itemId, fallback);

    /// <summary>The editing camera has the mouse (right button orbits).</summary>
    public static bool OwnsTheMouse(Node world) => Editor(world)?.OwnsTheMouse ?? false;
}
