using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts.ParcelInfo;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The "Sobre este Terreno" window against a pretend server: the header and the eight tabs show what the server sent, the owner edits (data, picture, arrival, offers, access, permissions, objects, music), the tenant renews or leaves,
/// a visitor only reads and may buy or rent what is offered, every change is sent to the right place and shows its answer, and an old server is told apart from a lost connection.
/// </summary>
public partial class ParcelInfoSmoke : Node3D
{
    private static readonly Guid Island = Guid.NewGuid(), Parcel = Guid.NewGuid(), Lease = Guid.NewGuid(), Carla = Guid.NewGuid();

    private sealed class Server
    {
        public bool Owner = true, Tenant, Gone, Down;
        public bool Staff = false;
        public string Name = "Centro Comercial", Description = "Terreno ideal para lojas", Category = "comercial", Image = "", AccessMode = "public", BuildMode = "owner", Teleport = "free";
        public bool Voice = true, Media = true;
        public bool Scripts; public int? AutoReturn; public double? SpawnX, SpawnZ; public double Yaw; public int Revision = 1;
        public long? Sale, Rent; public bool Leased; public string? MusicUrl; public double MusicVolume = .6; public bool Autoplay = true; public string MusicName = "";
        public readonly List<(Guid Id, string Name, string Kind)> Entries = [];
        public readonly List<(Guid Id, string Name, bool Authorized)> Objects = [(Guid.NewGuid(), "Mesa", true), (Guid.NewGuid(), "Cadeira do visitante", false), (Guid.NewGuid(), "Placa", true)];
        public readonly List<string> Calls = [];
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private object View() => new
        {
            parcel = new
            {
                id = Parcel, code = "L-03", name = Name, description = Description, category = Category, area = 1024.0, cells = 64, imageUrl = Image, createdAt = DateTimeOffset.UtcNow.AddDays(-30), revision = Revision, accessMode = AccessMode, buildMode = BuildMode, allowScripts = Scripts, allowVoice = Voice, allowMedia = Media, autoReturnMinutes = AutoReturn,
                teleportMode = Teleport, spawnX = SpawnX, spawnZ = SpawnZ, yaw = Yaw, media = MusicUrl is null ? null : new { musicUrl = MusicUrl, volume = MusicVolume, autoplay = Autoplay, name = MusicName },
            },
            island = new { id = Island, name = "Ilha Tropical", size = 256, coverImageUrl = (string?)null },
            owner = new { id = Guid.NewGuid(), name = Owner ? "AliceVerse" : "Bruno", kind = "person", groupId = (Guid?)null, groupName = (string?)null },
            acquiredAt = DateTimeOffset.UtcNow.AddDays(-10),
            history = new object[] { new { at = DateTimeOffset.UtcNow.AddDays(-10), what = "sale", from = "Bruno", to = "AliceVerse", amount = 60000L }, new { at = DateTimeOffset.UtcNow.AddDays(-30), what = "claim", from = (string?)null, to = "Bruno", amount = 0L } },
            rights = new { canResell = Owner && !Leased, canLease = Owner && !Leased, ownedByIslandOwner = false },
            usage = new { objects = 120, maxObjects = 1000, cost = 360, maxCost = 0, others = 1, builders = Entries.Count(e => e.Kind == "build") },
            sale = Sale is { } s ? new { price = s, exclusive = false, includeObjects = true, description = "Boa esquina" } : null,
            rentOffer = Rent is { } r && !Leased ? new { price = r, periodDays = 30, minPeriods = 1, maxPeriods = 6, deposit = 100L, graceDays = 3, autoRenewAllowed = true } : null,
            rental = Leased && (Owner || Tenant || Staff) ? new { id = Lease, tenant = "Carla", landlord = "AliceVerse", price = 3000L, periodDays = 30, status = "active", startsAt = DateTimeOffset.UtcNow.AddDays(-5), expiresAt = DateTimeOffset.UtcNow.AddDays(25), graceUntil = (DateTimeOffset?)null, deposit = 100L, autoRenew = false, periods = 1 } : null,
            me = new { canManage = Owner, isOwner = Owner, isTenant = Tenant, isStaff = Staff },
        };

