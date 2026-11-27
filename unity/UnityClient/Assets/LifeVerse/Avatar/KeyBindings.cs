using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

namespace LifeVerse.Unity.Avatar
{
    public enum GameAction { Forward, Back, Left, Right, Jump, Run, Descend, Fly, Sit, Vehicle, FirstPerson, Radial, Chat, Performance, Build, Terrain, Talk, Wave, Dance, Applaud, Point, Greet, Laugh, CameraReset }

    /// <summary>
    /// The keys of the game and what each does. Every action has one key the person can change (the arrow keys and the left Ctrl stay as extra keys for walking and going down). Choosing a key that another action already uses swaps the two, so nothing
    /// is ever left without a key and no key does two things. Game keys do nothing while the person is typing (the chat, a field of a window).
    /// </summary>
    public static class KeyBindings
    {
        private static readonly Dictionary<GameAction, Key> Defaults = new Dictionary<GameAction, Key>
        {
            [GameAction.Forward] = Key.W, [GameAction.Back] = Key.S, [GameAction.Left] = Key.A, [GameAction.Right] = Key.D, [GameAction.Jump] = Key.Space, [GameAction.Run] = Key.LeftShift, [GameAction.Descend] = Key.C, [GameAction.Fly] = Key.F,
            [GameAction.Sit] = Key.X, [GameAction.Vehicle] = Key.E, [GameAction.FirstPerson] = Key.V, [GameAction.Radial] = Key.Q, [GameAction.Chat] = Key.Enter, [GameAction.Performance] = Key.F3, [GameAction.Build] = Key.B, [GameAction.Terrain] = Key.T,
            [GameAction.CameraReset] = Key.R, [GameAction.Talk] = Key.Y, [GameAction.Wave] = Key.G, [GameAction.Dance] = Key.H, [GameAction.Applaud] = Key.J, [GameAction.Point] = Key.K, [GameAction.Greet] = Key.L, [GameAction.Laugh] = Key.N,
        };
        private static readonly Dictionary<GameAction, Key> Current = new Dictionary<GameAction, Key>(Defaults);
        private static readonly Dictionary<GameAction, Key[]> Extra = new Dictionary<GameAction, Key[]> { [GameAction.Forward] = new[] { Key.UpArrow }, [GameAction.Back] = new[] { Key.DownArrow }, [GameAction.Left] = new[] { Key.LeftArrow }, [GameAction.Right] = new[] { Key.RightArrow }, [GameAction.Descend] = new[] { Key.LeftCtrl } };

        /// <summary>True while the person is typing somewhere: game keys are then ignored. Set by the game (the chat, a field in focus).</summary>
        public static Func<bool> Blocked { get; set; }
        /// <summary>Tests replace the keyboard.</summary>
        public static Func<Keyboard> KeyboardSource { get; set; }
        private static Keyboard Board => KeyboardSource != null ? KeyboardSource() : Keyboard.current;

        public static IEnumerable<GameAction> All => (GameAction[])Enum.GetValues(typeof(GameAction));
        public static Key Of(GameAction action) => Current[action];
        public static Key DefaultOf(GameAction action) => Defaults[action];
        public static bool IsDefault => All.All(a => Current[a] == Defaults[a]);

        public static string Label(GameAction action)
        {
            switch (action)
            {
                case GameAction.Forward: return "Andar para a frente"; case GameAction.Back: return "Andar para trás"; case GameAction.Left: return "Andar para a esquerda"; case GameAction.Right: return "Andar para a direita"; case GameAction.Jump: return "Pular";
                case GameAction.Run: return "Correr"; case GameAction.Descend: return "Descer (voando ou na água)"; case GameAction.Fly: return "Voar"; case GameAction.Sit: return "Sentar ou levantar"; case GameAction.Vehicle: return "Entrar ou sair do carro";
                case GameAction.FirstPerson: return "Primeira pessoa"; case GameAction.Radial: return "Menu radial"; case GameAction.Chat: return "Abrir o chat"; case GameAction.Performance: return "Desempenho"; case GameAction.Build: return "Modo de construção";
                case GameAction.Terrain: return "Editar terreno"; case GameAction.Talk: return "Falar (segurar)"; case GameAction.Wave: return "Gesto: acenar"; case GameAction.Dance: return "Gesto: dançar"; case GameAction.Applaud: return "Gesto: aplaudir";
                case GameAction.CameraReset: return "Redefinir a câmera"; case GameAction.Point: return "Gesto: apontar"; case GameAction.Greet: return "Gesto: cumprimentar"; default: return "Gesto: rir";
            }
        }

