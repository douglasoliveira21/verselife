using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>Order and visibility of the bottom toolbar's buttons (the same rules as the Godot client's <c>ToolbarLayout</c>).</summary>
    public static class ToolbarLayout
    {
        /// <summary>The ids in the order to show them: the saved order first (unknown ids ignored), then the buttons it does not mention, in the default order.</summary>
        public static List<string> Order(IReadOnlyList<string> defaults, IReadOnlyList<string> saved)
        {
            var result = new List<string>(); foreach (var id in saved ?? Array.Empty<string>()) if (defaults.Contains(id) && !result.Contains(id)) result.Add(id);
            foreach (var id in defaults) if (!result.Contains(id)) result.Add(id); return result;
        }

        /// <summary>Moves one id a step (-1 left, +1 right).</summary>
        public static List<string> Move(IReadOnlyList<string> order, string id, int step)
        {
            var list = order.ToList(); var at = list.IndexOf(id); if (at < 0) return list; var to = Mathf.Clamp(at + step, 0, list.Count - 1); if (to == at) return list;
            list.RemoveAt(at); list.Insert(to, id); return list;
        }

        /// <summary>The toolbar keeps at least one button: hiding the last visible one is refused.</summary>
        /// <summary>The order after a button is dragged onto another: it takes the place of the one it was dropped on (the others shift).</summary>
        public static List<string> Place(IReadOnlyList<string> order, string id, string onto)
        {
            var list = order.ToList(); if (id == onto || !list.Contains(id) || !list.Contains(onto)) return list;
            var to = list.IndexOf(onto); list.Remove(id); list.Insert(Math.Min(to, list.Count), id); return list;
        }

        public static bool CanHide(IReadOnlyList<string> all, IReadOnlyCollection<string> hidden, string id) => all.Count(x => x != id && !hidden.Contains(x)) > 0;

        public static List<string> Split(string text) => (text ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
    }

    /// <summary>The islands visited in this session, to go back and forward like a browser (at most 20).</summary>
    public sealed class IslandHistory
    {
        public const int Max = 20;
        private readonly List<Guid> _list = new List<Guid>(); private int _index = -1;
        public IReadOnlyList<Guid> Islands => _list;
        public Guid Current => _index >= 0 ? _list[_index] : Guid.Empty;
        public bool CanBack => _index > 0;
        public bool CanForward => _index >= 0 && _index < _list.Count - 1;

        /// <summary>The island a back (or forward) would go to; empty when there is none. The history moves only when the person arrives (<see cref="Visit"/>), so a trip that fails leaves it as it was.</summary>
        public Guid PeekBack => CanBack ? _list[_index - 1] : Guid.Empty;
        public Guid PeekForward => CanForward ? _list[_index + 1] : Guid.Empty;

        /// <summary>An island was entered: the one the history points at changes nothing; the one just behind or ahead is a back or forward; otherwise what was ahead is dropped and it goes on top.</summary>
        public void Visit(Guid island)
        {
            if (island == Guid.Empty || Current == island) return;
            if (CanBack && _list[_index - 1] == island) { _index--; return; }
            if (CanForward && _list[_index + 1] == island) { _index++; return; }
            if (_index < _list.Count - 1) _list.RemoveRange(_index + 1, _list.Count - _index - 1);
            _list.Add(island); if (_list.Count > Max) _list.RemoveAt(0); _index = _list.Count - 1;
        }

    }

    /// <summary>Where the pictures of the world are kept and what they are called ("LifeVerse 2026-11-26 07-30-12.png").</summary>
    public static class WorldPhotos
    {
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) is { Length: > 0 } pictures ? pictures : Application.persistentDataPath, "LifeVerse");
        public static string NameFor(DateTime when, string format) => "LifeVerse " + when.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture) + (format == "jpg" ? ".jpg" : ".png");
        public static byte[] Encode(Texture2D picture, string format) => format == "jpg" ? picture.EncodeToJPG(92) : picture.EncodeToPNG();
    }

    /// <summary>Which avatar a ray of the screen points at: the nearest person whose body (a capsule from the feet to the head) the ray passes within reach of.</summary>
    public static class AvatarPick
    {
        public static int Pick(Ray ray, IEnumerable<(int Peer, Vector3 Feet)> people, float maxDistance = 60f, float radius = .45f, float height = 1.9f)
        {
            var best = -1; var bestAlong = float.MaxValue;
            foreach (var (peer, feet) in people)
            {
                var a = feet + Vector3.up * .2f; var b = feet + Vector3.up * (height - .2f); var (along, gap) = Closest(ray, a, b);
                if (along > 0f && along < maxDistance && gap < radius && along < bestAlong) { best = peer; bestAlong = along; }
            }
            return best;
        }

        /// <summary>How far along the ray its closest approach to a segment is, and how close it gets.</summary>
        public static (float Along, float Gap) Closest(Ray ray, Vector3 a, Vector3 b)
        {
            var best = (Along: 0f, Gap: float.MaxValue);
            for (var i = 0; i <= 10; i++)
            {
                var p = Vector3.Lerp(a, b, i / 10f); var along = Vector3.Dot(p - ray.origin, ray.direction); var gap = Vector3.Distance(ray.origin + ray.direction * along, p);
                if (gap < best.Gap) best = (along, gap);
            }
            return best;
        }
    }
}
