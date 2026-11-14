using Godot;
using LifeVerse.Client.Scripts.Interface;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 0 of the interface: the scale (automatic and manual), the per-account settings file and the migration of the old per-machine viewer.cfg.</summary>
public partial class InterfaceSettingsSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            // Automático: 80% at 1080p, proportional, within 75%-150%
            Require(Mathf.IsEqualApprox(InterfaceScale.Auto(1920, 1080), .8f), "1080p is the usual 80%");
            Require(Mathf.IsEqualApprox(InterfaceScale.Auto(1280, 720), 1.08f, .01f) && InterfaceScale.Effective(1280, 720, InterfaceScale.Auto(1280, 720)) >= .72f - .001f, "a small screen raises the factor so the interface stays readable");
            Require(Mathf.IsEqualApprox(InterfaceScale.Auto(2560, 1440), .8f), "1440p keeps 80% (the stretch already grows it)");
            Require(Mathf.IsEqualApprox(InterfaceScale.Auto(3840, 2160), .8f) && InterfaceScale.Auto(640, 360) == InterfaceScale.AutoMax, "4K keeps 80% and a tiny window stops at 150%");
            Require(InterfaceScale.Resolve(false, 1.1f, 3840, 2160) == 1.1f && InterfaceScale.Resolve(false, 9f, 1920, 1080) == InterfaceScale.Max, "manual is kept, within limits");
            Require(InterfaceScale.MenuLabels()[0] == "Automático" && InterfaceScale.MenuLabels().Length == 8 && InterfaceScale.MenuLabels()[4] == "100%", "the menu: Automático + 75/85/90/100/110/125/150");
            Require(InterfaceScale.MenuIndex(true, 1f) == 0 && InterfaceScale.MenuIndex(false, 1f) == 4 && InterfaceScale.MenuIndex(false, .86f) == 2, "the menu entry follows the settings");

            // per account, and the legacy migration
            var legacy = "user://smoke_legacy_viewer.cfg"; var old = new ConfigFile(); old.SetValue("ui", "scale", 1.25); old.Save(legacy);
            var a = InterfaceSettings.Load("Ana", "user://smoke_interface_ana.cfg", legacy);
            Require(!a.AutoScale && Mathf.IsEqualApprox(a.Scale, 1.25f), "the first time, the old scale seeds the account");
            a.AutoScale = true; a.Mode = "compact"; a.WindowOpacity = .5f; a.Save();
            var again = InterfaceSettings.Load("Ana", "user://smoke_interface_ana.cfg", legacy);
            Require(again.AutoScale && again.Mode == "compact" && Mathf.IsEqualApprox(again.WindowOpacity, .5f), "the account's choices persist");
            var other = InterfaceSettings.Load("Beto", "user://smoke_interface_beto.cfg", null);
            Require(other.AutoScale && other.Mode == "full", "another account starts clean, not with Ana's choices");
            Require(InterfaceSettings.PathFor("Ana Maria/../x") == "user://interface_ana_maria____x.cfg" && InterfaceSettings.PathFor("") == "user://interface_guest.cfg", "account names make safe file names");

            foreach (var f in new[] { legacy, "user://smoke_interface_ana.cfg", "user://smoke_interface_beto.cfg" }) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(f));
            GD.Print("event=interface_settings_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=interface_settings_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
