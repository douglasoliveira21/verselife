using System;
using System.Linq;
using System.IO;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class UiTests
    {
        private string _file;
        [TearDown] public void Clean() { if (_file != null && File.Exists(_file)) File.Delete(_file); }

        [Test]
        public void SettingsAreBroughtWithinLimitsAndSurviveASaveAndLoad()
        {
            _file = Path.Combine(Path.GetTempPath(), "lv-settings-" + Guid.NewGuid() + ".json");
            var s = new UserSettings { GraphicsPreset = 9, FpsLimit = 5, MasterVolume = 7f, InterfaceScale = 0f, ChatFontSize = 99, ChatOpacity = float.NaN, MouseSensitivity = -1f, LastLogin = "ana", InvertY = true }; s.Windows["settings"] = new[] { 10f, 20f }; s.Windows["bad"] = new[] { 1f };
            Assert.IsTrue(s.Save(_file)); var back = UserSettings.Load(_file);
            Assert.AreEqual(3, back.GraphicsPreset); Assert.AreEqual(30, back.FpsLimit); Assert.AreEqual(1f, back.MasterVolume); Assert.AreEqual(UserSettings.MinScale, back.InterfaceScale); Assert.AreEqual(28, back.ChatFontSize);
            Assert.AreEqual(.8f, back.ChatOpacity, 1e-5f, "um número inválido volta ao padrão"); Assert.AreEqual(.02f, back.MouseSensitivity, 1e-5f); Assert.AreEqual("ana", back.LastLogin); Assert.IsTrue(back.InvertY);
            Assert.IsTrue(back.Windows.ContainsKey("settings") && !back.Windows.ContainsKey("bad"), "a posição de janela inválida é descartada");
        }

        [Test]
        public void AnUnreadableSettingsFileGivesTheDefaultsAndNeverBreaksTheGame()
        {
            _file = Path.Combine(Path.GetTempPath(), "lv-settings-" + Guid.NewGuid() + ".json"); File.WriteAllText(_file, "{ isto não é json");
            var s = UserSettings.Load(_file); Assert.AreEqual(2, s.GraphicsPreset); Assert.IsTrue(s.AutoScale);
            Assert.AreEqual(2, UserSettings.Load(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid())).GraphicsPreset);
        }

        [Test]
        public void TheInterfaceScaleIsAutomaticFromTheScreenHeightOrChosen()
        {
            var s = new UserSettings { AutoScale = true };
            Assert.AreEqual(1f, s.EffectiveScale(1080f), 1e-5f); Assert.AreEqual(1.5f, s.EffectiveScale(2160f), 1e-5f, "o máximo"); Assert.AreEqual(.75f, s.EffectiveScale(500f), 1e-5f, "o mínimo");
            s.AutoScale = false; s.InterfaceScale = 1.25f; Assert.AreEqual(1.25f, s.EffectiveScale(500f), 1e-5f); s.LargeText = true; Assert.AreEqual(1.25f * 1.15f, s.EffectiveScale(500f), 1e-5f);
        }

        [Test]
        public void TheChatCleansWhatIsSent()
        {
            Assert.IsNull(ChatPanel.Clean("   ")); Assert.IsNull(ChatPanel.Clean(null)); Assert.AreEqual("olá mundo", ChatPanel.Clean("  olá\u0000 mundo\n"));
            Assert.AreEqual(ChatPanel.MaxLength, ChatPanel.Clean(new string('a', 1000)).Length);
        }

        [Test]
        public void TheLoginFormIsCheckedBeforeAnythingIsSent()
        {
            var island = Guid.NewGuid().ToString();
            Assert.IsNotNull(LoginView.Validate(false, "", "", "x", island, out _)); Assert.IsNotNull(LoginView.Validate(false, "ana", "", "", island, out _)); Assert.IsNotNull(LoginView.Validate(false, "ana", "", "senha", "xyz", out _));
            Assert.IsNull(LoginView.Validate(false, "ana", "", "qualquer", island, out var id)); Assert.AreEqual(island, id.ToString());
            Assert.IsNotNull(LoginView.Validate(true, "an", "a@b.co", "Senha123", island, out _), "nome curto"); Assert.IsNotNull(LoginView.Validate(true, "ana", "sem-arroba", "Senha123", island, out _), "e-mail inválido");
            Assert.IsNotNull(LoginView.Validate(true, "ana", "a@b.co", "senha", island, out _), "senha fraca"); Assert.IsNull(LoginView.Validate(true, "ana", "a@b.co", "Senha123", island, out _));
            Assert.IsTrue(LoginView.StrongPassword("Abc123")); Assert.IsFalse(LoginView.StrongPassword("abc123")); Assert.IsFalse(LoginView.StrongPassword("ABC123")); Assert.IsFalse(LoginView.StrongPassword("Abcdef")); Assert.IsFalse(LoginView.StrongPassword("Ab1"));
        }

        [Test]
        public void TheLoadingScreenSaysInPortugueseWhereTheOpeningIs()
        {
            Assert.AreEqual("Pedindo a entrada na ilha…", LoadingView.Describe("requesting_ticket").Text); Assert.IsTrue(LoadingView.Describe("waiting_island 3/15").Text.EndsWith("3/15"));
            Assert.Less(LoadingView.Describe("requesting_ticket").Progress, LoadingView.Describe("loading_world").Progress); Assert.Less(LoadingView.Describe("loading_world").Progress, LoadingView.Describe("connecting").Progress);
            Assert.AreEqual(1f, LoadingView.Describe("in_island").Progress); Assert.AreEqual(0f, LoadingView.Describe("failed").Progress);
        }

        [Test]
        public void EachGraphicsPresetAsksForMoreThanTheOneBelow()
        {
            for (var i = 1; i < GraphicsQuality.Presets.Length; i++) { Assert.GreaterOrEqual(GraphicsQuality.Presets[i].LodBias, GraphicsQuality.Presets[i - 1].LodBias); Assert.GreaterOrEqual(GraphicsQuality.Presets[i].ShadowDistance, GraphicsQuality.Presets[i - 1].ShadowDistance); }
            Assert.IsFalse(GraphicsQuality.Presets[0].Shadows, "Baixo não tem sombras"); Assert.AreEqual("Ultra", GraphicsQuality.Of(99).Name, "um preset fora do intervalo vira o mais alto");
            var before = QualitySettings.lodBias; GraphicsQuality.Apply(new UserSettings { GraphicsPreset = 0, VSync = false, FpsLimit = 60 }); Assert.AreEqual(.6f, QualitySettings.lodBias, 1e-4f); Assert.AreEqual(60, Application.targetFrameRate);
            GraphicsQuality.Apply(new UserSettings()); QualitySettings.lodBias = before;
        }

        [Test]
        public void TheApiRowsAreReadTolerantlyFromListsAndWrappedLists()
        {
            var plain = Json.Rows("[{\"id\":\"11111111-2222-3333-4444-555555555555\",\"name\":\"A\",\"n\":7,\"ok\":true},{\"name\":null}]"); Assert.AreEqual(2, plain.Count);
            Assert.AreEqual("A", plain[0].Text("name")); Assert.AreEqual(7, plain[0].Number("n")); Assert.IsTrue(plain[0].Flag("ok")); Assert.AreNotEqual(Guid.Empty, plain[0].Id()); Assert.AreEqual("fallback", plain[1].Text("name", "fallback")); Assert.AreEqual("x", plain[1].First("a", "b", "name") == "" ? "x" : "y");
            Assert.AreEqual(1, Json.Rows("{\"items\":[{\"a\":1}]}").Count); Assert.AreEqual(1, Json.Rows("{\"transactions\":[{\"a\":1}]}").Count); Assert.AreEqual(0, Json.Rows("{\"a\":1}").Count); Assert.AreEqual(0, Json.Rows("não é json").Count);
            Assert.AreEqual("Dono", Json.One("{\"name\":\"Dono\"}").Text("name"));
        }

        [Test]
        public void TheWalletChecksATransferBeforeAskingTheApi()
        {
            var id = Guid.NewGuid().ToString();
            Assert.IsNotNull(WalletWindow.Validate("", "5", 100, out _, out _)); Assert.IsNotNull(WalletWindow.Validate(id, "abc", 100, out _, out _)); Assert.IsNotNull(WalletWindow.Validate(id, "-3", 100, out _, out _)); Assert.IsNotNull(WalletWindow.Validate(id, "0", 100, out _, out _));
            Assert.AreEqual("Saldo insuficiente.", WalletWindow.Validate(id, "101", 100, out _, out _)); Assert.IsNull(WalletWindow.Validate(id, "100", 100, out var to, out var coins)); Assert.AreEqual(100, coins); Assert.AreEqual(id, to.ToString());
            Assert.IsNull(WalletWindow.Validate(id, "5", -1, out _, out _), "sem saldo conhecido a API decide");
        }

        [Test]
        public void TheLuaEditorFindsSyntaxMistakesWithTheIslandsOwnInterpreter()
        {
            Assert.IsNull(LuaEditorWindow.SyntaxProblem("local x = 1\nif x > 0 then print(x) end")); Assert.IsNotNull(LuaEditorWindow.SyntaxProblem("if x then")); Assert.IsNotNull(LuaEditorWindow.SyntaxProblem("local = 5")); Assert.IsNotNull(LuaEditorWindow.SyntaxProblem(""));
            StringAssert.Contains("2", LuaEditorWindow.SyntaxProblem("local a = 1\nlocal b = = 2"), "a mensagem diz a linha");
            Assert.IsNotNull(LuaEditorWindow.SyntaxProblem(new string('a', LuaEditorWindow.MaxSourceLength + 1)));
        }

        [Test]
        public void TheMapPaintsWaterBlueAndLandByHeight()
        {
            var deep = Minimap.ColourOf(-8f, 0f); var shallow = Minimap.ColourOf(-.5f, 0f); var sand = Minimap.ColourOf(.3f, 0f); var grass = Minimap.ColourOf(4f, 0f); var snow = Minimap.ColourOf(40f, 0f);
            Assert.Greater(shallow.g, deep.g, "mais raso é mais claro"); Assert.Greater(deep.b, deep.r); Assert.Greater(sand.r, sand.b); Assert.Greater(grass.g, grass.r); Assert.Greater(snow.r, 230);
        }

        [Test]
        public void TheRadialMenuKnowsWhichSliceAPointIsIn()
        {
            Assert.AreEqual(-Mathf.PI / 2f, RadialMenu.AngleOf(0, 8), 1e-5f); Assert.AreEqual(0f, RadialMenu.AngleOf(2, 8), 1e-5f);
        }

        [Test]
        public void TheUserNameOfAnAccountFollowsTheApiRule()
        {
            var island = Guid.NewGuid().ToString();
            Assert.IsNotNull(LoginView.Validate(true, "ana maria", "a@b.co", "Senha123", island, out _), "espaço não vale"); Assert.IsNotNull(LoginView.Validate(true, "ana!", "a@b.co", "Senha123", island, out _)); Assert.IsNotNull(LoginView.Validate(true, new string('a', 33), "a@b.co", "Senha123", island, out _));
            Assert.IsNull(LoginView.Validate(true, "ana_2", "a@b.co", "Senha123", island, out _));
        }
        [Test]
        public void TheStarterCharacterIsValidAndTheDraftIsCheckedWithTheApisOwnRules()
        {
            var draft = CharacterDraft.Starter("Ana Maria"); Assert.AreEqual(0, draft.Problems().Count, string.Join("; ", draft.Problems()));
            foreach (var body in new[] { "human_a", "human_b", "lifeverse_man" }) { draft.BaseModel = body; draft.UseStarterOutfit(); Assert.AreEqual(0, draft.Problems().Count, body + ": " + string.Join("; ", draft.Problems())); }
            draft.DisplayName = "ab"; Assert.IsTrue(draft.Problems().Any(p => p.Contains("3 a 64"))); draft.DisplayName = "Ana";
            draft.SkinColor = "vermelho"; Assert.IsTrue(draft.Problems().Any(p => p.Contains("#RRGGBB"))); draft.SkinColor = "#C98F65";
            draft.HairStyle = "inexistente"; Assert.IsTrue(draft.Problems().Any(p => p.Contains("cabelo"))); draft.HairStyle = "short";
            draft.CameraSensitivity = .5f; Assert.IsTrue(draft.Problems().Any(p => p.Contains("sensibilidade"))); draft.CameraSensitivity = .003f;
            draft.Worn.Remove("shoes"); Assert.IsTrue(draft.Problems().Any(p => p.Contains("obrigatórios")), "sem sapatos não vale"); draft.UseStarterOutfit();
            draft.Body["height"] = 3f; Assert.IsTrue(draft.Problems().Any(p => p.Contains("corpo")) || draft.BodyJson.Length > 0, "o JSON do corpo traz o número dentro do limite");
        }

        [Test]
        public void TheRequestAndTheAppearanceJsonCarryWhatTheApiAndTheAvatarRead()
        {
            var draft = CharacterDraft.Starter("Ana Maria"); draft.HairStyle = "ponytail"; draft.SkinColor = "#8D5524";
            using var request = System.Text.Json.JsonDocument.Parse(draft.ToRequest()); var root = request.RootElement;
            Assert.AreEqual("Ana Maria", root.GetProperty("displayName").GetString()); Assert.AreEqual("ponytail", root.GetProperty("hairStyle").GetString()); Assert.IsTrue(root.GetProperty("equippedItems").GetArrayLength() >= 3); Assert.IsTrue(root.TryGetProperty("bodySettingsJson", out _) && root.TryGetProperty("outfitsJson", out _));
            var appearance = LifeVerse.Unity.Avatar.AvatarAppearance.Parse(draft.ToAppearanceJson()); Assert.AreEqual("ponytail", appearance.HairStyle); Assert.AreEqual(0x8D / 255f, appearance.Skin.r, .01f); Assert.AreEqual("Ana Maria", appearance.DisplayName);
            var back = CharacterDraft.FromResponse(draft.ToRequest()); Assert.AreEqual("ponytail", back.HairStyle); Assert.AreEqual(draft.Worn.Count, back.Worn.Count, "ler de volta o que foi enviado");
        }

        [Test]
        public void TheIslandAndTheEventAreCheckedBeforeBeingSent()
        {
            Assert.IsNotNull(IslandManageWindow.ValidateGeneral("ab", "10", "256", out _, out _)); Assert.IsNotNull(IslandManageWindow.ValidateGeneral("Ilha", "300", "256", out _, out _)); Assert.IsNotNull(IslandManageWindow.ValidateGeneral("Ilha", "10", "63", out _, out _));
            Assert.IsNull(IslandManageWindow.ValidateGeneral("Ilha", "64", "256", out var players, out var size)); Assert.AreEqual(64, players); Assert.AreEqual(256, size);
            Assert.IsNotNull(DiscoveryWindow.ValidateEvent("", "10", "1", out _, out _)); Assert.IsNotNull(DiscoveryWindow.ValidateEvent("Festa", "501", "1", out _, out _)); Assert.IsNull(DiscoveryWindow.ValidateEvent("Festa", "50", "0", out _, out _));
            Assert.AreEqual("", DiscoveryWindow.When("lixo"));
        }

        [Test]
        public void ThePresenceWordsAreInPortuguese()
        {
            Assert.AreEqual("online", PersonActionsWindow.PresenceWords("online")); Assert.AreEqual("ausente", PersonActionsWindow.PresenceWords("away")); Assert.AreEqual("ocupada", PersonActionsWindow.PresenceWords("busy")); Assert.AreEqual("offline", PersonActionsWindow.PresenceWords("invisible")); Assert.AreEqual("—", PersonActionsWindow.PresenceWords("?"));
        }

        [Test]
        public void ColoursAreAcceptedOnlyAsHexWithAnOptionalAlpha()
        {
            Assert.IsTrue(Controls.IsColour("#A1b2C3")); Assert.IsTrue(Controls.IsColour("#A1B2C3FF")); Assert.IsFalse(Controls.IsColour("A1B2C3")); Assert.IsFalse(Controls.IsColour("#12345")); Assert.IsFalse(Controls.IsColour(null)); Assert.IsFalse(Controls.IsColour("#GGGGGG"));
        }
        [Test]
        public void TheFilePickerFiltersByExtensionAndTheUploadAnswersBecomeSentences()
        {
            Assert.IsTrue(FilePickerWindow.Matches("foto.PNG", Uploads.ImageFilter)); Assert.IsTrue(FilePickerWindow.Matches("a.jpeg", Uploads.ImageFilter)); Assert.IsFalse(FilePickerWindow.Matches("a.txt", Uploads.ImageFilter)); Assert.IsTrue(FilePickerWindow.Matches("qualquer.coisa", null)); Assert.IsTrue(FilePickerWindow.Matches("x.glb", Uploads.ModelFilter));
            var id = Guid.NewGuid(); var ok = Uploads.Read(201, "{\"id\":\"" + id + "\",\"name\":\"foto\",\"storageKey\":\"k1\"}"); Assert.IsTrue(ok.Ok); Assert.AreEqual(id, ok.AssetId); Assert.AreEqual("k1", ok.StorageKey);
            var wrapped = Uploads.Read(200, "{\"asset\":{\"id\":\"" + id + "\"}}"); Assert.AreEqual(id, wrapped.AssetId);
            StringAssert.Contains("cheio", Uploads.Read(409, "{}").Message); StringAssert.Contains("Empty", Uploads.Read(400, "{\"detail\":\"Empty files are not accepted.\"}").Message); Assert.IsFalse(Uploads.Read(0, "").Ok);
            Assert.AreEqual("image/png", LifeVerse.Unity.World.IslandEntryClient.MimeOf("a.png")); Assert.AreEqual("model/gltf-binary", LifeVerse.Unity.World.IslandEntryClient.MimeOf("m.GLB")); Assert.AreEqual("application/octet-stream", LifeVerse.Unity.World.IslandEntryClient.MimeOf("x.zzz"));
        }

        [Test]
        public void AProductAndTheMusicOfAParcelAreCheckedBeforeBeingSent()
        {
            var asset = Guid.NewGuid().ToString();
            Assert.IsNotNull(MarketWindow.ValidateProduct("", "Cadeira", "10", out _, out _)); Assert.IsNotNull(MarketWindow.ValidateProduct(asset, "ab", "10", out _, out _)); Assert.IsNotNull(MarketWindow.ValidateProduct(asset, "Cadeira", "-1", out _, out _)); Assert.IsNotNull(MarketWindow.ValidateProduct(asset, "Cadeira", "20000000", out _, out _));
            Assert.IsNull(MarketWindow.ValidateProduct(asset, "Cadeira", "0", out _, out var free)); Assert.AreEqual(0, free, "de graça pode");
            Assert.IsNull(ParcelsPane.ValidateMusic("")); Assert.IsNull(ParcelsPane.ValidateMusic("https://exemplo.com/a.mp3")); Assert.IsNotNull(ParcelsPane.ValidateMusic("ftp://exemplo.com/a.mp3")); Assert.IsNotNull(ParcelsPane.ValidateMusic("música sem endereço"));
        }

        [Test]
        public void TheMarriageStatesAreInPortuguese()
        {
            Assert.AreEqual("casados", MarriageWindow.StateWords("accepted")); Assert.AreEqual("pedido em aberto", MarriageWindow.StateWords("pending")); Assert.AreEqual("encerrado", MarriageWindow.StateWords("divorced")); Assert.AreEqual("desconhecido", MarriageWindow.StateWords("desconhecido"));
        }

        [Test]
        public void TheCellGridChoosesCellsAndHandsThemToTheApiAsPairs()
        {
            var grid = new CellGrid(); grid.Build(256, 4); Assert.AreEqual(64, grid.Size); Assert.AreEqual(-32, grid.Origin, "a ilha é centrada: de -32 a 31");
            grid.Toggle((3, 2)); grid.Toggle((-1, 2)); grid.Toggle((9, 9)); grid.Toggle((9, 9)); grid.Toggle((99, 99)); grid.Toggle((-33, 0)); grid.Toggle((31, 31));
            Assert.AreEqual(3, grid.Chosen); CollectionAssert.AreEqual(new[] { -1, 2 }, grid.Cells[0]); CollectionAssert.AreEqual(new[] { 3, 2 }, grid.Cells[1], "em ordem: linha e depois coluna"); CollectionAssert.AreEqual(new[] { 31, 31 }, grid.Cells[2]); grid.ClearChosen(); Assert.AreEqual(0, grid.Chosen);
            var big = new CellGrid(); big.Build(4096, 8); Assert.AreEqual(256, big.Size, "no máximo 256 x 256 células"); var none = new CellGrid(); none.Build(256, 0); Assert.AreEqual(0, none.Size);
            grid.SetOwnedRuns(new[] { new[] { 0, -2, 2 }, new[] { 1, 5, 5 } }); Assert.AreEqual(6, grid.Owned, "uma corrida é [linha, primeira, última] e inclui as duas pontas");
            using var document = System.Text.Json.JsonDocument.Parse("{\"runs\":[[0,0,1],[1,-2,2]]}"); var runs = CellGrid.RunsOf(document.RootElement); Assert.AreEqual(2, runs.Length); CollectionAssert.AreEqual(new[] { 1, -2, 2 }, runs[1]);
        }   }
}
