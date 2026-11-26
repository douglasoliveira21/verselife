using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LifeVerse.Shared.Terrain;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.World;
using LifeVerse.Unity.Audio;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The game as a person plays it: the login screen (with the list of public islands); signing in (or creating an account) against the API, the session renewed by itself before
    /// its 15 minutes end; the loading screen while the island opens; then the world (ground, sea, sky and weather, objects, people, vehicles), the camera and the avatar, with the HUD
    /// (bars, chat, map, notifications), the radial menu (Q), the performance overlay (F3) and the windows (social, wallet, inventory, search, profile, island, scripts, settings).
    /// When the connection drops or the session cannot be renewed it goes back to the login screen with a message. It starts by itself in a normal run; the self-checks and previews
    /// of the project (the "-lifeverse-..." arguments) and batch mode skip it. The API address is <c>LIFEVERSE_API_URL</c> or the production one.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        public const string DefaultApi = "https://api.nerdplus.com.br";
        public enum Phase { Login, Loading, Playing }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode || Environment.GetCommandLineArgs().Any(a => a.StartsWith("-lifeverse-") && a != "-lifeverse-app")) return;
            if (!Application.isPlaying) return;
            new GameObject("LifeVerse").AddComponent<GameApp>().Begin(Environment.GetEnvironmentVariable("LIFEVERSE_API_URL") is { Length: > 0 } url ? url : DefaultApi);
        }

        public UserSettings Settings { get; private set; }
        public UiRoot Ui { get; private set; }
        public LoginView Login { get; private set; }
        public LoadingView Loading { get; private set; }
        public Hud Hud { get; private set; }
        public NotificationCenter Notices { get; private set; }
        public Phase Current { get; private set; } = Phase.Login;
        public string ApiBase { get; private set; }
        public WorldClient Client { get; private set; }
        public LocalPlayer Player { get; private set; }
        public FollowCamera Follow { get; private set; }
        public ApiContext Context { get; private set; }
        public IslandEntryClient Api => _api;
        public string SettingsPath;
        public Guid Island => _island;
        private IslandEntryClient _api; private GameObject _world; private Guid _island; private Coroutine _entering;

        public void Begin(string apiBase, UserSettings settings = null)
        {
            ApiBase = apiBase; Settings = settings ?? UserSettings.Load(SettingsPath); GraphicsQuality.Apply(Settings); Store = new SettingsStore(Settings); Store.Changed += OnSettingChanged;
            Ui = UiRoot.Create(Settings); Notices = new NotificationCenter(Ui); Hud = new Hud(Ui, Notices);
            Login = new LoginView(Settings, "Versão " + Application.version + "  •  Unity"); Login.SignIn += OnSignIn; Login.Register += OnRegister; Ui.ScreenLayer.Add(Login);
            Loading = new LoadingView(); Loading.Back.clicked += BackToLogin;
            _api = new IslandEntryClient(apiBase); _api.SessionExpired += OnSessionExpired;
            Context = new ApiContext { Ui = Ui, Api = _api, Notices = Notices, CurrentIsland = () => _island, EnterIsland = id => Teleport(id), TravelTo = (id, x, z) => Teleport(id, x, z) };
            Hud.Map.RegisterCallback<WheelEvent>(e => { if (Store == null) return; Store.Set("world.minimap_zoom", Mathf.RoundToInt(Mathf.Clamp(Hud.Map.ZoomMetres * (e.delta.y > 0 ? 1.15f : 1f / 1.15f), 40f, 1000f) / 10f) * 10 + ""); SettingsEffects.Interface(Store, Hud, Ui); Settings.Save(SettingsPath); e.StopPropagation(); });
            RegisterWindows(); Hud.Chat.Send = text => Client != null && Client.SendChat(text);
            Hud.Chat.TypingChanged += typing => { if (Player != null) Player.Typing = typing; };
            Hud.Perf.Extra = () => (Store != null && Store.Bool("metrics.memory") ? "memória " + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576L) + " MB   " : "") + (Client == null ? "" : "pessoas " + (PeopleViews?.Count ?? 0) + "   objetos " + Client.Objects.Count + " (" + Client.Objects.CulledCount + " fora do alcance)   mensagens do mundo " + Client.Snapshots);
            if (SavedLogin.Load(SavedLoginPath) is { } saved) Login.FillSaved(saved.Login, saved.Password);
            ApplyAudio(); StartCoroutine(StartSounds()); StartCoroutine(LoadIslands()); StartCoroutine(WatchServers()); Login.MusicToggled += () => { Settings.LoginMusicMuted = !Settings.LoginMusicMuted; Login.SetMusic(Settings.LoginMusicMuted); Settings.Save(SettingsPath); };
            Sync = new AccountSync(Settings, (m, p, b) => _api.ApiAsync(m, p, b), () => _api.UserId, () => Settings.Save(SettingsPath)); UpdateInfo = new UpdateCheck(Application.version); Ui.Root.Then(_api.ApiAsync("GET", UpdateCheck.Route), r => { UpdateInfo.Apply(r.Status, r.Body); ShowUpdate(); });
        }

        // ---- the live channel of the platform: notices, the bell, teleport invites, the balance ----
        public LiveChannel Live { get; private set; }
        /// <summary>The address of the live channel when it is not the API's own (the tests use a server of their own).</summary>
        public string LiveAddress;
        public LiveEvents Events { get; private set; }
        public Button Bell { get; private set; }
        public int Unread { get; private set; }
        private IVisualElementScheduledItem _bellCount;

        /// <summary>Opens the live channel for the person who just signed in and routes its events.</summary>
        public void StartLive()
        {
            Live ??= gameObject.AddComponent<LiveChannel>();
            if (Events == null) { Events = new LiveEvents(Context, Settings) { Me = () => _api.UserId, MoveTo = MoveToSpot, BellChanged = CountBellSoon }; Live.Received += Events.Handle; Events.BalanceChanged += b => Hud.SetBalance(b); }
            LifeVerse.Unity.Avatar.AvatarWardrobe.Files = async (kind, id) => { var r = await _api.GetBytesAsync("/api/v1/avatars/" + kind + "/" + id + "/file"); return r.Status == 200 ? r.Bytes : null; };
            Ui.ScreenLayer.Then(_api.ApiAsync("GET", "/api/v1/economy/wallet"), r => { if (r.Status == 200) try { using var d = System.Text.Json.JsonDocument.Parse(r.Body); if (d.RootElement.TryGetProperty("availableCoins", out var c) && c.TryGetInt64(out var coins)) Hud.SetBalance(coins); } catch (System.Text.Json.JsonException) { } });
            if (string.IsNullOrEmpty(LiveAddress)) Live.Begin(ApiBase, () => _api.AccessToken); else Live.BeginAt(LiveAddress, () => _api.AccessToken); CountBell();
        }

        /// <summary>Goes to a spot: inside this island the island moves the avatar; on another island the game travels there.</summary>
        public void MoveToSpot(Guid island, float x, float z)
        {
            if (island == _island && Client != null && Current == Phase.Playing) Client.RequestTeleport("", x, z); else Teleport(island, x, z);
        }

        /// <summary>The number on the bell: the unread notifications as the server counts them.</summary>
        public void SetBell(int unread) { Unread = Math.Max(0, unread); if (Bell != null) Bell.text = Unread > 0 ? "Avisos (" + (Unread > 99 ? "99+" : Unread.ToString()) + ")" : "Avisos"; }

        public void CountBell() => Ui.Root.Then(_api.ApiAsync("GET", CenterPage.ListPath("todas", true, null, 0, 1)), r => { if (r.Status == 200 && CenterPage.Parse(r.Body) is { } page) SetBell(page.Unread); });

        /// <summary>Several events in a row count the bell once.</summary>
        private void CountBellSoon() { _bellCount?.Pause(); _bellCount = Ui.Root.schedule.Execute(CountBell); _bellCount.ExecuteLater(800); }

        /// <summary>The settings that follow the account.</summary>
        public AccountSync Sync { get; private set; }

        /// <summary>What the platform said about a newer client (unknown until it answers).</summary>
        public UpdateCheck UpdateInfo { get; private set; }

        private void ShowUpdate()
        {
            if (Login == null || UpdateInfo == null) return; Login.SetNews(UpdateInfo.Status == LifeVerse.Shared.Updates.UpdateStatus.UpToDate || UpdateInfo.Status == LifeVerse.Shared.Updates.UpdateStatus.Unknown ? new string[0] : new[] { UpdateInfo.Describe() }); Login.ShowUpdate(UpdateInfo.Describe(), UpdateInfo.Required, UpdateInfo.Release != null && UpdateInfo.Release.HasSafeDownloadLink, () => UpdateInfo.OpenDownload());
            if (UpdateInfo.CanDownload && UpdateInfo.Status != LifeVerse.Shared.Updates.UpdateStatus.UpToDate) { Login.InstallRequested = () => StartCoroutine(DownloadUpdate()); Login.ShowInstall(null, true); }
        }

        /// <summary>Downloads the installer, checks it and, when the person confirms by pressing the button again, opens it.</summary>
        public System.Collections.IEnumerator DownloadUpdate(string folder = null)
        {
            Login.ShowInstall("Baixando a atualização…", false); var last = 0f; var task = UpdateDownloader.DownloadAsync(UpdateInfo.Release.DownloadUrl, UpdateInfo.Sha256, folder ?? UpdateDownloader.DefaultFolder, p => last = p);
            while (!task.IsCompleted) { Login.ShowInstall("Baixando a atualização… " + Mathf.RoundToInt(last * 100f) + "%", false); yield return new WaitForSeconds(.3f); }
            var result = task.Result; UpdateDownloaded = result;
            if (!result.Ok) { Login.ShowInstall(result.Message, true); yield break; }
            Login.InstallRequested = () => { if (!UpdateDownloader.Open(result.Path)) Login.ShowInstall("Não foi possível abrir o arquivo: " + result.Path, true); }; Login.ShowInstall(result.Message + " Pressione de novo para abrir o instalador.", true);
        }

        /// <summary>What the last download of an update gave (for the tests).</summary>
        public DownloadResult UpdateDownloaded { get; private set; }

        private void RegisterWindows()
        {
            Hud.Register("Chat", "chat", () => { Hud.Chat.Focus(); return null; });
            Hud.Register("Pessoas", "people", () => new PeopleHubWindow(Context, NearbyPeople, RecentPeople, OpenPersonActions, name => GoToPerson(name)));
            Hud.Register("Social", "social", () => new SocialWindow(Context));
            Hud.Register("Inventário", "inventory", () => new InventoryManagerWindow(Context, Store));
            Hud.Register("Carteira", "wallet", () => new WalletHubWindow(Context));
            Hud.Register("Buscar", "search", () => new GlobalSearchWindow(Context, Settings, () => Settings.Save(SettingsPath), (island, x, z) => { if (x is { } px && z is { } pz) MoveToSpot(island, px, pz); else Context.EnterIsland(island); }));
            Hud.Register("Construir", "build", () => { ToggleBuild(); return null; });
            Hud.Register("Voz", "voice", () => { if (Voice == null) return null; Voice.Locked = !Voice.Locked; Notices.Show(Voice.Locked ? "Microfone aberto: quem está perto ouve você." : "Microfone fechado. Segure " + KeyBindings.KeyName(KeyBindings.Of(GameAction.Talk)) + " para falar.", NoticeKind.Info); return null; });
            Bell = Hud.Register("Avisos", "notifications", () => new NotificationCenterWindow(Context, SetBell));
            Hud.Register("Mapa", "map", () => new WorldMapWindow(Context, () => Player != null ? Player.GamePosition : (Vector3?)null));
            Hud.Register("Mais", "more", MoreWindow);
            Hud.Register("Configurações", "settings", () => new SettingsCatalogWindow(Context, Store, SettingsApplied, id => SettingsEffects.Info(id, this), SettingActions(), () => OpenClassicSettings().Open()));
            Hud.Register("Ajuda", "help", () => Hud.HelpWindow(Ui)); Hud.Travel = TravelIslands;
            Hud.Register("Gestos", "gestures", () => { OpenRadial(new Vector2(Screen.width / 2f, Screen.height / 2f) / Mathf.Max(.01f, Ui.Panel.scale)); return null; }); Hud.Register("Foto", "photo", () => { TakePhoto(); return null; });
            Hud.Register("Mini-mapa", "minimap", () => { MinimapShown = !MinimapShown; Hud.Map.style.visibility = MinimapShown ? Visibility.Visible : Visibility.Hidden; return null; });
            Hud.SetDefaultOrder("voice", "gestures", "photo", "build", "people", "map", "inventory", "minimap", "search", "wallet", "notifications");
            Hud.BuyCoinsPressed = () => Hud.Toggle("wallet", () => new WalletHubWindow(Context));
            // the menus of the top bar, as in the Godot client
            FloatingWindow Open(FloatingWindow w) { w?.Open(); return w; }
            Hud.AddMenu("Eu", new (string, string, Action)[]
            {
                ("Meu perfil", "user", () => Open(new ProfileHubWindow(Context, _api.UserId))), ("Avatar: visuais, corpo, roupas e animações", "avatar", () => Open(OpenAvatarStudio())), ("Meu personagem", "user", () => OpenCharacter()?.Open()),
                ("Anexos do corpo", "pin", () => Open(new AttachmentsWindow(Context, RefreshOwnAttachments, EditAttachment))), ("Inventário", "bag", () => Hud.Toggle("inventory", () => new InventoryManagerWindow(Context, Store))),
                ("Carteira", "coin", () => Hud.Toggle("wallet", () => new WalletHubWindow(Context))), ("Minhas propriedades", "home", () => Open(new MyPropertiesWindow(Context))),
                ("Configurações", "settings", () => Hud.Toggle("settings", () => new SettingsCatalogWindow(Context, Store, SettingsApplied, id => SettingsEffects.Info(id, this), SettingActions(), () => OpenClassicSettings().Open()))),
                ("Barra de ferramentas", "layers", () => Open(ToolbarWindow())), ("Sair da ilha", "power", BackToLogin),
            });
            Hud.AddMenu("Comunicar", new (string, string, Action)[]
            {
                ("Conversa local", "chat", () => Hud.Chat.Focus()), ("Pessoas", "people", () => Hud.Toggle("people", () => new PeopleHubWindow(Context, NearbyPeople, RecentPeople, OpenPersonActions, name => GoToPerson(name)))),
                ("Mensagens e amigos", "chat-dots", () => Hud.Toggle("social", () => new SocialWindow(Context))), ("Grupos", "people", () => Open(new GroupsHubWindow(Context))),
                ("Avisos", "bell", () => Hud.Toggle("notifications", () => new NotificationCenterWindow(Context, SetBell))), ("Gestos", "hand", () => OpenRadial(new Vector2(Screen.width / 2f, Screen.height / 2f) / Mathf.Max(.01f, Ui.Panel.scale))),
            });
            Hud.AddMenu("Mundo", new (string, string, Action)[]
            {
                ("Mapa do mundo", "map", () => Hud.Toggle("map", () => new WorldMapWindow(Context, () => Player != null ? Player.GamePosition : (Vector3?)null))), ("Buscar no LifeVerse", "search", () => Hud.Toggle("search", () => new GlobalSearchWindow(Context, Settings, () => Settings.Save(SettingsPath), (island, x, z) => { if (x is { } px && z is { } pz) MoveToSpot(island, px, pz); else Context.EnterIsland(island); }))),
                ("Eventos, marcos e fotos", "calendar", () => Open(new DiscoveryWindow(Context, this, () => Player != null ? Player.GamePosition : Vector3.zero, () => Player != null ? Player.HeadingDegrees * Mathf.Deg2Rad : 0f))),
                ("Sobre a ilha", "info", () => Open(new IslandInfoWindow(Context, _island))), ("Gerenciar esta ilha", "settings", () => Open(new IslandManagementWindow(Context, _island))), ("Gerenciar terrenos", "grid", () => Open(new LandManagementWindow(Context, _island, Here))),
                ("Sobre este terreno", "pin", () => OpenParcelHere()), ("Mercado imobiliário", "home", () => Open(new RealEstateMarketWindow(Context))), ("Editar terreno   T", "mountain", ToggleTerrain), ("Tirar foto   F12", "photo", TakePhoto),
                ("Ilha anterior", "chevron-left", () => TravelIslands(-1)), ("Próxima ilha", "chevron-right", () => TravelIslands(1)),
            });
            Hud.AddMenu("Conteúdo", new (string, string, Action)[]
            {
                ("Construir   B", "hammer", ToggleBuild), ("Mercado", "bag", () => Open(new MarketWindow(Context))), ("Ofertas de itens", "download", () => Open(new OffersWindow(Context))), ("Script de um objeto", "code", () => ScriptOpener()?.Open()),
            });
            Hud.AddMenu("Ajuda", new (string, string, Action)[] { ("Atalhos e controles", "help", () => Hud.Toggle("help", () => Hud.HelpWindow(Ui))), ("Manual dos scripts", "doc", () => Open(new ScriptDocsWindow(Context, _ => { }))) });
            Hud.AddMenu("Desenvolver", new (string, string, Action)[]
            {
                ("Desempenho   F3", "chart", () => Hud.TogglePerformance()), ("Exportar diagnóstico", "download", () => { var path = SettingsEffects.WriteDiagnostics(Store, this); Notices.Show("Diagnóstico salvo em " + path, NoticeKind.Good); }),
            });
            Hud.Presence.RegisterValueChangedCallback(e => { if (Current == Phase.Playing) Api.ApiAsync("PUT", "/api/v1/social/presence", System.Text.Json.JsonSerializer.Serialize(new { status = e.newValue, privacyJson = (string)null })); });
        }

        /// <summary>The windows that do not fit in the bar: profile, groups, market, events and photos, the island and its management, item offers, scripts and the character.</summary>
        private FloatingWindow MoreWindow()
        {
            var window = new FloatingWindow(Ui, "more", "Mais", 300f, -1f, new Vector2(520, 380));
            void Add(string label, Func<FloatingWindow> create) { var button = UiKit.Button(label, () => { window.Close(); create()?.Open(); }); button.style.marginBottom = 3; window.Body.Add(button); }
            Add("Perfil", () => new ProfileHubWindow(Context, _api.UserId)); Add("Grupos", () => new GroupsHubWindow(Context)); Add("Mercado", () => new MarketWindow(Context)); Add("Mercado imobiliário", () => new RealEstateMarketWindow(Context)); Add("Minhas propriedades", () => new MyPropertiesWindow(Context));
            Add("Eventos, marcos e fotos", () => new DiscoveryWindow(Context, this, () => Player != null ? Player.GamePosition : Vector3.zero, () => Player != null ? Player.HeadingDegrees * Mathf.Deg2Rad : 0f));
            Add("Ofertas de itens", () => new OffersWindow(Context)); Add("Sobre a ilha", () => new IslandInfoWindow(Context, _island)); Add("Gestão da ilha", () => new IslandManagementWindow(Context, _island)); Add("Gestão de terrenos", () => new LandManagementWindow(Context, _island, Here)); Add("Sobre este terreno", OpenParcelHere);
            Add("Script de um objeto", ScriptOpener); Add("Meu personagem", OpenCharacter); Add("Avatar: visuais, corpo, roupas e animações", OpenAvatarStudio); Add("Barra de ferramentas", ToolbarWindow); Add("Anexos do corpo", () => new AttachmentsWindow(Context, RefreshOwnAttachments, EditAttachment));
            return window;
        }

        /// <summary>After a change of what is worn: asks the platform for the avatar again and shows it on the own body.</summary>
        /// <summary>Reads the person's avatar again and redraws them (after a look, a body, a garment or an animation changed in the Avatar window).</summary>
        public void RefreshOwnAvatar() => Ui.Root.Then(Api.ApiAsync("GET", "/api/v1/users/me/avatar"), r => { if (r.Status == 200 && Client != null) PeopleViews?.ApplyAppearance(Client.SelfPeerId, r.Body); });

        public void RefreshOwnAttachments() => Ui.Root.Then(Api.ApiAsync("GET", AttachmentsWindow.Route), r =>
        {
            if (r.Status != 200) return; var list = AttachmentData.ParseList(r.Body, out _); var worn = PeopleViews?.ViewOf(Client != null ? Client.SelfPeerId : 0)?.GetComponent<AvatarAttachments>(); if (worn != null) worn.Apply(list); HudWorn?.Apply(list);
        });

        /// <summary>The editor of the character: the saved one is read from the API (the starter one when there is none) and a saved change shows on the avatar at once.</summary>
        private FloatingWindow OpenCharacter()
        {
            var holder = new FloatingWindow(Ui, "character-loading", "Meu personagem", 300f, -1f, new Vector2(400, 200)); holder.Body.Add(UiKit.Text("Carregando o personagem…", "lv-muted"));
            holder.Then(Api.ApiAsync("GET", "/api/v1/users/me/avatar"), r =>
            {
                holder.Close(); var draft = r.Status == 200 ? CharacterDraft.FromResponse(r.Body) : CharacterDraft.Starter(Login.User.value.Trim());
                var window = new CharacterWindow(Context, draft); window.Saved += () => { var view = PeopleViews?.ViewOf(Client.SelfPeerId); view?.ApplyAppearance(AvatarAppearance.Parse(draft.ToAppearanceJson())); }; window.Open();
            });
            return holder;
        }

        /// <summary>Finds the person with a name that is nearby (the world only knows names) among the platform's people and opens what can be done to them.</summary>
        private void OpenPersonActions(string displayName)
        {
            Ui.ScreenLayer.Then(Api.ApiAsync("GET", "/api/v1/social/people?q=" + Uri.EscapeDataString(displayName)), r =>
            {
                var match = r.Status == 200 ? Json.Rows(r.Body).FirstOrDefault(p => string.Equals(p.First("displayName", "username"), displayName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Text("username"), displayName, StringComparison.OrdinalIgnoreCase)) : default;
                if (r.Status != 200 || match.Id() == Guid.Empty) { Notices.Show("Não achei " + displayName + " na plataforma.", NoticeKind.Warn); return; }
                new PersonActionsWindow(Context, match.Id(), displayName, id => new ProfileHubWindow(Context, id).Open(), (id, name) => { var social = new SocialWindow(Context); social.Open(); social.Talk(id, name); }).Open();
            });
        }

        /// <summary>The script editor needs to know which object: until the building tools pick one with the mouse, the person types its identifier.</summary>
        private Vector3? Here() => Player != null ? Player.GamePosition : (Vector3?)null;

        /// <summary>Opens "Sobre este Terreno" for the parcel the person stands on (found in the island's land list).</summary>
        public FloatingWindow OpenParcelHere()
        {
            var at = Here(); if (at == null || _island == Guid.Empty) { Notices.Show("Entre numa ilha primeiro.", NoticeKind.Warn); return null; }
            Ui.ScreenLayer.Then(Api.ApiAsync("GET", "/api/v1/worlds/islands/" + _island + "/land"), r =>
            {
                var parcel = r.Status == 200 ? ParcelInfoWindow.ParcelAt(r.Body, at.Value.x, at.Value.z) : Guid.Empty;
                if (parcel == Guid.Empty) { Notices.Show(r.Status == 200 ? "Você não está em nenhum terreno agora." : UiTasks.Explain(r.Status, r.Body), NoticeKind.Info); return; }
                LastParcelInfo = new ParcelInfoWindow(Context, _island, parcel, Here); LastParcelInfo.Open();
            });
            return null;
        }

        public ParcelInfoWindow LastParcelInfo { get; private set; }

        private FloatingWindow ScriptOpener()
        {
            var window = new FloatingWindow(Ui, "script-open", "Abrir script de um objeto", 380f, -1f, new Vector2(300, 140)); var id = UiKit.Field("Identificador do objeto", false, 40); var note = UiKit.Text("", "lv-small", "lv-bad"); window.Body.Add(id); window.Body.Add(note);
            window.Body.Add(UiKit.Button("Abrir o editor", () => { if (!Guid.TryParse(id.value.Trim(), out var obj)) { note.text = "Digite o identificador do objeto."; return; } window.Close(); new LuaEditorWindow(Context, _island, obj).Open(); }, "lv-btn-primary")); return window;
        }

        /// <summary>The objects the person wears, on their own body in the world.</summary>
        public AvatarAttachments OwnAttachments => PeopleViews?.ViewOf(Client != null ? Client.SelfPeerId : 0)?.GetComponent<AvatarAttachments>();
        public AttachmentEditor AttachmentEditing { get; private set; }

        /// <summary>The editor on the body for a worn object (needs it to have appeared on the body).</summary>
        public FloatingWindow EditAttachment(AttachmentData a)
        {
            if (OwnAttachments?.HolderOf(a.Id) == null) { Notices.Show("Este objeto ainda não apareceu no corpo.", NoticeKind.Info); return null; }
            AttachmentEditing?.Close(); AttachmentEditing = new AttachmentEditor(Context, a, () => OwnAttachments, () => Follow != null ? Follow.Camera : null, () => Follow, RefreshOwnAttachments); return AttachmentEditing;
        }

        public PlayerViews PeopleViews => _world != null ? _world.GetComponentInChildren<PlayerViews>() : null;

        // ---- the list of islands, before signing in ----

        private IEnumerator LoadIslands()
        {
            var listing = _api.GetIslandsAsync(); while (!listing.IsCompleted) yield return null;
            var islands = new List<(Guid, string, string)>();
            if (listing.Result != null) foreach (var row in Json.Rows(listing.Result)) if (row.Id() != Guid.Empty) islands.Add((row.Id(), row.Text("name", "Ilha"), row.Text("category")));
            Login.SetIslands(islands, Settings.LastIsland);
        }

        // ---- signing in ----

        private void OnSignIn(string user, string password, Guid island) { if (UpdateBlocks()) return; StartCoroutine(SignInFlow(user, password, null, island)); }
        private void OnRegister(string user, string email, string password, Guid island) { if (UpdateBlocks()) return; StartCoroutine(SignInFlow(user, password, email, island)); }

        /// <summary>A version below the minimum of the platform cannot sign in: the person is told to update.</summary>
        private bool UpdateBlocks() { if (UpdateInfo == null || !UpdateInfo.Required) return false; Login.Fail(UpdateInfo.Describe()); return true; }

        private IEnumerator SignInFlow(string user, string password, string email, Guid island)
        {
            _island = island;
            var attempt = email == null ? _api.LoginAsync(user, password) : _api.RegisterAsync(user, email, password);
            while (!attempt.IsCompleted) yield return null;
            var result = attempt.Result;
            if (!result.Ok) { Login.Fail(string.IsNullOrEmpty(result.Message) ? "Não foi possível entrar. Tente de novo." : result.Message); yield break; }
            var leftToServer = island == Guid.Empty;
            if (island == Guid.Empty)
            {
                // the server chooses: the island where the person stopped (if it still exists and they may enter) or the welcome island
                var start = _api.ApiAsync("GET", "/api/v1/worlds/me/start"); while (!start.IsCompleted) yield return null;
                island = start.Result.Status == 200 ? Json.One(start.Result.Body).Id("islandId") : Guid.Empty; StartReason = start.Result.Status == 200 ? Json.One(start.Result.Body).Text("reason") : "";
                if (island == Guid.Empty) { Login.Fail(start.Result.Status == 200 ? "O servidor não indicou uma ilha. Escolha uma na lista." : UiTasks.Explain(start.Result.Status, start.Result.Body)); yield break; }
                _island = island;
            }
            if (Login.RememberPassword.value && SavedLogin.Save(user, password, SavedLoginPath)) SavedPassword = true; else { SavedLogin.Clear(SavedLoginPath); SavedPassword = false; }
            Settings.LastLogin = Login.UserToRemember; Settings.LastIsland = leftToServer ? "" : island.ToString(); Settings.Save(SettingsPath); Login.Succeed(); StartLive();   // "onde parei" stays the choice next time
            yield return EnterWorld();
        }

        /// <summary>Why the server chose the island when the person left it to the server ("last" or "welcome"), or "".</summary>
        public string StartReason { get; private set; } = "";
        /// <summary>Whether the mini-map shows (the "Mini-mapa" button hides and shows it).</summary>
        public bool MinimapShown { get; private set; } = true;
        /// <summary>Where "Lembrar senha" keeps the sealed login (the checks point it elsewhere).</summary>
        public string SavedLoginPath { get; set; } = SavedLogin.DefaultPath;
        public bool SavedPassword { get; private set; }

        /// <summary>The health of the API on the login screen, every 20 seconds while it is open.</summary>
        private IEnumerator WatchServers()
        {
            while (this != null)
            {
                if (Current == Phase.Login && Login != null) { var check = _api.GetBytesAsync("/health"); while (!check.IsCompleted) yield return null; Login.SetServers(check.IsFaulted ? 0 : check.Result.Status); }
                var until = Time.realtimeSinceStartup + 20f; while (Time.realtimeSinceStartup < until) { if (Login != null && Current == Phase.Login && Time.realtimeSinceStartup - _newsClock > 6f) { _newsClock = Time.realtimeSinceStartup; Login.NextNews(); } yield return null; }
            }
        }
        private float _newsClock;

        /// <summary>The theme of the login screen: it fades in while the screen is open (unless muted) and out when the person goes into the world.</summary>
        public AudioSource LoginMusic { get; private set; }
        private void TickLoginMusic()
        {
            if (LoginMusic == null) { var clip = Resources.Load<AudioClip>("LifeVerse/Login/theme"); if (clip == null) return; LoginMusic = gameObject.AddComponent<AudioSource>(); LoginMusic.clip = clip; LoginMusic.loop = true; LoginMusic.playOnAwake = false; LoginMusic.volume = 0f; LoginMusic.spatialBlend = 0f; }
            var wanted = Current == Phase.Login && Settings != null && !Settings.LoginMusicMuted ? .35f * Mathf.Clamp01(Settings.MasterVolume) * Mathf.Clamp01(Settings.MusicVolume) : 0f;
            LoginMusic.volume = Mathf.MoveTowards(LoginMusic.volume, wanted, Time.unscaledDeltaTime * (wanted > LoginMusic.volume ? .15f : .4f));
            if (wanted > 0f && !LoginMusic.isPlaying) LoginMusic.Play(); else if (wanted <= 0f && LoginMusic.volume <= .001f && LoginMusic.isPlaying) LoginMusic.Stop();
        }

        private void OnSessionExpired() { if (Current == Phase.Login) return; BackToLogin(); Login.Fail("Sua sessão acabou. Entre de novo."); }

        // ---- opening the island ----

        /// <summary>Where the person will arrive (a trip to a spot), so the files near it load first; null: where the island spawns people.</summary>
        public Vector2? Arrival { get; private set; }

        private IEnumerator EnterWorld(EntryResult ticket = null, Vector2? arrival = null)
        {
            Show(Phase.Loading); Loading.SetStatus("requesting_ticket");
            BuildWorld();
            var join = Client.gameObject.AddComponent<WorldJoin>(); join.Client = Client;
            Arrival = arrival; var running = join.Join(_api, _island, true, ticket, arrival);
            while (running.MoveNext()) { Loading.SetStatus(join.Status, join.Failure, join.Detail); yield return running.Current; }
            Loading.SetStatus(join.Status, join.Failure);
            if (join.Status != "in_island")
            {
                var needsAvatar = join.FailureStatus == 409; TearDownWorld();
                if (needsAvatar) { yield return CreateCharacter(); }
                yield break;
            }
            yield return new WaitUntil(() => Client.SelfPeerId >= 0 && PeopleViews != null && PeopleViews.ViewOf(Client.SelfPeerId) != null);
            Player.Attach(Client, Client.Terrain, PeopleViews, Follow); Player.Vehicles = _world.GetComponentInChildren<VehicleViews>(); ApplySettingsToWorld();
            Client.DisconnectedBy += OnDisconnected; Client.ChatReceived += OnChat; Client.PlayerSpawned += OnSpawned; Client.PlayerDespawned += OnDespawned;
            Hud.SetIsland("Ilha " + _island.ToString().Substring(0, 8)); Show(Phase.Playing); Notices.Show("Você entrou na ilha.", NoticeKind.Good);
            Reconnects = 0; if (Settings.IslandYaw.TryGetValue(_island.ToString("N"), out var yaw) && Follow != null) Follow.Yaw = yaw; Islands.Visit(_island); Hud.SetTravel(Islands.CanBack, Islands.CanForward); ApplyInterface();
            if (Sync != null && Sync.Enabled) { var pull = Sync.PullIfEnabledAsync(); while (!pull.IsCompleted) yield return null; if (pull.Result.Result == SyncResult.Ok && pull.Result.Values.Count > 0) { ApplySettingsToWorld(); Settings.Save(SettingsPath); } }
        }

        /// <summary>The API asked for an avatar before entering (409): the character creator opens over the loading screen, and once saved the island is entered again.</summary>
        private IEnumerator CreateCharacter()
        {
            Loading.SetStatus("failed", "Crie o seu personagem para entrar na ilha.");
            var done = false; var window = new CharacterWindow(Context, CharacterDraft.Starter(Login.User.value.Trim()), true); window.Saved += () => done = true; window.Open();
            while (!done && Current == Phase.Loading) yield return null;
            window.Close(); if (!done) yield break;
            _entering = StartCoroutine(EnterWorld());
        }

        /// <summary>The trip in progress (null when there is none).</summary>
        public TeleportTrip Trip { get; private set; }
        public bool Travelling => _trip != null;
        private Coroutine _trip;

        /// <summary>Travels to another island as a controlled transfer (see <see cref="TeleportTrip"/>): the person stays where they are until the ticket is in hand, and if the destination cannot be reached the island they left is entered again.</summary>
        public bool Teleport(Guid island, float? x = null, float? z = null)
        {
            if (island == Guid.Empty || Current != Phase.Playing || _trip != null) return false;
            if (island == _island && x == null) { Notices.Show("Você já está nessa ilha.", NoticeKind.Info); return false; }
            Trip = new TeleportTrip { Destination = island, Source = _island, X = x, Z = z }; _trip = StartCoroutine(TeleportFlow(Trip)); return true;
        }

        public void CancelTrip() { if (Trip != null && Trip.Step is "preparing" or "connecting") { var trip = Trip; StopCoroutine(_trip); _trip = null; StartCoroutine(trip.Cancel(_api)); Notices.Show("Viagem cancelada.", NoticeKind.Info); } }

        private IEnumerator TeleportFlow(TeleportTrip trip)
        {
            Notices.Show("Preparando a viagem…", NoticeKind.Info);
            yield return trip.Prepare(_api);
            if (trip.Failed) { Notices.Show(trip.Message, NoticeKind.Bad); _trip = null; yield break; }
            var source = _island;
            if (Client != null) { Client.DisconnectedBy -= OnDisconnected; Client.ChatReceived -= OnChat; Client.PlayerSpawned -= OnSpawned; Client.PlayerDespawned -= OnDespawned; }
            TearDownWorld(); _island = trip.Destination; Settings.LastIsland = _island.ToString(); Settings.Save(SettingsPath);
            yield return EnterWorld(trip.Ticket, trip.X is { } tx && trip.Z is { } tz ? new Vector2(tx, tz) : (Vector2?)null);
            if (Current != Phase.Playing)
            {
                // the destination did not take the person: the reservation is released and the island they left is entered again
                yield return trip.Cancel(_api); Notices.Show("Não foi possível chegar à ilha de destino. Voltando para a ilha anterior.", NoticeKind.Bad); _trip = null; Teleport_Return(source); yield break;
            }
            yield return trip.ConfirmArrival(_api);
            if (trip.Failed) Notices.Show(trip.Message, NoticeKind.Bad); else Notices.Show("Você chegou.", NoticeKind.Good);
            _trip = null;
        }

        private void Teleport_Return(Guid source) { if (source == Guid.Empty) return; _island = source; Settings.LastIsland = source.ToString(); Settings.Save(SettingsPath); _entering = StartCoroutine(EnterWorld()); }

        /// <summary>Goes from the island the person is in to another one by a plain entry (no reservation): used to return after a trip failed.</summary>
        public void SwitchIsland(Guid island)
        {
            if (island == Guid.Empty || Current == Phase.Loading) return;
            if (Client != null) { Client.DisconnectedBy -= OnDisconnected; Client.ChatReceived -= OnChat; Client.PlayerSpawned -= OnSpawned; Client.PlayerDespawned -= OnDespawned; }
            TearDownWorld(); _island = island; Settings.LastIsland = island.ToString(); Settings.Save(SettingsPath); if (_entering != null) StopCoroutine(_entering); _entering = StartCoroutine(EnterWorld());
        }

        /// <summary>The whole world as one person sees it, in the order the pieces need each other.</summary>
        private void BuildWorld()
        {
            _world = new GameObject("World");
            var camera = new GameObject("Camera") { tag = "MainCamera" }.GetComponent<Transform>(); camera.SetParent(_world.transform);
            var cam = camera.gameObject.AddComponent<Camera>(); cam.nearClipPlane = .2f; cam.farClipPlane = 6000f; cam.fieldOfView = 62f; camera.gameObject.AddComponent<HDAdditionalCameraData>(); camera.gameObject.AddComponent<AudioListener>();
            var terrain = new GameObject("Terrain").AddComponent<TerrainView>(); terrain.transform.SetParent(_world.transform); terrain.Apply(new TerrainMap());
            var ocean = new GameObject("Ocean").AddComponent<OceanView>(); ocean.transform.SetParent(_world.transform); ocean.Camera = cam;
            var objects = new GameObject("Objects").AddComponent<WorldObjects>(); objects.transform.SetParent(_world.transform); objects.Camera = cam;
            var environment = new GameObject("Environment").AddComponent<IslandEnvironment>(); environment.transform.SetParent(_world.transform); environment.Initialize(cam, terrain.Map.SeaLevel);
            Client = _world.AddComponent<WorldClient>(); Client.Terrain = terrain; Client.Ocean = ocean; Client.Objects = objects; Client.Environment = environment; Client.Token = Login.User.value.Trim();
            var views = new GameObject("People").AddComponent<PlayerViews>(); views.transform.SetParent(_world.transform); views.Camera = cam; views.Attach(Client);
            var cars = new GameObject("Vehicles").AddComponent<VehicleViews>(); cars.transform.SetParent(_world.transform); cars.Attach(Client);
            Follow = _world.AddComponent<FollowCamera>(); Follow.Camera = cam; Follow.Terrain = terrain; Follow.Distance = Store.Float("camera.default_distance"); SettingsEffects.Camera(Store, Follow, Settings.ReduceMotion); Player = _world.AddComponent<LocalPlayer>();
            Plates = new NamePlates(Ui); Follow.PickPoint = PickPoint; Follow.OverInterface = point => UiKit.IsOver(Ui.Root, point);
            Hud.Chat.Send = text => Client != null && Client.SendChat(text);
            // what scripts of objects ask of the person: dialogs, permission requests and panels (the island checks every answer)
            ScriptDialogs = new ScriptDialogs(Ui) { Send = json => { if (Client != null) Client.SendScriptResponse(json); } }; ScriptPanels = new ScriptHud(Ui) { Send = json => { if (Client != null) Client.SendScriptResponse(json); } };
            Client.ScriptOperation += json => { ScriptDialogs.OnOperation(json); ScriptPanels.OnOperation(json); Context.RaiseIslandScript(json); };
            Context.SendToIsland = json => Client != null && Client.Connected && Client.SendScriptResponse(json);
            Builder = new BuildEditor(Context, () => Client, () => Follow != null ? Follow.Camera : null, () => Player != null ? Player.GamePosition : Vector3.zero, () => Client != null ? Client.Terrain : null);
            Builder.Attach();
            // sound: the soundscape, what scripts play, the music of the parcel, and proximity voice
            Ambient = new GameObject("Ambient").AddComponent<AmbientSound>(); Ambient.transform.SetParent(_world.transform);
            Texts = new GameObject("ObjectTexts").AddComponent<ObjectTexts>(); Texts.transform.SetParent(_world.transform); Texts.Attach(Client);
            Sounds = new GameObject("WorldSounds").AddComponent<WorldSounds>(); Sounds.transform.SetParent(_world.transform); Sounds.Attach(Client); Sounds.AssetLoader = asset => Client != null && Client.AssetLoader != null ? Client.AssetLoader(asset) : System.Threading.Tasks.Task.FromResult<byte[]>(null);
            AvatarAttachments.Constructions = (package, parent) => Client != null && Client.Objects != null ? Client.Objects.BuildPackage(package, parent) : 0;
            AvatarAttachments.Loader = asset => Client != null && Client.AssetLoader != null ? Client.AssetLoader(asset) : System.Threading.Tasks.Task.FromResult<byte[]>(null);
            objects.MediaAudio = host => BusSource.Make(host, Bus.Media, .8f, true, 25f, true).Source;
            ParcelMusic = new GameObject("ParcelMusic").AddComponent<LandMusic>(); ParcelMusic.transform.SetParent(_world.transform); ParcelMusic.Setup(_api, () => _island, () => Player != null ? Player.GamePosition : (Vector3?)null); ParcelMusic.IslandFinder = () => Client != null && Client.Environment != null ? Client.Environment.GetComponent<IslandLookService>() : null; ParcelMusic.Load();
            Voice = new GameObject("Voice").AddComponent<VoiceChat>(); Voice.transform.SetParent(_world.transform); Voice.Attach(Client); Hearing = new GameObject("Hearing").AddComponent<VoiceListener>(); Hearing.transform.SetParent(_world.transform); Hearing.Attach(Client);
            Hearing.PositionOf = peer => PeopleViews != null && PeopleViews.ViewOf(peer) != null ? PeopleViews.ViewOf(peer).GamePosition : (Vector3?)null; Voice.HearingOthers = () => Hearing.AnySpeaking(); ApplyAudio();
            TerrainTools = new TerrainEditor(Context, () => Client, () => Follow != null ? Follow.Camera : null, () => Client != null ? Client.Terrain : null);
            Effects = new GameObject("Effects").AddComponent<ObjectEffects>(); Effects.transform.SetParent(_world.transform); Effects.Attach(Client);
            HudWorn = new HudAttachments(Ui, () => Follow != null ? Follow.Camera : null); RefreshOwnAttachments();
        }

        /// <summary>The particles and lights that scripts put on objects.</summary>
        public ObjectEffects Effects { get; private set; }

        /// <summary>What is worn at points of the screen.</summary>
        public HudAttachments HudWorn { get; private set; }

        public ScriptDialogs ScriptDialogs { get; private set; }
        public ScriptHud ScriptPanels { get; private set; }
        public AmbientSound Ambient { get; private set; }
        public WorldSounds Sounds { get; private set; }
        /// <summary>The floating texts that scripts put over objects.</summary>
        public ObjectTexts Texts { get; private set; }
        public LandMusic ParcelMusic { get; private set; }
        public VoiceChat Voice { get; private set; }
        public VoiceListener Hearing { get; private set; }
        public BusSource LoginTheme { get; private set; }
        private MicTest _micTest;
        private float _nextSplash;

        /// <summary>Puts the volumes and the voice settings on the sound system (called at the start and whenever the settings change).</summary>
        public void ApplyAudio()
        {
            KeyBindings.Load(Settings.Keys); KeyBindings.Blocked = () => Hud != null && Hud.Chat != null && Hud.Chat.Typing || Ui != null && Ui.Root != null && Ui.Root.panel != null && Ui.Root.panel.focusController != null && Ui.Root.panel.focusController.focusedElement is TextField;
            MicDevices.Preferred = Settings.InputDevice; UiSounds.Enabled = Settings.UiSounds; Buses.SetAll(Settings.MasterVolume, Settings.AmbientVolume, Settings.EffectsVolume, Settings.MusicVolume, Settings.MediaVolume, Settings.VoiceVolume);
            if (Voice != null) { Voice.Gate = Settings.VoiceSensitivity; Voice.EchoGuard = Settings.EchoGuard; }
            if (ParcelMusic != null) ParcelMusic.Enabled = Settings.ParcelMusic;
        }

        /// <summary>Reads the recordings that come with the game and starts the login theme if the person is still at the login screen.</summary>
        private IEnumerator StartSounds()
        {
            yield return SoundLibrary.LoadAll(); EnsureLoginTheme();
        }

        private void EnsureLoginTheme()
        {
            if (LoginTheme != null || !SoundLibrary.Has(SoundLibrary.Login)) return;
            var host = new GameObject("LoginMusic"); host.transform.SetParent(transform, false); LoginTheme = BusSource.Make(host, Bus.Music, .6f, false, 30f, true); LoginTheme.Source.clip = SoundLibrary.Get(SoundLibrary.Login); LoginTheme.FadeSeconds = 1.5f; LoginTheme.Fade = 0f;
            LoginTheme.FadeTarget = Current == Phase.Login ? 1f : 0f; LoginTheme.Source.Play();
        }
        public BuildEditor Builder { get; private set; }
        public TerrainEditor TerrainTools { get; private set; }
        public BuildWindow BuildPanel { get; private set; }
        public TerrainWindow TerrainPanel { get; private set; }

        /// <summary>B: starts or ends the build mode (the panel, the selection, the gizmo).</summary>
        public void ToggleBuild()
        {
            if (Current != Phase.Playing || Builder == null) return;
            if (Builder.Active) { BuildPanel?.Close(); BuildPanel = null; Builder.SetActive(false); return; }
            TerrainTools.SetActive(false); TerrainPanel?.Close(); TerrainPanel = null; Builder.SetActive(true); BuildPanel = new BuildWindow(Context, Builder); BuildPanel.Open();
        }

        /// <summary>T: starts or ends the terrain tools.</summary>
        public void ToggleTerrain()
        {
            if (Current != Phase.Playing || TerrainTools == null) return;
            if (TerrainTools.Active) { TerrainPanel?.Close(); TerrainPanel = null; TerrainTools.SetActive(false); return; }
            Builder.SetActive(false); BuildPanel?.Close(); BuildPanel = null; TerrainTools.SetActive(true); TerrainPanel = new TerrainWindow(Context, TerrainTools); TerrainPanel.Open();
        }

        private void TearDownWorld() { Plates?.Clear(); Plates = null; if (Follow != null && _island != Guid.Empty) Settings.IslandYaw[_island.ToString("N")] = Follow.Yaw; Effects = null; Texts = null; HudWorn?.Clear(); HudWorn = null; Ambient = null; Sounds = null; ParcelMusic = null; Voice = null; Hearing = null; BuildPanel?.Close(); TerrainPanel?.Close(); Builder?.SetActive(false); TerrainTools?.SetActive(false); BuildPanel = null; TerrainPanel = null; Builder = null; TerrainTools = null; ScriptDialogs?.Clear(); ScriptPanels?.Clear(); if (_world != null) Destroy(_world); _world = null; Client = null; Player = null; Follow = null; }

        // ---- while playing ----

        private void OnChat(LifeVerse.Wire.ReceiveChatMessage chat) => Hud.Chat.Add(chat.PeerId, chat.DisplayName, chat.Message, DateTimeOffset.FromUnixTimeMilliseconds(chat.UnixMilliseconds));
        private void OnSpawned(LifeVerse.Wire.SpawnPlayerMessage spawn) { if (spawn.PeerId != Client.SelfPeerId) Notices.Show(spawn.DisplayName + " chegou.", NoticeKind.Info, 3f); }
        private void OnDespawned(LifeVerse.Wire.DespawnPlayerMessage gone) { var view = PeopleViews?.ViewOf(gone.PeerId); if (view != null && gone.PeerId != Client.SelfPeerId) Notices.Show(view.DisplayName + " saiu.", NoticeKind.Info, 3f); }

        /// <summary>When the connection to the island drops: with "reconectar sozinho" on, the island is entered again (up to three times in a row); otherwise, or after that, back to the login.</summary>
        private void OnDisconnected(string reason)
        {
            if (Current != Phase.Playing) return;
            if (Store != null && Store.Bool("network.auto_reconnect") && Reconnects < 3 && _island != Guid.Empty) { Reconnects++; Notices.Show("A conexão com a ilha caiu. Tentando voltar (" + Reconnects + " de 3)…", NoticeKind.Warn); SwitchIsland(_island); return; }
            Reconnects = 0; BackToLogin(); Login.Fail("A conexão com a ilha foi perdida. Entre de novo.");
        }

        /// <summary>The tries to come back after the connection dropped (they start again once inside).</summary>
        public int Reconnects { get; private set; }
        /// <summary>For the checks: the island drops the connection as if the network failed.</summary>
        public void SimulateDrop() => OnDisconnected("test");

        private float _mapClock, _recentClock;

        /// <summary>The other avatars of the island and how far they are (metres).</summary>
        public IEnumerable<(string Name, float Distance)> NearbyPeople()
        {
            var views = PeopleViews; if (views == null || Player == null || Client == null) yield break; var me = Player.GamePosition;
            foreach (var peer in views.Peers) { if (peer == Client.SelfPeerId) continue; var v = views.ViewOf(peer); if (v != null && v.DisplayName.Length > 0) yield return (v.DisplayName, Vector3.Distance(v.GamePosition, me)); }
        }

        /// <summary>The people seen lately by this account (the list itself: the window may clear it).</summary>
        public List<string> RecentPeople()
        {
            var key = Api != null && Api.UserId != Guid.Empty ? Api.UserId.ToString() : "local";
            if (!Settings.RecentPeople.TryGetValue(key, out var list)) Settings.RecentPeople[key] = list = new List<string>(); return list;
        }

        /// <summary>Goes next to a person who is in this island (the island checks the teleport).</summary>
        public bool GoToPerson(string name)
        {
            var views = PeopleViews; if (views == null || Client == null) return false;
            foreach (var peer in views.Peers) { var v = views.ViewOf(peer); if (peer != Client.SelfPeerId && v != null && string.Equals(v.DisplayName, name, StringComparison.OrdinalIgnoreCase)) return Client.RequestTeleport("", v.GamePosition.x + 1.5f, v.GamePosition.z); }
            Notices.Show(name + " não está nesta ilha agora.", NoticeKind.Info); return false;
        }

        /// <summary>Who is around goes to the front of the recent list (saved when it changes).</summary>
        private void NoteRecent()
        {
            var list = RecentPeople(); var updated = PeopleHubWindow.Remember(list, NearbyPeople().OrderBy(p => p.Distance).Select(p => p.Name));
            if (updated.SequenceEqual(list)) return; list.Clear(); list.AddRange(updated); Settings.Save(SettingsPath);
        }

        /// <summary>The name plates over the avatars.</summary>
        public NamePlates Plates { get; private set; }
        private void LateUpdate() { if (Current == Phase.Playing && Plates != null && Follow != null) Plates.Tick(PeopleViews, Follow.Camera, Client != null ? Client.SelfPeerId : -1, Hud.Presence.value); }

        private void Update()
        {
            TickLoginMusic(); SettingsEffects.RenderScale.Tick(Time.unscaledDeltaTime);
            if (Current != Phase.Playing || Client == null) return;
            var views = PeopleViews; Hud.SetConnection(Client.Connected, Mathf.Max(0, (views?.Count ?? 1) - 1));
            if (Hud.PerformanceVisible) Hud.Perf.Frame(Time.unscaledDeltaTime);
            _mapClock += Time.unscaledDeltaTime;
            if (_mapClock > .25f && Player != null && Client.Terrain != null)
            {
                _mapClock = 0f; Hud.Map.Paint(Client.Terrain.Map);
                var others = new List<Vector3>(); if (views != null) foreach (var peer in views.Peers) if (peer != Client.SelfPeerId) others.Add(views.ViewOf(peer).GamePosition);
                Hud.Map.Place(Player.GamePosition, others);
            }
            _recentClock += Time.unscaledDeltaTime; if (_recentClock > 2f) { _recentClock = 0f; NoteRecent(); }
            Builder?.Tick(Time.unscaledDeltaTime); TerrainTools?.Tick(Time.unscaledDeltaTime);
            if (Ambient != null && Client.Terrain != null && Client.Terrain.Map != null && Player != null)
            {
                var spot = Player.GamePosition; var edge = Client.Terrain.Map.HalfExtent - Mathf.Max(Mathf.Abs(spot.x), Mathf.Abs(spot.z)); var weather = Client.Environment != null ? Client.Environment.WeatherShown : "clear";
                Ambient.SetTargets(AmbientLevels.Compute(edge, Player.State.Swimming, spot.y, weather));
                if (Player.State.Swimming && new Vector2(Player.State.VelocityX, Player.State.VelocityZ).magnitude > .5f && Time.unscaledTime > _nextSplash) { _nextSplash = Time.unscaledTime + 1.05f; Ambient.Splash(); }
            }
            if (Voice != null) Voice.TalkHeld = KeyBindings.Held(GameAction.Talk);
            if (Hearing != null && PeopleViews != null) foreach (var peer in PeopleViews.Peers) { var view = PeopleViews.ViewOf(peer); if (view != null) view.SetSpeaking(peer == Client.SelfPeerId ? Voice != null && Voice.Talking : Hearing.IsSpeaking(peer)); }
            if (Keyboard.current is { } keyboard && !Hud.Chat.Typing)
            {
                if (KeyBindings.Pressed(GameAction.Chat)) Hud.Chat.Focus();
                if (KeyBindings.Pressed(GameAction.Performance)) Hud.TogglePerformance();
                if (Follow != null && Follow.ZoomTarget.HasValue && (keyboard.escapeKey.wasPressedThisFrame || KeyBindings.Pressed(GameAction.Forward) || KeyBindings.Pressed(GameAction.Back) || KeyBindings.Pressed(GameAction.Left) || KeyBindings.Pressed(GameAction.Right) || KeyBindings.Pressed(GameAction.Jump))) Follow.ZoomTarget = null;
                if (KeyBindings.Pressed(GameAction.Build)) ToggleBuild();
                if (KeyBindings.Pressed(GameAction.Terrain)) ToggleTerrain();
                var editing = (Builder != null && Builder.Active) || (TerrainTools != null && TerrainTools.Active);
                var cameraGesture = Follow != null && (Follow.Gesture != LifeVerse.Shared.Camera.CameraGesture.None || LifeVerse.Shared.Camera.CameraInputRules.IsCameraGesture(keyboard.ctrlKey.isPressed, keyboard.altKey.isPressed, keyboard.shiftKey.isPressed, Follow.FreeOptions));
                if (!editing && !cameraGesture && Mouse.current is { } mouse && mouse.leftButton.wasPressedThisFrame && UnityEngine.Cursor.lockState != CursorLockMode.Locked && !UiKit.IsOver(Ui.Root, mouse.position.ReadValue())) ClickWorld(mouse.position.ReadValue());
                if (Mouse.current is { } right)
                {
                    // a quick click of the right button (little movement) opens the menu of what is under it; held and dragged, it turns the camera
                    if (right.rightButton.wasPressedThisFrame) { _rightAt = right.position.ReadValue(); _rightTime = Time.unscaledTime; _rightMoved = 0f; }
                    else if (right.rightButton.isPressed) _rightMoved += right.delta.ReadValue().magnitude;
                    if (right.rightButton.wasReleasedThisFrame && !editing && Time.unscaledTime - _rightTime < .35f && _rightMoved < 8f && UnityEngine.Cursor.lockState != CursorLockMode.Locked && !Hud.Radial.IsOpen && !UiKit.IsOver(Ui.Root, _rightAt)) OpenContextMenu(_rightAt);
                }
                if (keyboard.f10Key.wasPressedThisFrame) Hud.ToggleReveal();
                if (keyboard.f12Key.wasPressedThisFrame) TakePhoto();
                if (KeyBindings.Pressed(GameAction.Radial) && !Hud.Radial.IsOpen) OpenRadial(Mouse.current != null ? new Vector2(Mouse.current.position.ReadValue().x, Screen.height - Mouse.current.position.ReadValue().y) / Ui.Panel.scale : new Vector2(Screen.width / 2f, Screen.height / 2f));
            }
        }

        // ---- objects of the world: click one to touch it (scripts answer), sit on it or open its scripts ----

        /// <summary>The object the last click picked.</summary>
        public Guid ClickedObject { get; private set; }
        public FloatingWindow ObjectMenu { get; private set; }

        /// <summary>Opens the menu of the object a click on the screen points at. False when the click hits no object (or the person is not playing).</summary>
        public bool ClickWorld(Vector2 screen, Vector2? menuAt = null)
        {
            if (Current != Phase.Playing || Client?.Objects == null || Follow == null || Follow.Camera == null) return false;
            if (!Client.Objects.Pick(Follow.Camera.ScreenPointToRay(screen), 40f, out var id, out var box)) return false;
            ClickedObject = id; var state = Client.Objects.StateOf(id); var top = LifeVerse.Unity.World.GameSpace.ToGame(new Vector3(box.center.x, box.max.y, box.center.z));
            ObjectMenu?.Close(); var at = menuAt ?? new Vector2(screen.x, Screen.height - screen.y) / Ui.Panel.scale;
            var menu = new FloatingWindow(Ui, "object-menu", state != null && state.Name.Length > 0 ? state.Name : "Objeto", 220f, -1f, at); menu.MoveTo(at); ObjectMenu = menu;
            menu.Body.Add(UiKit.Button("Tocar", () => { TouchObject(id); menu.Close(); }, "lv-btn-primary"));
            menu.Body.Add(UiKit.Button("Sentar aqui", () => { SitOnObject(top); menu.Close(); }));
            menu.Body.Add(UiKit.Button("Dar zoom", () => { ZoomTo(box); menu.Close(); }));
            menu.Body.Add(UiKit.Button("Scripts do objeto", () => { new LuaEditorWindow(Context, _island, id).Open(); menu.Close(); }));
            if (state != null)
            {
                var (saleType, price) = BuildEditor.SaleOf(state); var payPrice = BuildEditor.PayPriceOf(state);
                if (saleType != "none" && price >= 0) menu.Body.Add(UiKit.Button("Comprar por VC$ " + price.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + "…", () => { new PurchaseWindow(Context, Builder, state).Open(); menu.Close(); }, "lv-btn-primary"));
                if (payPrice > 0) menu.Body.Add(UiKit.Button("Pagar VC$ " + payPrice.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + "…", () => { new PaymentWindow(Context, state).Open(); menu.Close(); }, "lv-btn-primary"));
                var carried = BuildCarried(state); if (carried.Count > 0) menu.Body.Add(UiKit.Button("Abrir (" + carried.Count + " itens)", () => { OpenObjectContents(state, carried); menu.Close(); }));
            }
            menu.Open(); return true;
        }

        /// <summary>The names of what an object carries (sounds, textures, objects…).</summary>
        public static List<string> BuildCarried(WorldObjectState state)
        {
            var names = new List<string>();
            try { foreach (var item in (System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(state.ContentJson) ? "[]" : state.ContentJson) as System.Text.Json.Nodes.JsonArray ?? new System.Text.Json.Nodes.JsonArray()).OfType<System.Text.Json.Nodes.JsonObject>()) names.Add(item["name"]?.GetValue<string>() ?? "Item"); }
            catch (Exception exception) when (exception is System.Text.Json.JsonException || exception is InvalidOperationException) { }
            return names;
        }

        private void OpenObjectContents(WorldObjectState state, List<string> names)
        {
            var window = new FloatingWindow(Ui, "object-contents", state.Name, 320f, 300f, new Vector2(420, 140)); foreach (var name in names) window.Body.Add(UiKit.Text("• " + name, "lv-body")); window.Open();
        }

        /// <summary>Pays an object that takes payments: a transfer to its owner, which the platform checks (balance, wallet).</summary>
        public void PayObject(WorldObjectState state, long amount)
        {
            Api.ApiAsync("POST", "/api/v1/economy/transfer", System.Text.Json.JsonSerializer.Serialize(new { recipientUserId = state.OwnerUserId, amountCoins = amount, idempotencyKey = Guid.NewGuid().ToString("N"), message = "Pagamento a " + state.Name })).ContinueWith(task => { });
            Notices.Show("Pagamento enviado.", NoticeKind.Info);
        }

        /// <summary>The camera looks at the object from a distance that fits it; walking or Esc brings it back to the person.</summary>
        public void ZoomTo(UnityEngine.Bounds box) { if (Follow == null) return; Follow.ZoomTarget = box.center; Follow.ZoomDistance = Mathf.Clamp(box.size.magnitude * 1.4f, 2f, FollowCamera.MaxDistance); }

        /// <summary>Touches an object: its scripts get the on_touch event (the island decides what the touch does).</summary>
        public bool TouchObject(Guid id) => Client != null && Client.Send(new LifeVerse.Wire.TouchObjectMessage(id.ToString()));

        /// <summary>Sits on top of a point (an object's top), within six metres of the person.</summary>
        public bool SitOnObject(Vector3 topGame)
        {
            if (Player == null || (topGame - Player.GamePosition).magnitude > 6f) { Notices.Show("Chegue mais perto para sentar.", NoticeKind.Info); return false; }
            return Player.SitOnObject(topGame, Player.HeadingDegrees);
        }

        /// <summary>The actions of the radial menu: the gestures, flying, sitting, the car and the first person.</summary>
        public List<RadialItem> RadialItems() => new List<RadialItem>
        {
            new RadialItem("Acenar", () => Player?.StartGesture("wave")), new RadialItem("Dançar", () => Player?.StartGesture("dance")), new RadialItem("Aplaudir", () => Player?.StartGesture("applaud")), new RadialItem("Apontar", () => Player?.StartGesture("point")),
            new RadialItem("Cumprimentar", () => Player?.StartGesture("greet")), new RadialItem("Rir", () => Player?.StartGesture("laugh")), new RadialItem("Sentar no chão", () => Player?.StartGesture("sitfloor")), new RadialItem("Carro", () => Player?.ToggleVehicle()),
            new RadialItem("Primeira pessoa", () => Follow?.Toggle()), new RadialItem("Voar", () => Player?.RequestFlight()), new RadialItem("Construir", ToggleBuild), new RadialItem("Terreno", ToggleTerrain),
        };

        public void OpenRadial(Vector2 at) => Hud.Radial.Open(Ui, RadialItems(), at);

        // ---- the free camera, the menu of the right button, the interface modes, the islands visited and the photos ----

        private Vector2 _rightAt; private float _rightTime, _rightMoved;
        public readonly IslandHistory Islands = new IslandHistory();

        /// <summary>The point a ray of the screen hits, in the Unity scene: the nearest of a person (their chest), an object (the centre of its box) and the ground.</summary>
        public Vector3? PickPoint(Ray ray)
        {
            Vector3? best = null; void Keep(Vector3 point) { if (best == null || Vector3.Distance(ray.origin, point) < Vector3.Distance(ray.origin, best.Value)) best = point; }
            var person = AvatarPick.Pick(ray, People()); var view = person >= 0 ? PeopleViews?.ViewOf(person) : null; if (view != null) Keep(LifeVerse.Unity.World.GameSpace.ToUnity(view.GamePosition) + Vector3.up * 1.1f);
            if (Client?.Objects != null && Client.Objects.Pick(ray, 80f, out _, out var box)) Keep(box.center);
            if (Client?.Terrain != null && Client.Terrain.Raycast(ray) is { } ground) Keep(ground);
            return best;
        }

        /// <summary>The people of the island and where their feet are, in the Unity scene.</summary>
        private List<(int, Vector3)> People()
        {
            var views = PeopleViews; var people = new List<(int, Vector3)>();
            if (views != null) foreach (var peer in views.Peers) { var v = views.ViewOf(peer); if (v != null) people.Add((peer, LifeVerse.Unity.World.GameSpace.ToUnity(v.GamePosition))); }
            return people;
        }

        /// <summary>What the right button points at: "self", "person", "object", "ground" (or "" when nothing).</summary>
        public string ContextTarget { get; private set; } = "";

        /// <summary>The actions for what is under a point of the screen: the person (the nearest of the avatars the ray passes by), an object, or the ground.</summary>
        public List<RadialItem> ContextItems(Vector2 screen)
        {
            var items = new List<RadialItem>(); ContextTarget = "";
            if (Current != Phase.Playing || Follow == null || Follow.Camera == null || Client == null) return items;
            var ray = Follow.Camera.ScreenPointToRay(screen); var views = PeopleViews;
            var person = AvatarPick.Pick(ray, People()); var id = Guid.Empty; var box = default(UnityEngine.Bounds); var hitObject = Client.Objects != null && Client.Objects.Pick(ray, 80f, out id, out box);
            var view = person >= 0 ? views.ViewOf(person) : null;
            if (view != null && (!hitObject || Vector3.Distance(ray.origin, LifeVerse.Unity.World.GameSpace.ToUnity(view.GamePosition)) <= Vector3.Distance(ray.origin, box.center) + .5f))
            {
                var head = LifeVerse.Unity.World.GameSpace.ToUnity(view.GamePosition) + Vector3.up * 1.6f;
                if (person == Client.SelfPeerId)
                {
                    ContextTarget = "self";
                    items.Add(new RadialItem("Meu perfil", () => new ProfileHubWindow(Context, Api.UserId).Open())); items.Add(new RadialItem("Avatar", () => OpenAvatarStudio().Open()));
                    items.Add(new RadialItem("Gestos", () => OpenRadial(UiPoint(screen)))); items.Add(new RadialItem("Sentar no chão", () => Player?.StartGesture("sitfloor")));
                    items.Add(new RadialItem("Primeira pessoa", () => Follow?.Toggle())); items.Add(new RadialItem("Inventário", () => Hud.Toggle("inventory", () => new InventoryManagerWindow(Context, Store))));
                }
                else
                {
                    ContextTarget = "person"; var name = view.DisplayName;
                    items.Add(new RadialItem("Perfil e ações", () => OpenPersonActions(name), name.Length > 0));
                    items.Add(new RadialItem("Dar zoom", () => { if (Follow == null) return; Follow.ZoomTarget = head; Follow.ZoomDistance = 2.5f; }));
                }
                return items;
            }
            if (hitObject)
            {
                ContextTarget = "object"; var top = LifeVerse.Unity.World.GameSpace.ToGame(new Vector3(box.center.x, box.max.y, box.center.z)); var hit = box; var obj = id;
                items.Add(new RadialItem("Tocar", () => TouchObject(obj))); items.Add(new RadialItem("Sentar aqui", () => SitOnObject(top))); items.Add(new RadialItem("Dar zoom", () => ZoomTo(hit)));
                items.Add(new RadialItem("Scripts", () => new LuaEditorWindow(Context, _island, obj).Open())); items.Add(new RadialItem("Editar", () => { if (Builder != null && !Builder.Active) ToggleBuild(); Builder?.Select(new[] { obj }); }, Builder != null));
                items.Add(new RadialItem("Mais…", () => ClickWorld(screen)));
                return items;
            }
            if (Client.Terrain != null && Client.Terrain.Raycast(ray) is { } ground)
            {
                ContextTarget = "ground"; var spot = LifeVerse.Unity.World.GameSpace.ToGame(ground);
                items.Add(new RadialItem("Teleportar para cá", () => Client?.RequestTeleport("", spot.x, spot.z))); items.Add(new RadialItem("Sobre este terreno", () => OpenParcelAt(spot)));
                items.Add(new RadialItem("Olhar para cá", () => Follow?.FocusAt(ray))); items.Add(new RadialItem("Construir aqui", ToggleBuild));
            }
            return items;
        }

        private Vector2 UiPoint(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y) / Ui.Panel.scale;

        /// <summary>The menu of the right button for a point of the screen (nothing when there is nothing there).</summary>
        public bool OpenContextMenu(Vector2 screen) { var items = ContextItems(screen); if (items.Count == 0) return false; Hud.Radial.Open(Ui, items, UiPoint(screen)); return true; }

        private FloatingWindow OpenAvatarStudio() => new AvatarStudioWindow(Context, () => OpenCharacter()?.Open(), on => { if (Follow != null) Follow.Mirror = on; }, RefreshOwnAvatar);

        /// <summary>The interface mode (full, compact, immersive) and the toolbar (order, hidden buttons, small) from the settings.</summary>
        public void ApplyInterface() => Hud.ApplyLayout(Store.Get("interface.mode"), Store.Bool("interface.compact_toolbar"), Settings.ToolbarOrder, Settings.ToolbarCustomized ? Settings.ToolbarHidden : (IReadOnlyCollection<string>)Hud.DefaultHidden);

        /// <summary>Back (-1) or forward (+1) among the islands visited in this session.</summary>
        public void TravelIslands(int step)
        {
            var island = step < 0 ? Islands.PeekBack : Islands.PeekForward; if (island == Guid.Empty || Current != Phase.Playing) return;
            Notices.Show(step < 0 ? "Voltando à ilha anterior…" : "Indo para a próxima ilha…", NoticeKind.Info); Teleport(island);
        }

        /// <summary>The window that orders and hides the buttons of the bottom toolbar.</summary>
        public FloatingWindow ToolbarWindow()
        {
            var window = new FloatingWindow(Ui, "toolbar-layout", "Barra de ferramentas", 340f, 460f, new Vector2(420, 120)); var list = new ScrollView(); list.style.flexGrow = 1; list.style.flexShrink = 1; list.style.maxHeight = 340; list.style.overflow = Overflow.Hidden; list.contentViewport.style.overflow = Overflow.Hidden; window.Body.Add(list);
            void Fill()
            {
                if (!Settings.ToolbarCustomized) { Settings.ToolbarHidden = new List<string>(Hud.DefaultHidden); Settings.ToolbarCustomized = true; }   // from the first change on, the bar is the person's
                list.Clear(); var order = ToolbarLayout.Order(Hud.ToolbarDefaults, Settings.ToolbarOrder);
                foreach (var id in order)
                {
                    var key = id; var label = Hud.BottomBar.Q<Button>("btn-" + id)?.text ?? id; var hidden = Settings.ToolbarHidden.Contains(id);
                    var toggle = new Toggle { value = !hidden }; toggle.RegisterValueChangedCallback(e => { if (!e.newValue && !ToolbarLayout.CanHide(order, Settings.ToolbarHidden, key)) { toggle.SetValueWithoutNotify(true); Notices.Show("A barra precisa de pelo menos um botão.", NoticeKind.Warn); return; } if (e.newValue) Settings.ToolbarHidden.Remove(key); else if (!Settings.ToolbarHidden.Contains(key)) Settings.ToolbarHidden.Add(key); Settings.Save(SettingsPath); ApplyInterface(); });
                    list.Add(UiKit.Row(toggle, UiKit.Text(label, "lv-body"), UiKit.Spacer(), UiKit.Button("◀", () => { Settings.ToolbarOrder = ToolbarLayout.Move(order, key, -1); Settings.Save(SettingsPath); ApplyInterface(); Fill(); }), UiKit.Button("▶", () => { Settings.ToolbarOrder = ToolbarLayout.Move(order, key, 1); Settings.Save(SettingsPath); ApplyInterface(); Fill(); })));
                }
            }
            Fill(); window.Body.Add(UiKit.Button("Restaurar a barra", () => { RestoreToolbar(); Fill(); })); return window;
        }

        public void RestoreToolbar() { Settings.ToolbarOrder.Clear(); Settings.ToolbarHidden.Clear(); Settings.ToolbarCustomized = false; Store.Set("interface.compact_toolbar", "false"); Settings.Save(SettingsPath); ApplyInterface(); }

        /// <summary>The last picture taken (its file), for the checks.</summary>
        public string LastPhoto { get; private set; }

        /// <summary>F12: a picture of the world (without the interface unless the setting says so) in Pictures/LifeVerse.</summary>
        public void TakePhoto() { if (Current == Phase.Playing) StartCoroutine(Photo()); }

        private IEnumerator Photo()
        {
            var format = Store.Get("camera.photo_format") == "jpg" ? "jpg" : "png"; var withInterface = Store.Bool("camera.photo_include_ui"); var shown = Ui.Root.style.display;
            if (!withInterface) { Ui.Root.style.display = DisplayStyle.None; yield return null; }
            yield return new WaitForEndOfFrame();
            var picture = ScreenCapture.CaptureScreenshotAsTexture(); if (!withInterface) Ui.Root.style.display = shown;
            try
            {
                Directory.CreateDirectory(WorldPhotos.Folder); var path = Path.Combine(WorldPhotos.Folder, WorldPhotos.NameFor(DateTime.Now, format)); File.WriteAllBytes(path, WorldPhotos.Encode(picture, format));
                LastPhoto = path; Notices.Show("Foto salva em " + path, NoticeKind.Good);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { Notices.Show("Não foi possível salvar a foto: " + exception.Message, NoticeKind.Bad); }
            finally { Destroy(picture); }
        }

        /// <summary>Opens "Sobre este Terreno" for the parcel at a point of the island.</summary>
        public void OpenParcelAt(Vector3 game)
        {
            if (_island == Guid.Empty) return;
            Ui.ScreenLayer.Then(Api.ApiAsync("GET", "/api/v1/worlds/islands/" + _island + "/land"), r =>
            {
                var parcel = r.Status == 200 ? ParcelInfoWindow.ParcelAt(r.Body, game.x, game.z) : Guid.Empty;
                if (parcel == Guid.Empty) { Notices.Show(r.Status == 200 ? "Aqui não há terreno." : UiTasks.Explain(r.Status, r.Body), NoticeKind.Info); return; }
                LastParcelInfo = new ParcelInfoWindow(Context, _island, parcel, Here); LastParcelInfo.Open();
            });
        }

        /// <summary>The values of the settings catalog (the Configurações window), over <see cref="Settings"/>.</summary>
        public SettingsStore Store { get; private set; }

        /// <summary>After a change in Configurações: the engine, the interface, the file, the world, and the account when the sync is on.</summary>
        public void SettingsApplied()
        {
            Settings.Clamped(); GraphicsQuality.Apply(Settings); Ui.ApplySettings(); Settings.Save(SettingsPath); ApplySettingsToWorld();
            if (Sync != null && Sync.Enabled) _ = Sync.PushAppliedAsync();
        }

        private void OnSettingChanged(string id)
        {
            if (id.StartsWith("metrics.", StringComparison.Ordinal)) SettingsEffects.Metrics(Store, Hud);
            else if (id == "camera.default_distance" && Follow != null) Follow.Distance = Store.Float(id);
            else if (id == "camera.default_mode" && Follow != null) Follow.SetFirstPerson(Store.Get(id) == "first");
        }

        /// <summary>The buttons of Configurações that do something once.</summary>
        public Dictionary<string, Action> SettingActions() => new Dictionary<string, Action>
        {
            ["account.leave_island"] = () => { if (Current == Phase.Playing) BackToLogin(); },
            ["account.quit"] = Application.Quit,
            ["advanced.open_data"] = () => Application.OpenURL("file:///" + Application.persistentDataPath.Replace(System.IO.Path.DirectorySeparatorChar, '/')),
            ["advanced.open_logs"] = () => Application.OpenURL("file:///" + System.IO.Path.GetDirectoryName(Application.consoleLogPath ?? Application.persistentDataPath)?.Replace(System.IO.Path.DirectorySeparatorChar, '/')),
            ["advanced.export_diagnostics"] = () => Notices.Show("Diagnóstico salvo em " + SettingsEffects.WriteDiagnostics(Store, this), NoticeKind.Good, 10f),
            ["advanced.restore_all"] = () => { Store.Restore(); SettingsApplied(); },
            ["advanced.clear_cache"] = () => { var n = SettingsEffects.ClearCache(); Notices.Show(n == 0 ? "O cache já estava vazio." : n + " arquivo(s) apagado(s): eles serão baixados de novo quando o jogo precisar.", NoticeKind.Info); },
            ["interface.restore_toolbar"] = () => { RestoreToolbar(); Notices.Show("A barra de ferramentas voltou ao padrão.", NoticeKind.Info); },
            ["interface.restore_layout"] = () => { Settings.Windows.Clear(); SettingsApplied(); Notices.Show("As janelas voltam ao lugar de origem ao serem abertas de novo.", NoticeKind.Info); },
            ["interface.restore_all"] = () => { Store.Restore("Interface"); Settings.Windows.Clear(); SettingsApplied(); },
            ["avatar.open_editor"] = () => OpenCharacter()?.Open(),
            ["chat.open_people"] = () => new SocialWindow(Context).Open(),
        };

        public SettingsWindow OpenClassicSettings()
        {
            var window = new SettingsWindow(Ui, SettingsPath) { Mic = _micTest ??= gameObject.AddComponent<MicTest>(), Sync = Sync }; window.Changed += ApplySettingsToWorld; window.Closed += () => _micTest?.End(); return window;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (Store == null) return; AudioListener.pause = !focused && Store.Bool("audio.mute_background");
            var background = Store.Get("perf.background_fps"); if (!focused && background != "off" && int.TryParse(background, out var fps)) Application.targetFrameRate = fps; else if (focused) GraphicsQuality.Apply(Settings);
        }

        /// <summary>Puts the settings on the world: mouse, run, camera comfort, shadows and rain by the graphics preset.</summary>
        public void ApplySettingsToWorld()
        {
            if (Follow != null) { Follow.Sensitivity = Settings.MouseSensitivity; Follow.InvertY = Settings.InvertY; Follow.Options = Follow.Options with { HeadBob = !Settings.ReduceMotion, Sway = !Settings.ReduceMotion }; }
            if (Player != null) Player.AlwaysRun = Settings.AlwaysRun;
            Hud.Chat.ApplySettings(); ApplyAudio(); ApplyInterface();
            var environment = _world != null ? _world.GetComponentInChildren<IslandEnvironment>() : null;
            if (Effects != null) Effects.Quality = Mathf.Min(1f, GraphicsQuality.Current.RainAmount);
            if (environment != null) { var preset = GraphicsQuality.Current; environment.RainQuality = preset.RainAmount; environment.SetShadows(preset.Shadows, preset.ShadowDistance); }
            // the settings of the catalog (Configurações): camera, graphics beyond the preset, toasts, build grid, footsteps, performance numbers
            SettingsEffects.Camera(Store, Follow, Settings.ReduceMotion);
            SettingsEffects.Graphics(Store, Follow != null ? Follow.Camera : null, environment != null ? (on, distance) => environment.SetShadows(on, distance) : (Action<bool, float>)null,
                amount => { if (Effects != null) Effects.Quality = Mathf.Min(1f, GraphicsQuality.Current.RainAmount) * amount; if (environment != null) environment.RainQuality = GraphicsQuality.Current.RainAmount * amount; });
            SettingsEffects.Effects(Store, environment, Follow != null ? Follow.Camera : null); SettingsEffects.Interface(Store, Hud, Ui);
            WorldObjects.MaxLights = Store.Int("world.max_lights"); WorldObjects.MaxLightShadows = Store.Int("world.max_light_shadows"); if (Client != null && Client.Objects != null) Client.Objects.RefreshBudget();
            SettingsEffects.Toasts(Store, Notices); SettingsEffects.Build(Store, Builder); LifeVerse.Unity.Avatar.AvatarSounds.FootstepsEnabled = Store.Bool("avatar.footsteps");
            if (Store.Bool("metrics.fps") || Store.Bool("metrics.frame_time") || Store.Bool("metrics.memory")) SettingsEffects.Metrics(Store, Hud);
        }

        // ---- moving between screens ----

        public void Show(Phase phase)
        {
            Current = phase; Ui.ScreenLayer.Clear(); if (LoginTheme != null) LoginTheme.FadeTarget = phase == Phase.Login ? 1f : 0f;
            if (phase == Phase.Login) { Ui.ScreenLayer.Add(Login); Hud.Hide(); }
            else if (phase == Phase.Loading) { Ui.ScreenLayer.Add(Loading); Hud.Hide(); }
            else Hud.Show();
        }

        public void BackToLogin()
        {
            Live?.End(); SetBell(0);
            if (Client != null) { Client.DisconnectedBy -= OnDisconnected; Client.ChatReceived -= OnChat; Client.PlayerSpawned -= OnSpawned; Client.PlayerDespawned -= OnDespawned; }
            if (_entering != null) { StopCoroutine(_entering); _entering = null; }
            TearDownWorld(); Show(Phase.Login); Login.SetBusy(false);
        }

        private void OnDestroy() { _api?.Dispose(); }
    }
}