        private static (long, string) Ok(object? body = null) => (200, JsonSerializer.Serialize(body ?? new { }, Web));
        private static (long, string) Fail(long status, string error) => (status, JsonSerializer.Serialize(new { error }, Web));

        public Task<(long, string)> Handle(string method, string path, string? body)
        {
            Calls.Add($"{method} {path}");
            if (Down) return Task.FromResult<(long, string)>((0, ""));
            var json = body is null ? null : JsonNode.Parse(body)!.AsObject();
            if (path.StartsWith("/api/v1/social/people", StringComparison.Ordinal)) return Task.FromResult(Ok(new[] { new { id = Carla, username = "carla", displayName = "Carla" } }));
            if (path.EndsWith("/about", StringComparison.Ordinal)) return Task.FromResult(Gone ? (404L, "") : Ok(View()));
            if (method == "PATCH") { if (!Owner) return Task.FromResult(Fail(403, "forbidden")); if (json!["expectedRevision"]!.GetValue<int>() != Revision) return Task.FromResult(Fail(409, "conflict")); Name = json["name"]!.GetValue<string>(); Description = json["description"]!.GetValue<string>(); Revision++; return Task.FromResult(Ok()); }
            var tail = path[(path.IndexOf("/parcels/", StringComparison.Ordinal) + 9 + 36)..]; var rentals = path.Contains("/rentals/", StringComparison.Ordinal);
            if (rentals) { if (path.EndsWith("/end", StringComparison.Ordinal)) { Leased = false; Tenant = false; } return Task.FromResult(Ok()); }
            switch (tail)
            {
                case "/category": { var c = json!["category"]!.GetValue<string>(); if (c == "fabrica") return Task.FromResult(Fail(400, "bad_category")); Category = c; Revision++; return Task.FromResult(Ok()); }
                case "/image": Image = json!["image"]?.GetValue<string>() ?? ""; return Task.FromResult(Ok());
                case "/arrival": SpawnX = json!["x"]?.GetValue<double>(); SpawnZ = json["z"]?.GetValue<double>(); Yaw = json["yaw"]!.GetValue<double>(); Teleport = json["mode"]!.GetValue<string>(); return Task.FromResult(Ok());
                case "/sale": if (method == "DELETE") Sale = null; else { if (Rent is not null) return Task.FromResult(Fail(409, "conflict")); Sale = json!["price"]!.GetValue<long>(); } return Task.FromResult(Ok());
                case "/rental-offer": if (method == "DELETE") Rent = null; else { if (Sale is not null) return Task.FromResult(Fail(409, "conflict")); Rent = json!["pricePerPeriod"]!.GetValue<long>(); } return Task.FromResult(Ok());
                case "/buy": if (json!["expectedPrice"]!.GetValue<long>() != Sale) return Task.FromResult(Fail(409, "price_changed")); Sale = null; Owner = true; return Task.FromResult(Ok());
                case "/rent": Leased = true; Tenant = true; Rent = null; return Task.FromResult(Ok());
                case "/access": if (method == "GET") return Task.FromResult(Ok(new { mode = AccessMode, entries = Entries.Select(e => new { id = e.Id, userId = Carla, userName = e.Name, kind = e.Kind, reason = "", expiresAt = (DateTimeOffset?)null, createdAt = DateTimeOffset.UtcNow }) })); Entries.Add((Guid.NewGuid(), "Carla", json!["kind"]!.GetValue<string>())); return Task.FromResult(Ok());
                case "/access-mode": AccessMode = json!["mode"]!.GetValue<string>(); return Task.FromResult(Ok());
                case "/options": BuildMode = json!["buildMode"]!.GetValue<string>(); Scripts = json["allowScripts"]!.GetValue<bool>(); if (json["allowVoice"] is { } voice) Voice = voice.GetValue<bool>(); if (json["allowMedia"] is { } media) Media = media.GetValue<bool>(); AutoReturn = json["autoReturnMinutes"]?.GetValue<int>(); return Task.FromResult(Ok());
                case "/objects": return Task.FromResult(Ok(Objects.Select(o => new { id = o.Id, name = o.Name, ownerUserId = Guid.NewGuid(), ownerName = o.Authorized ? "AliceVerse" : "Visitante", primitiveType = "cube", cost = 5, hasScripts = false, x = 1, y = 1, z = 1, createdAt = DateTimeOffset.UtcNow, authorized = o.Authorized })));
                case "/objects/return": { var scope = json!["scope"]!.GetValue<string>(); if (string.IsNullOrWhiteSpace(json["reason"]?.GetValue<string>())) return Task.FromResult(Fail(400, "bad_reason")); var ids = json["objectIds"]?.AsArray().Select(x => Guid.Parse(x!.GetValue<string>())).ToList(); Objects.RemoveAll(o => scope == "others" ? !o.Authorized : ids!.Contains(o.Id)); return Task.FromResult(Ok()); }
                case "/media": { var url = json!["musicUrl"]!.GetValue<string>(); if (url.Length == 0) MusicUrl = null; else { if (!url.StartsWith("https://", StringComparison.Ordinal)) return Task.FromResult(Fail(400, "not_https")); MusicUrl = url; MusicVolume = json["volume"]!.GetValue<double>(); Autoplay = json["autoplay"]!.GetValue<bool>(); MusicName = json["name"]!.GetValue<string>(); } return Task.FromResult(Ok()); }
            }
            return Task.FromResult<(long, string)>((404, "{}"));
        }
    }

