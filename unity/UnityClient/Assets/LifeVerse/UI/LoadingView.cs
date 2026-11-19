using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The loading screen while the island opens: what is happening in Portuguese (asking for the entry, waiting for the island to start, loading the world, connecting), a bar that
    /// fills as the steps pass and a hint. The words come from the status of <c>WorldJoin</c>; a failure shows its message and a button to go back.
    /// </summary>
    public sealed class LoadingView : VisualElement
    {
        private readonly Label _status, _hint; private readonly VisualElement _fill; public readonly Button Back;
        public float Progress { get; private set; }
        public bool Failed { get; private set; }
        public string StatusText => _status.text;

        private static readonly string[] Hints =
        {
            "Dica: segure o botão direito do mouse para girar a câmera.", "Dica: aperte V para alternar a primeira pessoa.", "Dica: G acena, H dança, J aplaude.", "Dica: aperte F para voar e C para descer.", "Dica: E entra e sai de um carro.",
        };

        public LoadingView()
        {
            name = "loading"; style.flexGrow = 1; style.alignItems = Align.Center; style.justifyContent = Justify.Center; style.backgroundColor = new Color(.03f, .06f, .08f, .98f); pickingMode = PickingMode.Position;
            var logo = UiKit.Text("LifeVerse", "lv-title-text", "lv-accent"); logo.style.fontSize = 44; logo.style.marginBottom = 18; Add(logo);
            _status = UiKit.Text("", "lv-muted"); _status.style.marginBottom = 10; Add(_status);
            var bar = new VisualElement(); bar.AddToClassList("lv-progress"); bar.style.width = 360; _fill = new VisualElement(); _fill.AddToClassList("lv-progress-fill"); bar.Add(_fill); Add(bar);
            _hint = UiKit.Text(Hints[Random.Range(0, Hints.Length)], "lv-dim", "lv-small"); _hint.style.marginTop = 18; Add(_hint);
            Back = UiKit.Button("Voltar", () => { }, "lv-btn-secondary"); Back.name = "back"; Back.style.marginTop = 16; Back.style.display = DisplayStyle.None; Add(Back);
        }

        /// <summary>The Portuguese words and how far along the opening is, from the status of the join: requesting_ticket, waiting_island n/m, loading_world, connecting, in_island, failed.</summary>
        public static (string Text, float Progress) Describe(string status)
        {
            if (status == null) return ("Preparando…", 0f);
            if (status.StartsWith("waiting_island")) { var rest = status.Substring("waiting_island".Length).Trim(); return ("Iniciando a ilha… " + rest, .3f); }
            switch (status)
            {
                case "requesting_ticket": return ("Pedindo a entrada na ilha…", .12f);
                case "loading_world": return ("Carregando o mundo…", .55f);
                case "connecting": return ("Conectando à ilha…", .8f);
                case "ready": return ("Pronto", 1f);
                case "in_island": return ("Entrando…", 1f);
                case "failed": return ("Não foi possível entrar.", 0f);
                default: return ("Preparando…", 0f);
            }
        }

        public void SetStatus(string status, string failure = null)
        {
            var (text, progress) = Describe(status); Failed = status == "failed";
            _status.text = Failed && !string.IsNullOrEmpty(failure) ? failure : text; _status.EnableInClassList("lv-bad", Failed); Progress = progress;
            _fill.style.width = Length.Percent(progress * 100f); Back.style.display = Failed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