        /// <summary>The name of a key for the screen ("Shift esquerdo", "Espaço").</summary>
        public static string KeyName(Key key)
        {
            switch (key)
            {
                case Key.Space: return "Espaço"; case Key.LeftShift: return "Shift esquerdo"; case Key.RightShift: return "Shift direito"; case Key.LeftCtrl: return "Ctrl esquerdo"; case Key.RightCtrl: return "Ctrl direito"; case Key.LeftAlt: return "Alt esquerdo"; case Key.RightAlt: return "Alt direito";
                case Key.Enter: return "Enter"; case Key.Tab: return "Tab"; case Key.Backquote: return "`"; case Key.UpArrow: return "Seta para cima"; case Key.DownArrow: return "Seta para baixo"; case Key.LeftArrow: return "Seta esquerda"; case Key.RightArrow: return "Seta direita";
                default: return key.ToString();
            }
        }

        /// <summary>Keys that cannot be given to an action: the ones the game reserves (Esc closes things, Delete and the modifiers are the editors') and the ones no keyboard layout means anything by.</summary>
        public static bool Allowed(Key key) => key != Key.None && key != Key.Escape && key != Key.Delete && key != Key.Backspace && key != Key.IMESelected && key != Key.ContextMenu && key != Key.PrintScreen && key != Key.Pause && key != Key.LeftWindows && key != Key.RightWindows && key != Key.F4;

        /// <summary>Gives a key to an action. If another action had it, the two swap. Returns the other action (or null), or false when the key is not allowed.</summary>
        public static bool Set(GameAction action, Key key, out GameAction? swapped)
        {
            swapped = null; if (!Allowed(key)) return false; var other = All.Cast<GameAction?>().FirstOrDefault(a => a.Value != action && Current[a.Value] == key);
            if (other.HasValue) { Current[other.Value] = Current[action]; swapped = other; }
            Current[action] = key; return true;
        }

        public static void Reset() { foreach (var a in All) Current[a] = Defaults[a]; }

        /// <summary>The choices as saved in the settings (action name to key name). Unknown names, keys that are not allowed and repeats are ignored; what is missing keeps its default.</summary>
        public static Dictionary<string, string> Save() => All.Where(a => Current[a] != Defaults[a]).ToDictionary(a => a.ToString(), a => Current[a].ToString());

        private static readonly Key[] Spare = { Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0 };

        public static void Load(IDictionary<string, string> saved)
        {
            Reset(); if (saved == null) return; var used = new HashSet<Key>(); var applied = new HashSet<GameAction>();
            foreach (var pair in saved)
            {
                if (!Enum.TryParse(pair.Key, out GameAction action) || !Enum.IsDefined(typeof(GameAction), action) || !Enum.TryParse(pair.Value, out Key key) || !Allowed(key) || used.Contains(key) || applied.Contains(action)) continue;
                used.Add(key); applied.Add(action); Current[action] = key;
            }
            // an action that kept its default must not share a key with a choice (or with another action): it moves to a free key
            foreach (var a in All.Where(x => !applied.Contains(x)))
            {
                var key = Current[a]; if (used.Contains(key)) key = Spare.First(k => !used.Contains(k) && !Current.Values.Contains(k)); Current[a] = key; used.Add(key);
            }
        }
        private static bool Typing => Blocked != null && Blocked();
        private static bool Down(Keyboard board, Key key) => board != null && key != Key.None && board[key] != null && board[key].isPressed;
        private static bool Edge(Keyboard board, Key key) => board != null && key != Key.None && board[key] != null && board[key].wasPressedThisFrame;

        /// <summary>The key of the action is held (or one of its extra keys).</summary>
        public static bool Held(GameAction action) { var board = Board; if (Typing) return false; if (Down(board, Current[action])) return true; return Extra.TryGetValue(action, out var more) && more.Any(k => Down(board, k)); }
        /// <summary>The key of the action went down this frame (extra keys do not count: they only keep walking and going down).</summary>
        public static bool Pressed(GameAction action) { if (Typing) return false; return Edge(Board, Current[action]); }
    }
}
