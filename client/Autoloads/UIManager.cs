using Godot;

namespace LifeVerse.Client.Autoloads;

public partial class UIManager : Node
{
    private bool _transitioning;

    public async void ShowLogin() => await ChangeScene("res://Scenes/Login.tscn");
    public async void ShowWorld() => await ChangeScene("res://Scenes/World.tscn");
    public async void ShowCharacterCreator() => await ChangeScene("res://Scenes/CharacterCreator.tscn");

    private async Task ChangeScene(string path)
    {
        if (_transitioning) return;
        _transitioning = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var error = GetTree().ChangeSceneToFile(path);
        if (error != Error.Ok) GD.PushError($"event=scene_change_failed path={path} error={error}");
        _transitioning = false;
    }
}
