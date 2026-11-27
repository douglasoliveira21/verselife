using System;
using System.Collections.Generic;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The hour and the weather as this person wants to see them: only on their own screen, nobody else notices. "Da ilha" goes back to what the island (or the parcel) says.</summary>
    public sealed class LocalSkyWindow : FloatingWindow
    {
        public static readonly (string Name, float? Hour)[] Hours = { ("Da ilha", null), ("Nascer do sol", 6.3f), ("Meio-dia", 12f), ("Pôr do sol", 17.7f), ("Meia-noite", 0f) };
        public static readonly (string Name, string Weather)[] Weathers = { ("Da ilha", null), ("Limpo", "clear"), ("Nublado", "cloudy"), ("Chuva", "rain"), ("Tempestade", "storm"), ("Neblina", "fog") };
        private readonly Func<IslandEnvironment> _environment;
        private readonly List<Button> _hours = new List<Button>(), _weathers = new List<Button>();

        public LocalSkyWindow(UiRoot ui, Func<IslandEnvironment> environment) : base(ui, "local-sky", "Hora e clima só para você", 360f, -1f, new Vector2(430, 110))
        {
            _environment = environment;
            var note = UiKit.Text("Muda só a sua tela, até você sair da ilha. As outras pessoas continuam vendo a ilha como ela está.", "lv-small", "lv-muted"); note.style.whiteSpace = WhiteSpace.Normal; Body.Add(note);
            Body.Add(UiKit.Text("Hora", "lv-bold", "lv-accent")); var hours = Wrapped(); Body.Add(hours);
            for (var i = 0; i < Hours.Length; i++) { var n = i; var button = UiKit.Button(Hours[i].Name, () => ChooseHour(n)); _hours.Add(button); hours.Add(button); }
            Body.Add(UiKit.Text("Clima", "lv-bold", "lv-accent")); var weathers = Wrapped(); Body.Add(weathers);
            for (var i = 0; i < Weathers.Length; i++) { var n = i; var button = UiKit.Button(Weathers[i].Name, () => ChooseWeather(n)); _weathers.Add(button); weathers.Add(button); }
            Mark();
        }

        private static VisualElement Wrapped() { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap; return row; }

        public void ChooseHour(int index) { var e = _environment(); if (e == null || index < 0 || index >= Hours.Length) return; e.LocalHour = Hours[index].Hour; Mark(); }
        public void ChooseWeather(int index) { var e = _environment(); if (e == null || index < 0 || index >= Weathers.Length) return; e.LocalWeather = Weathers[index].Weather; Mark(); }

        private void Mark()
        {
            var e = _environment();
            for (var i = 0; i < _hours.Count; i++) _hours[i].EnableInClassList("lv-btn-primary", e != null && Nullable.Equals(e.LocalHour, Hours[i].Hour));
            for (var i = 0; i < _weathers.Count; i++) _weathers[i].EnableInClassList("lv-btn-primary", e != null && e.LocalWeather == Weathers[i].Weather);
        }
    }

    /// <summary>
    /// "Primeiros passos": a few pages on how to walk, look, talk, build and explore, with the keys the person has now. It opens by itself the first time someone enters an island and again from the Help menu;
    /// closing it with "não mostrar ao conectar" ticked, or after the last page, keeps it from opening by itself.
    /// </summary>
    public sealed class WelcomeWindow : FloatingWindow
    {
        private readonly (string Title, string Text)[] _pages = Pages();
        private readonly Label _title, _text, _count; private readonly Button _back, _next; private readonly Toggle _quiet;
        private bool _readAll;
        public int Page { get; private set; }
        public int PageCount => _pages.Length;
        public string PageText => _text.text;
        public Toggle DoNotShow => _quiet;

        public WelcomeWindow(UiRoot ui, bool done, Action<bool> closed) : base(ui, "welcome", "Primeiros passos", 480f, 380f, new Vector2(400, 110))
        {
            _title = UiKit.Text("", "lv-heading"); Body.Add(_title);
            _text = UiKit.Text("", "lv-body"); _text.style.whiteSpace = WhiteSpace.Normal; _text.style.flexGrow = 1; _text.style.flexShrink = 1; Body.Add(_text);
            _count = UiKit.Text("", "lv-small", "lv-muted"); _back = UiKit.Button("Voltar", () => Show(Page - 1)); _next = UiKit.Button("Próximo", Next, "lv-btn-primary");
            Body.Add(UiKit.Row(_back, UiKit.Spacer(), _count, UiKit.Spacer(), _next));
            _quiet = UiKit.Check("Não mostrar ao conectar", done, _ => { }); Body.Add(_quiet);
            Closed += () => closed?.Invoke(_quiet.value || _readAll);
            Show(0);
        }

        public void Next() { if (Page >= _pages.Length - 1) Close(); else Show(Page + 1); }

        public void Show(int page)
        {
            Page = Mathf.Clamp(page, 0, _pages.Length - 1); _title.text = _pages[Page].Title; _text.text = _pages[Page].Text; _count.text = (Page + 1) + " / " + _pages.Length;
            _back.SetEnabled(Page > 0); var last = Page == _pages.Length - 1; _next.text = last ? "Concluir" : "Próximo"; _readAll |= last;
        }

        public static (string Title, string Text)[] Pages()
        {
            string K(GameAction action) => KeyBindings.KeyName(KeyBindings.Of(action));
            var walk = K(GameAction.Forward) + ", " + K(GameAction.Left) + ", " + K(GameAction.Back) + " e " + K(GameAction.Right);
            return new[]
            {
                ("Bem-vindo ao LifeVerse!", "Este é um mundo que continua existindo: você anda, conversa, constrói e explora ilhas com outras pessoas.\n\nEste guia mostra o essencial. Ele fica em Ajuda > Primeiros passos."),
                ("Andar", walk + " (ou as setas) andam para onde a câmera olha. Um duplo clique no chão leva você até o ponto.\n\n" + K(GameAction.Run) + " corre. " + K(GameAction.Jump) + " pula. " + K(GameAction.Fly) + " liga e desliga o voo, e " + K(GameAction.Descend) + " desce. " + K(GameAction.Sit) + " senta, e " + K(GameAction.Vehicle) + " entra no carro e sai."),
                ("Câmera", "Segure o botão direito do mouse e arraste para girar a câmera; a roda aproxima e afasta.\n\n" + K(GameAction.FirstPerson) + " alterna a primeira pessoa. " + K(GameAction.CameraReset) + " põe a câmera de volta atrás de você. Um duplo clique em uma pessoa ou objeto olha para ela."),
                ("Conversar", "Aperte " + K(GameAction.Chat) + ", escreva e aperte Enter para falar com quem está perto. Esc sai da conversa.\n\nSegure " + K(GameAction.Talk) + " para falar com a voz. Em Pessoas você encontra amigos, grupos e mensagens. " + K(GameAction.Radial) + " abre os gestos."),
                ("Construir e moldar o terreno", K(GameAction.Build) + " abre as ferramentas de construção e " + K(GameAction.Terrain) + " as de terreno.\n\nSó quem é dono, ou tem permissão, pode mudar um lugar."),
                ("Explorar", "O mini-mapa mostra onde você está e o mapa do mundo leva a outras ilhas. F12 tira uma foto sem a interface.\n\nO mar cerca a ilha: entre na água para nadar. Divirta-se!"),
            };
        }
    }
}
