using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LifeVerse.Unity.Audio
{
    /// <summary>The microphones of this computer and the one the person chose ("auto" is the system default). A device that is no longer connected falls back to the default. The sound output has no picker: the game follows the system's default output (the engine does not let a program pick another), and the settings page says so.</summary>
    public static class MicDevices
    {
        public const string Auto = "auto";
        /// <summary>What the person chose (set from the settings).</summary>
        public static string Preferred { get; set; } = Auto;
        /// <summary>Tests replace the list of devices.</summary>
        public static System.Func<string[]> Lister { get; set; }

        public static string[] Names() { var all = Lister != null ? Lister() : Microphone.devices; return all.Where(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 120).Distinct().Take(16).ToArray(); }

        /// <summary>The choices of the dropdown: the system default first, then each microphone by name.</summary>
        public static List<string> Choices() { var list = new List<string> { Auto }; list.AddRange(Names()); return list; }

        public static string Label(string choice) => choice == Auto ? "Padrão do sistema" : choice;

        /// <summary>The device to open: the chosen one if it is there, otherwise the default (null means the system default to the engine).</summary>
        public static string Pick() { var names = Names(); if (Preferred != Auto && names.Contains(Preferred)) return Preferred; return null; }
    }
}
