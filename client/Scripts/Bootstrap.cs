using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public partial class Bootstrap : Node
{
    public override void _Ready() => GetNode<UIManager>("/root/UIManager").ShowLogin();
}