    public override async void _Ready()
    {
        try
        {
            var server = new Server(); var window = new ParcelInfoWindow { Name = "ParcelInfoWindow", Position = () => (40.5, -12.25, 90) };
            window.UseTransportForTesting(server.Handle); AddChild(window); await Frames(3);

            // --- header and tabs -------------------------------------------------------------------------------------------------------
            await window.OpenAsync(Island, Parcel); await Frames(3);
            Require(window.IsOpen && window.Loaded && window.TitleForTesting == "L-03 — Centro Comercial" && window.SubtitleForTesting == "Ilha Tropical • 1.024 m²", $"the header shows code, name, island and area (got {window.TitleForTesting} / {window.SubtitleForTesting})");
            Require(string.Join("|", window.TabTextsForTesting) == "Geral|Propriedade|Venda e Aluguel|Acesso|Permissões|Objetos|Mídia|Contrato", $"the eight tabs (got {string.Join("|", window.TabTextsForTesting)})");

            // --- Geral: the owner edits -------------------------------------------------------------------------------------------------------
            var text = window.TextForTesting; Require(text.Contains("1.024 m² (64 quadrados)") && text.Contains("AliceVerse") && text.Contains("Ilha Tropical") && text.Contains("1.000") && text.Contains("120") && text.Contains("880") && text.Contains("12%") && text.Contains("No meio do terreno"), $"general data: area, owner, island, prims, arrival (got {text})");
            Require(window.NameFieldForTesting == "Centro Comercial" && window.DescriptionFieldForTesting == "Terreno ideal para lojas" && window.ButtonEnabledForTesting("Salvar Alterações") && window.ButtonEnabledForTesting("Escolher Imagem") && window.ButtonEnabledForTesting("Usar Minha Posição"), "the owner can edit and save");
            window.NameFieldForTesting = "Praça das Lojas"; window.DescriptionFieldForTesting = "Lojas e galerias"; window.ChooseCategoryForTesting("mista"); window.UseMyPositionForTesting(); window.ChooseTeleportForTesting("forced");
            Require(await window.SaveGeneralForTesting(), "saving works"); await Frames(3);
            Require(server.Name == "Praça das Lojas" && server.Description == "Lojas e galerias" && server.Category == "mista" && server.SpawnX == 40.5 && server.SpawnZ == -12.25 && server.Yaw == 90 && server.Teleport == "forced", "name, description, category and arrival were all sent");
            Require(window.TitleForTesting == "L-03 — Praça das Lojas" && window.ToastForTesting.Contains("salvas"), $"the window shows the new name (got {window.TitleForTesting})");
            Require(window.TextForTesting.Contains("x 40,5") || window.TextForTesting.Contains("x 40.5"), "and the arrival point");
            window.NameFieldForTesting = "   "; Require(!await window.SaveGeneralForTesting(), "an empty name is refused"); Require(window.ToastForTesting.Contains("nome"), "and the person is told");
            await window.LoadAsync(); await Frames(2); server.Revision += 5; window.NameFieldForTesting = "Outro Nome"; Require(!await window.SaveGeneralForTesting(), "a stale save is refused"); Require(window.ToastForTesting.Contains("mudou enquanto"), $"and explained (got {window.ToastForTesting})");
            Require(await window.SetImageForTesting("asset:11111111-1111-1111-1111-111111111111"), "setting the picture works"); await Frames(2); Require(server.Image.StartsWith("asset:", StringComparison.Ordinal), "the picture is saved");
            Require(await window.SetImageForTesting(""), "taking it off works"); await Frames(2); Require(server.Image == "", "the picture is gone");

            // --- Propriedade -----------------------------------------------------------------------------------------------------------
            window.SelectTabForTesting("propriedade"); await Frames(2); text = window.TextForTesting;
            Require(text.Contains("AliceVerse") && text.Contains("Pessoa") && text.Contains("Nenhum") && text.Contains("Sim: o dono pode vender") && text.Contains("Venda · de Bruno · para AliceVerse · L$ 60.000") && text.Contains("Terreno criado"), $"ownership and the history of the title (got {text})");

            // --- Venda e Aluguel: the owner --------------------------------------------------------------------------------------------
            window.SelectTabForTesting("venda"); await Frames(2); Require(window.HasButtonForTesting("Colocar à Venda") && window.HasButtonForTesting("Habilitar Aluguel"), "the owner can sell or rent out");
            Require(await window.SellForTesting(90000, true), "selling works"); await Frames(2);
            Require(server.Sale == 90000 && window.TextForTesting.Contains("L$ 90.000") && window.HasButtonForTesting("Retirar da Venda") && window.HasButtonForTesting("Editar Preço") && !window.ButtonEnabledForTesting("Habilitar Aluguel"), "the offer shows and the rent button waits");
            Require(await window.WithdrawSaleForTesting(), "withdrawing works"); await Frames(2); Require(server.Sale is null, "the sale is gone");
            Require(await window.OfferRentForTesting(4000, 30), "offering for rent works"); await Frames(2); Require(server.Rent == 4000 && window.TextForTesting.Contains("L$ 4.000 a cada 30 dias") && window.HasButtonForTesting("Retirar Oferta"), "the rent offer shows");
            Require(!await window.SellForTesting(1000, true), "sale and rent exclude each other"); await Frames(2); Require(window.ToastForTesting.Length > 0, "and the person is told");
            Require(await window.WithdrawRentForTesting(), "withdrawing the rent offer works"); await Frames(2);

            // --- Acesso -----------------------------------------------------------------------------------------------------------------
            window.SelectTabForTesting("acesso"); await Frames(4); Require(window.HasButtonForTesting("Adicionar") && window.TextForTesting.Contains("Ninguém na lista"), "the access tab shows an empty list and the form");
            Require(await window.SetAccessModeForTesting("list"), "changing the mode works"); await Frames(3); Require(server.AccessMode == "list", "the mode is saved");
            Require(await window.AddAccessForTesting("Carla", "build", 60), "adding a person works (found by name)"); await Frames(4); Require(server.Entries.Count == 1 && window.TextForTesting.Contains("Carla · pode construir") && window.HasButtonForTesting("Remover"), "the person is listed");
            window.PressForTesting("Remover"); await Frames(4); Require(server.Calls.Any(c => c.StartsWith("DELETE ", StringComparison.Ordinal) && c.Contains("/access/")), "removing sends the delete");

            // --- Permissões ----------------------------------------------------------------------------------------------------------------
            window.SelectTabForTesting("permissoes"); await Frames(2); Require(window.ButtonEnabledForTesting("Salvar Permissões") && window.TextForTesting.Contains("Construtores na lista"), "the owner sees the rules to change");
            Require(await window.SetOptionsForTesting("everyone", true, 15), "saving the rules works"); await Frames(3); Require(server.BuildMode == "everyone" && server.Scripts && server.AutoReturn == 15, "the rules were sent");
            Require(window.VoiceFieldForTesting && window.MediaFieldForTesting && server.Voice && server.Media, "voice and media are on by default and the toggles show it");
            window.VoiceFieldForTesting = false; window.MediaFieldForTesting = false; window.PressSavePermissionsForTesting(); await Frames(4);
            Require(!server.Voice && !server.Media && server.BuildMode == "everyone", "the two switches go with the save");
            Require(await window.SetOptionsForTesting("owner", false, null), "a save without them changes neither"); await Frames(3); Require(!server.Voice && !server.Media, "an older request leaves them as they were");
            Require(await window.SetOptionsForTesting("everyone", true, 15, voice: true, media: true), "turning them back on works"); await Frames(3); Require(server.Voice && server.Media, "both are on again");

            // --- Objetos -----------------------------------------------------------------------------------------------------------------------
            window.SelectTabForTesting("objetos"); await Frames(4); Require(window.ObjectRowsForTesting == 3 && window.TextForTesting.Contains("120 de 1.000") && window.TextForTesting.Contains("Cadeira do visitante"), "the objects are listed with the prims in use");
            Require(!await window.ReturnSelectedForTesting(), "a return needs a reason"); Require(window.ToastForTesting.Contains("motivo"), "and the person is told");
            window.ReasonForTesting("Fora das regras"); Require(!await window.ReturnSelectedForTesting(), "and something marked"); Require(window.ToastForTesting.Contains("Marque"), "that too");
            window.CheckObjectForTesting(0, true); Require(await window.ReturnSelectedForTesting(), "returning the marked works"); await Frames(4); Require(server.Objects.All(o => o.Name != "Mesa") && window.ObjectRowsForTesting == 2, "the object is gone from the list");
            window.ReasonForTesting("Sem permissão"); Require(await window.ReturnOthersForTesting(), "returning the unauthorized works"); await Frames(4); Require(server.Objects.All(o => o.Authorized), "only the authorized stay");

            // --- Mídia -------------------------------------------------------------------------------------------------------------------------
            window.SelectTabForTesting("midia"); await Frames(2); Require(window.ButtonEnabledForTesting("Salvar Música") && !window.ButtonEnabledForTesting("Remover Música"), "no music yet: only saving");
            Require(!await window.SaveMediaForTesting("http://x/a.mp3", .5, true, "x"), "a music address must be https"); Require(window.ToastForTesting.Length > 0, "and the person is told");
            Require(await window.SaveMediaForTesting("https://cdn.example.com/radio.mp3", .45, false, "Rádio"), "saving the music works"); await Frames(3); Require(server.MusicUrl == "https://cdn.example.com/radio.mp3" && server.MusicName == "Rádio" && !server.Autoplay && Math.Abs(server.MusicVolume - .45) < .001 && window.ButtonEnabledForTesting("Remover Música"), "the music was sent and can be removed");
            Require(await window.SaveMediaForTesting("", .6, true, ""), "removing works"); await Frames(3); Require(server.MusicUrl is null, "the music is gone");

            // --- Contrato: nothing, then an offer, then a tenant --------------------------------------------------------------------------
            window.SelectTabForTesting("contrato"); await Frames(2); Require(window.TextForTesting.Contains("não tem contrato nem oferta"), "no contract, no offer");
            server.Rent = 4000; await window.LoadAsync(); await Frames(2); Require(window.TextForTesting.Contains("Condições da Oferta") && window.TextForTesting.Contains("L$ 4.000 a cada 30 dias") && window.TextForTesting.Contains("3 dia(s)"), "the offer terms are shown");
            server.Leased = true; await window.LoadAsync(); await Frames(2); text = window.TextForTesting;
            Require(text.Contains("Contrato de Aluguel") && text.Contains("Carla") && text.Contains("L$ 3.000") && text.Contains("Em dia") && window.HasButtonForTesting("Encerrar Contrato") && !window.ButtonEnabledForTesting("Encerrar Contrato"), $"the owner sees the contract and cannot end it early (got {text})");
            window.SelectTabForTesting("venda"); await Frames(2); Require(!window.ButtonEnabledForTesting("Habilitar Aluguel") && window.TextForTesting.Contains("Carla"), "while rented the trade tab shows the tenant");

            // --- the tenant ----------------------------------------------------------------------------------------------------------------------
            server.Owner = false; server.Tenant = true; await window.LoadAsync(); window.SelectTabForTesting("contrato"); await Frames(2);
            Require(window.HasButtonForTesting("Renovar") && window.HasButtonForTesting("Sair do Aluguel") && !window.HasButtonForTesting("Encerrar Contrato"), "the tenant can renew or leave");
            Require(await window.RenewForTesting(2), "renewing works"); Require(server.Calls.Any(c => c.Contains("/rentals/") && c.EndsWith("/renew", StringComparison.Ordinal)), "the renew request is sent");
            window.SelectTabForTesting("geral"); await Frames(2); Require(!window.HasButtonForTesting("Salvar Alterações") && window.TextForTesting.Contains("Só o dono do terreno muda"), "the tenant only reads the data");
            Require(await window.LeaveForTesting(), "leaving works"); await Frames(2);

            // --- a visitor ----------------------------------------------------------------------------------------------------------------------------
            server.Owner = false; server.Tenant = false; server.Leased = false; server.Sale = 70000; server.Rent = null; await window.LoadAsync(); window.SelectTabForTesting("venda"); await Frames(2);
            Require(window.HasButtonForTesting("Comprar") && window.HasButtonForTesting("Ver Condições") && !window.HasButtonForTesting("Colocar à Venda"), "a visitor can buy what is for sale, not offer it");
            Require(window.TextForTesting.Contains("L$ 70.000") && window.TextForTesting.Contains("Bruno"), "price and seller are shown");
            window.SelectTabForTesting("acesso"); await Frames(3); Require(!window.HasButtonForTesting("Adicionar") && window.TextForTesting.Contains("O acesso é do dono"), "a visitor does not manage access");
            server.Voice = false; server.Media = false; await window.LoadAsync();
            window.SelectTabForTesting("permissoes"); await Frames(2); Require(!window.HasButtonForTesting("Salvar Permissões") && window.TextForTesting.Contains("Só o dono do terreno muda"), "nor the rules");
            Require(window.TextForTesting.Contains("Desligada neste terreno") && window.TextForTesting.Contains("Não permitida"), "a visitor reads that voice and media are off here");
            server.Voice = true; server.Media = true; await window.LoadAsync();
            window.SelectTabForTesting("objetos"); await Frames(3); Require(window.ObjectRowsForTesting == 0 && window.TextForTesting.Contains("aparece para o dono"), "nor the objects");
            window.SelectTabForTesting("midia"); await Frames(2); Require(!window.HasButtonForTesting("Salvar Música") && window.TextForTesting.Contains("não toca música"), "nor the music");
            window.SelectTabForTesting("venda"); await Frames(2); Require(await window.BuyForTesting(), "buying works"); await Frames(3); Require(server.Sale is null && server.Owner, "the parcel is bought and the window shows the owner's view now");
            server.Owner = false; server.Rent = 800; await window.LoadAsync(); window.SelectTabForTesting("venda"); await Frames(2); Require(window.HasButtonForTesting("Alugar") && !window.HasButtonForTesting("Comprar"), "a rent offer shows Alugar");
            Require(await window.RentForTesting(2), "renting works"); await Frames(3); Require(server.Leased && server.Tenant, "the visitor became the tenant");

            // --- visiting ----------------------------------------------------------------------------------------------------------------------------------
            server.Owner = false; server.Tenant = false; server.Leased = false; await window.LoadAsync(); await Frames(2);
            (Guid Island, Guid Parcel)? went = null; Guid? islandOnly = null; window.VisitParcel = (i, p) => went = (i, p); window.Visit = i => islandOnly = i;
            Require(window.VisitEnabledForTesting, "a public parcel can be visited");
            window.VisitForTesting(); Require(went == (Island, Parcel) && islandOnly is null && !window.IsOpen, "Visitar goes to the parcel and closes the window");
            await window.OpenAsync(Island, Parcel); await Frames(3);
            server.Teleport = "disabled"; await window.LoadAsync(); await Frames(2); went = null;
            window.VisitForTesting(); Require(went is null && islandOnly == Island, "a parcel that takes no teleports sends the person to the island instead");
            await window.OpenAsync(Island, Parcel); await Frames(3); server.Teleport = "free";
            server.AccessMode = "private"; await window.LoadAsync(); await Frames(2); islandOnly = null;
            Require(!window.VisitEnabledForTesting && window.VisitTipForTesting.Contains("privado"), "a private parcel cannot be visited by a stranger");
            window.VisitForTesting(); Require(went is null && islandOnly is null && window.IsOpen, "and pressing does nothing");
            server.Tenant = true; await window.LoadAsync(); await Frames(2); Require(window.VisitEnabledForTesting, "its tenant can");
            server.Tenant = false; server.Owner = true; server.AccessMode = "public"; await window.LoadAsync(); await Frames(2);

            // --- trouble ----------------------------------------------------------------------------------------------------------------------------------
            server.Gone = true; await window.LoadAsync(); await Frames(2); Require(window.NoteForTesting.Contains("ainda não tem os detalhes"), $"an old server is told apart (got {window.NoteForTesting})");
            server.Gone = false; server.Down = true; await window.LoadAsync(); await Frames(2); Require(window.NoteForTesting.Contains("Sem conexão"), "no connection is said");
            server.Down = false; await window.LoadAsync(); await Frames(2); Require(window.NoteForTesting.Length == 0, "trying again recovers");

            foreach (var view in new[] { new Vector2(1280, 720), new Vector2(1366, 768), new Vector2(1920, 1080), new Vector2(2560, 1440) })
            { window.SetViewportForTesting(view); await Frames(2); var size = window.PanelSizeForTesting; Require(size.X <= view.X - 31 && size.Y <= view.Y - 31, $"the window fits {view} (got {size})"); }
            window.SetViewportForTesting(new Vector2(1920, 1080)); await Frames(2);

            if (OS.GetEnvironment("LIFEVERSE_SHOT") is { Length: > 0 } shot)
            {
                GetWindow().Size = new Vector2I(1536, 1024); window.SetViewportForTesting(new Vector2(1536, 1024)); await Frames(6);
                server.Owner = true; server.Tenant = false; server.Leased = false; server.Sale = null; server.Entries.Add((Guid.NewGuid(), "Carla", "allow")); await window.LoadAsync();
                window.SelectTabForTesting(OS.GetEnvironment("LIFEVERSE_SECTION") is { Length: > 0 } section ? section : "geral"); await Frames(10);
                GetViewport().GetTexture().GetImage().SavePng(shot);
            }
            GD.Print("event=parcel_info_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=parcel_info_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
