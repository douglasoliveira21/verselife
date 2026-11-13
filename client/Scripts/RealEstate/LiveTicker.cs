using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The clock behind "live" windows: says when it is time to look at the server again. It only runs while its window is open and
/// never fires with a dialog open on top of the window, so a refresh cannot pull the rug from under somebody typing a price.
/// </summary>
public sealed class LiveTicker(double every)
{
    private double _left = every;

    /// <summary>Seconds between looks at the server.</summary>
    public double Every { get; set; } = every;

    /// <summary>Advances the clock; true when it is time to look. A closed window keeps the clock at the start.</summary>
    public bool Due(double delta, bool open)
    {
        if (!open) { _left = Every; return false; }
        _left -= delta; if (_left > 0) return false;
        _left = Every; return true;
    }

    /// <summary>True while a confirmation dialog (sale, rent, edit...) is open on the window.</summary>
    public static bool DialogOpen(Node panel) => panel.GetChildren().OfType<Window>().Any(w => w.Visible);
}
