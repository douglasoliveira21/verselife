using System.Collections.Generic;
using System.Linq;

namespace LifeVerse.Unity.Prototypes.HudDirections
{
    /// <summary>The same made-up moment in the three prototypes, with the names of the functions exactly as the game writes them. The capture compares these lists with the game's own.</summary>
    public static class Model
    {
        public const string Island = "Praça do Farol", Coordinates = "1,  1,  −62", Balance = "1.234 VC", Clock = "17:31", Presence = "Disponível";
        public const int Here = 5, Unread = 2;

        public static readonly (string Label, string Colour)[] Presences = { ("Disponível", "#3ED598"), ("Ausente", "#F4B740"), ("Ocupado", "#EF5B5B"), ("Invisível", "#9AA5B1") };

        public static readonly (string Time, string Who, string Text, bool Mine)[] Near =
        {
            ("17:27", "Beto", "cheguei! que vista boa daqui", false), ("17:28", "Marina", "o quiosque ficou lindo", false), ("17:29", "Você", "vamos até o farol?", true), ("17:30", "Ana", "vamos, te encontro no deck", false),
        };

        public static readonly (string Time, string Who, string Text, bool Mine)[] WithAna =
        {
            ("17:22", "Ana", "você viu a ilha nova do Caio?", false), ("17:23", "Você", "ainda não, me manda o convite", true), ("17:24", "Ana", "mandei agora", false), ("17:29", "Você", "recebi, vou depois do farol", true), ("17:30", "Ana", "combinado", false),
        };

        public static readonly (string Name, int Unread)[] Tabs = { ("Por perto", 0), ("Ana", 0), ("Construtores", 1) };
        public static readonly (string Kind, string Text)[] Notices = { ("good", "Ana aceitou seu convite"), ("info", "Construtores: reunião às 18:00") };

        /// <summary>The buttons of the game's bar, in its default order; the last four start hidden.</summary>
        public static readonly (string Id, string Label, string Key)[] Tools =
        {
            ("chat", "Chat", "Enter"), ("voice", "Voz", ""), ("move", "Mover", ""), ("camera", "Câmera", ""), ("gestures", "Gestos", "Q"), ("photo", "Foto", "F12"), ("build", "Construir", "B"), ("people", "Pessoas", ""),
            ("map", "Mapa", ""), ("minimap", "Mini-mapa", ""), ("inventory", "Inventário", ""), ("search", "Buscar", ""), ("wallet", "Carteira", ""), ("notifications", "Avisos", ""),
            ("social", "Social", ""), ("more", "Mais", ""), ("settings", "Configurações", ""), ("help", "Ajuda", ""),
        };
        public const int DefaultShown = 14;

        public static (string Id, string Label, string Key) Tool(string id) => Tools.First(t => t.Id == id);

        public static readonly (string Title, (string Label, string Key)[] Items)[] Menus =
        {
            ("Eu", new[] { ("Meu perfil", ""), ("Avatar: visuais, corpo, roupas e animações", ""), ("Meu personagem", ""), ("Anexos do corpo", ""), ("Inventário", ""), ("Carteira", ""), ("Minhas propriedades", ""), ("Controles de movimento", ""), ("Controles da câmera", ""), ("Configurações", ""), ("Barra de ferramentas", ""), ("Sair da ilha", "") }),
            ("Comunicar", new[] { ("Conversa local", ""), ("Pessoas", ""), ("Mensagens e amigos", ""), ("Grupos", ""), ("Avisos", ""), ("Gestos", "") }),
            ("Mundo", new[] { ("Mapa do mundo", ""), ("Ir para a Ilha de Boas-vindas", ""), ("Hora e clima só para você", ""), ("Buscar no LifeVerse", ""), ("Eventos, marcos e fotos", ""), ("Sobre a ilha", ""), ("Gerenciar esta ilha", ""), ("Gerenciar terrenos", ""), ("Sobre este terreno", ""), ("Mercado imobiliário", ""), ("Editar terreno", "T"), ("Tirar foto", "F12"), ("Ilha anterior", ""), ("Próxima ilha", "") }),
            ("Conteúdo", new[] { ("Construir", "B"), ("Mercado", ""), ("Ofertas de itens", ""), ("Script de um objeto", "") }),
            ("Ajuda", new[] { ("Primeiros passos", ""), ("Atalhos e controles", ""), ("Manual dos scripts", "") }),
            ("Desenvolver", new[] { ("Desempenho", "F3"), ("Exportar diagnóstico", "") }),
        };

        public static string ItemId(string menu, string label) => "item:" + menu + "/" + label;

        /// <summary>Every function of the game's HUD a prototype has to give a place to.</summary>
        public static IEnumerable<string> Required()
        {
            foreach (var id in new[] { "back", "forward", "island-name", "coords", "about-island", "about-parcel", "open-map", "search", "presence", "balance", "buy", "people-count", "clock", "connection",
                "chat-log", "chat-tabs", "chat-field", "chat-send", "chat-minimize", "chat-close-tab", "minimap", "minimap-zoom-in", "minimap-zoom-out", "minimap-close", "toasts", "name-plates", "toolbar-customize", "toolbar-position", "interface-mode" }) yield return id;
            foreach (var tool in Tools) yield return "tool:" + tool.Id;
            foreach (var (title, items) in Menus) { yield return "menu:" + title; foreach (var (label, _) in items) yield return ItemId(title, label); }
        }
    }
}
