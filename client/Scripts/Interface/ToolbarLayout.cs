namespace LifeVerse.Client.Scripts.Interface;

/// <summary>Order and visibility of the bottom toolbar's buttons: pure rules, so they can be tested without the interface.</summary>
public static class ToolbarLayout
{
    /// <summary>The ids in the order to show them: the person's saved order first (ignoring unknown ids), then any button the saved order does not mention, in the default order.</summary>
    public static List<string> Order(IReadOnlyList<string> defaults, IReadOnlyList<string> saved)
    {
        var result = new List<string>();
        foreach (var id in saved) if (defaults.Contains(id) && !result.Contains(id)) result.Add(id);
        foreach (var id in defaults) if (!result.Contains(id)) result.Add(id);
        return result;
    }

    /// <summary>Moves one id a step (-1 left, +1 right) inside the order; returns the new order.</summary>
    public static List<string> Move(IReadOnlyList<string> order, string id, int step)
    {
        var list = order.ToList(); var at = list.IndexOf(id); if (at < 0) return list;
        var to = Math.Clamp(at + step, 0, list.Count - 1); if (to == at) return list;
        list.RemoveAt(at); list.Insert(to, id); return list;
    }

    /// <summary>The toolbar must keep at least one button: hiding the last visible one is refused.</summary>
    public static bool CanHide(IReadOnlyList<string> all, IReadOnlyCollection<string> hidden, string id) => all.Count(x => x != id && !hidden.Contains(x)) > 0;
}
