using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The radial menu on its own: opening, hovering, submenus and going back, long lists, closing, and what each kind of target offers.</summary>
public partial class RadialSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var menu = new RadialMenu(); var layer = new CanvasLayer(); AddChild(layer); layer.AddChild(menu);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var log = new List<string>();
            RadialItem Leaf(string name) => new() { Label = name, Run = () => log.Add(name) };

            menu.Open(new Vector2(400, 300), "Alvo", "user", [Leaf("A"), new RadialItem { Label = "B", Children = [Leaf("B1"), Leaf("B2")] }, Leaf("C")]);
            Require(menu.IsOpen && menu.Depth == 1 && menu.Labels.SequenceEqual(["A", "B", "C"]) && menu.Title == "Alvo", "the menu opens with its slices");
            menu.HoverForTesting(new Vector2(0, -150));
            Require(menu.HoverIndexForTesting == 0, "straight up is the first slice");
            menu.HoverForTesting(new Vector2(0, 0));
            Require(menu.HoverIndexForTesting == -1, "the middle disc is no slice");
            menu.PickForTesting(1);
            Require(menu.Depth == 2 && menu.Labels.SequenceEqual(["B1", "B2"]) && menu.Title == "B", "a slice with choices opens a smaller circle");
            menu.BackForTesting();
            Require(menu.Depth == 1 && menu.IsOpen, "the middle goes back one circle");
            menu.PickForTesting(2);
            Require(!menu.IsOpen && log.SequenceEqual(["C"]), "a plain slice runs and closes the menu");

            menu.Open(new Vector2(400, 300), "Muitos", "dots", Enumerable.Range(1, 12).Select(i => Leaf($"I{i}")).ToList());
            Require(menu.Labels.Count == 8 && menu.Labels[7] == "Mais", $"twelve entries become a circle of seven plus Mais: {string.Join(",", menu.Labels)}");
            menu.PickForTesting(7);
            Require(menu.Labels.SequenceEqual(["I8", "I9", "I10", "I11", "I12"]), "Mais opens the rest, nothing is lost");
            menu.BackForTesting(); menu.BackForTesting();
            Require(!menu.IsOpen, "going back from the first circle closes the menu");

            menu.Open(new Vector2(2, 2), "Canto", "map", [Leaf("X")]);
            Require(menu.CenterForTesting.X > 150 && menu.CenterForTesting.Y > 150, $"a menu opened in a corner is moved onto the screen: {menu.CenterForTesting}");
            menu.PickForTesting(5);
            Require(menu.IsOpen, "a slice that does not exist does nothing");
            menu.Close();
            menu.Open(new Vector2(400, 300), "Fora", "map", [new RadialItem { Label = "Desligado", Enabled = false, Run = () => log.Add("nunca") }]);
            menu.PickForTesting(0);
            Require(menu.IsOpen && !log.Contains("nunca"), "a disabled slice cannot be used");
            menu.Close();

            // what each target offers, using only real actions
            var services = new ContextServices { Owns = view => view.State.OwnerUserId == Guid.Empty };
            var mine = new BuildObjectView(); AddChild(mine); mine.Apply(new BuildObjectState { Id = Guid.NewGuid(), Name = "Mesa", PrimitiveType = "cube", OwnerUserId = Guid.Empty });
            var theirs = new BuildObjectView(); AddChild(theirs); theirs.Apply(new BuildObjectState { Id = Guid.NewGuid(), Name = "Vaso", PrimitiveType = "cube", OwnerUserId = Guid.NewGuid() });
            var own = InteractionRegistry.Build(new ObjectTarget(mine), services); var other = InteractionRegistry.Build(new ObjectTarget(theirs), services);
            Require(own.Any(x => x.Label == "Editar") && !other.Any(x => x.Label == "Editar"), "only your own objects offer editing");
            Require(own.Count <= 8 && other.Any(x => x.Label == "Interagir") && other.Any(x => x.Label == "Fechar"), "everyone can interact with an object and close the menu");
            var edit = own.Single(x => x.Label == "Editar").Children!;
            Require(edit.Select(x => x.Label).SequenceEqual(["Mover", "Girar", "Esticar", "Textura", "Duplicar", "Excluir"]), "the edit circle has the editing tools");
            Require(InteractionRegistry.Build(new GroundTarget(Vector3.Zero), services).Select(x => x.Label).SequenceEqual(["Ir até aqui", "Sobre o terreno", "Construir", "Criar objeto", "Selecionar terreno", "Copiar local", "Fechar"]), "the ground offers walking, building and its location");
            var avatar = InteractionRegistry.Build(new AvatarTarget(2, "Lucas", Vector3.Zero), services);
            Require(avatar.Any(x => x.Label == "Interagir" && x.Children!.Count > 0) && avatar.Any(x => x.Label == "Mensagem") && avatar.Single(x => x.Label == "Mais opções").Children!.Select(x => x.Label).SequenceEqual(["Buscar na lista", "Bloquear", "Denunciar"]), "another avatar offers talking, gestures and moderation");

            // the actions on a person: find the account by name, then ask the API
            var person = new PersonActions(); AddChild(person);
            var lucas = Guid.NewGuid(); var calls = new List<string>(); var said = new List<string>(); person.Said += said.Add;
            person.Transport = (method, path, body) =>
            {
                calls.Add($"{method} {path} {body}");
                if (path.StartsWith("/api/v1/social/people")) return (200, $"[{{\"id\":\"{lucas}\",\"username\":\"lucas\",\"displayName\":\"Lucas Ferreira\",\"relation\":\"none\"}},{{\"id\":\"{Guid.NewGuid()}\",\"username\":\"lucasf2\",\"displayName\":\"Outro\",\"relation\":\"none\"}}]");
                return (200, "{}");
            };
            person.AddFriend("Lucas Ferreira");
            Require(calls.Count == 2 && calls[1].StartsWith("Post /api/v1/social/friends/requests") && calls[1].Contains(lucas.ToString()) && said[^1].Contains("Pedido de amizade"), $"a friend request goes to the account found by name: {string.Join(" | ", calls)} / {string.Join(" | ", said)}");
            person.SendMessage("Lucas Ferreira", "Oi!");
            Require(calls.Count == 3 && calls[2].Contains("/social/messages") && calls[2].Contains("Oi!") && calls[2].Contains(lucas.ToString()), "the second action reuses the account already found: no second search");
            person.Block("Lucas Ferreira"); person.Report("Lucas Ferreira", "spam");
            Require(calls[3].Contains($"/social/blocks/{lucas}") && calls[4].Contains("/social/reports") && calls[4].Contains("spam"), "blocking and reporting use the account id");
            person.Block("Ninguém Assim");
            Require(said[^1].Contains("Não encontrei"), "an unknown name is said plainly and nothing is sent");
            Require(!calls[^1].Contains("/blocks/"), "no block was sent for an unknown name");
            Require(InteractionRegistry.Build(new SelfTarget("Eu"), services).Any(x => x.Label == "Inventário"), "your own avatar offers your things");
            var car = new VehicleView(); AddChild(car); car.Setup(7, "Carro do Rafa", "car");
            var carFree = InteractionRegistry.Build(new VehicleTarget(car), new ContextServices { IsDriving = _ => false }); var carDriven = InteractionRegistry.Build(new VehicleTarget(car), new ContextServices { IsDriving = id => id == 7 });
            Require(carFree.Select(x => x.Label).SequenceEqual(["Entrar", "Inspecionar", "Fechar"]) && carDriven[0].Label == "Sair", "a car offers entering, or leaving when you are driving it");
            var entered = -1; InteractionRegistry.Build(new VehicleTarget(car), new ContextServices { EnterVehicle = id => entered = id })[0].Run!();
            Require(entered == 7, "Entrar asks for this very vehicle");
            Require(new[] { own, other }.All(list => list.Last().Accent), "the last slice is always the orange Fechar");

            GD.Print("event=radial_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=radial_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
