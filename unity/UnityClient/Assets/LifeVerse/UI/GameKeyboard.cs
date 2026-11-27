using System;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// Who gets the keyboard: the game or a field of the interface. The interface of Unity reads W, A, S, D and the arrows as "move the focus" and Enter as "press what has the focus". While
    /// playing those keys are the game's: one step of the avatar after a click on a button would otherwise carry the focus to the chat, and the next ones would be typed there.
    /// </summary>
    public static class GameKeyboard
    {
        /// <summary>Listens on <paramref name="top"/> (the whole panel) and, while <paramref name="playing"/> says so, keeps those keys from moving the focus or pressing anything. Tab and Shift+Tab still walk
        /// through the interface, and an open list of choices keeps its arrows.</summary>
        public static void Guard(VisualElement top, Func<bool> playing)
        {
            top.RegisterCallback<NavigationMoveEvent>(e => { if (e.direction != NavigationMoveEvent.Direction.Next && e.direction != NavigationMoveEvent.Direction.Previous) Keep(top, e, playing); }, TrickleDown.TrickleDown);
            top.RegisterCallback<NavigationSubmitEvent>(e => Keep(top, e, playing), TrickleDown.TrickleDown);
        }

        private static void Keep(VisualElement top, EventBase e, Func<bool> playing)
        {
            if (!playing() || InOpenList(e.target as VisualElement)) return;
            top.focusController?.IgnoreEvent(e); e.StopImmediatePropagation();
        }

        private static bool InOpenList(VisualElement element)
        {
            for (; element != null; element = element.parent) if (element.ClassListContains("unity-base-dropdown")) return true;
            return false;
        }

        /// <summary>Takes the keyboard away from whatever element of the interface holds it.</summary>
        public static void ReleaseFocus(VisualElement root) => (root?.panel?.focusController?.focusedElement as VisualElement)?.Blur();
    }
}
