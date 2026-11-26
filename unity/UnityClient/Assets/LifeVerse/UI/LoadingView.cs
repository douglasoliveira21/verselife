using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The loading screen while the island opens (as the teleport screen of the viewer, over the picture of the world): what is happening in Portuguese (asking for the entry, waiting for the island to start, loading the world, connecting), a bar that
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
            name = "loading"; style.flexGrow = 1; style.alignItems = Align.Center; style.justifyContent = Justify.FlexEnd; style.backgroundColor = new Color(.03f, .06f, .08f, .98f); pickingMode = PickingMode.Position;
            var picture = Resources.Load<Texture2D>("LifeVerse/Login/background");
            if (picture != null) { style.backgroundImage = picture; style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover); style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center); style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center); style.unityBackgroundImageTintColor = new Color(.45f, .48f, .55f, 1f); }
            var logo = UiKit.Text("LifeVerse", "lv-brand"); logo.style.marginBottom = StyleKeyword.Auto; logo.style.marginTop = StyleKeyword.Auto; Add(logo);
            var panel = UiKit.Glass(); panel.AddToClassList("lv-card"); panel.style.width = 560; panel.style.alignItems = Align.Center; panel.style.marginBottom = 42; Add(panel);
            _status = UiKit.Text("", "lv-bold"); _status.style.marginBottom = 10; _status.style.whiteSpace = WhiteSpace.Normal; _status.style.unityTextAlign = TextAnchor.MiddleCenter; panel.Add(_status);
            var bar = new VisualElement(); bar.AddToClassList("lv-progress"); bar.style.width = 500; _fill = new VisualElement(); _fill.AddToClassList("lv-progress-fill"); bar.Add(_fill); panel.Add(bar);
            _hint = UiKit.Text(Hints[Random.Range(0, Hints.Length)], "lv-muted", "lv-small"); _hint.style.marginTop = 12; panel.Add(_hint);
            Back = UiKit.Button("Voltar", () => { }, "lv-btn-secondary"); Back.name = "back"; Back.style.marginTop = 12; Back.style.minWidth = 160; Back.style.display = DisplayStyle.None; panel.Add(Back);
        }

        /// <summary>The Portuguese words and how far along the opening is, from the status of the join: requesting_ticket, waiting_island n/m, loading_world, connecting, in_island, failed.</summary>
        public static (string Text, float Progress) Describe(string status)
        {
            if (status == null) return ("Preparando…", 0f);
            if (status.StartsWith("waiting_island")) { var rest = status.Substring("waiting_island".Length).Trim(); return ("Iniciando a ilha… " + rest, .3f); }
            switch (status)
            {
                case "requesting_ticket": return ("Pedindo a entrada na ilha…", .12f);
                case "loading_world": return ("Carregando o mundo…", .45f);
                case "loading_assets": return ("Baixando os recursos da ilha…", .6f);
                case "connecting": return ("Conectando à ilha…", .8f);
                case "ready": return ("Pronto", 1f);
                case "in_island": return ("Entrando…", 1f);
                case "failed": return ("Não foi possível entrar.", 0f);
                default: return ("Preparando…", 0f);
            }
        }

        public void SetStatus(string status, string failure = null, string detail = null)
        {
            var (text, progress) = Describe(status); Failed = status == "failed";
            _status.text = Failed && !string.IsNullOrEmpty(failure) ? failure : !string.IsNullOrEmpty(detail) ? detail : text; _status.EnableInClassList("lv-bad", Failed); Progress = progress;
            _fill.style.width = Length.Percent(progress * 100f); Back.style.display = Failed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
