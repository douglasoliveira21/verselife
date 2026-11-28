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
        public const string DefaultApi = "https://app.lifeverse.euporiagames.com";
        public enum Phase { Login, Loading, Playing }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode || Environment.GetCommandLineArgs().Any(a => a.StartsWith("-lifeverse-") && a != "-lifeverse-app")) return;
            if (!Application.isPlaying) return;
            var app = new GameObject("LifeVerse").AddComponent<GameApp>(); app.ShowWelcome = true;
            app.Begin(Environment.GetEnvironmentVariable("LIFEVERSE_API_URL") is { Length: > 0 } url ? url : DefaultApi);
        }

        /// <summary>Whether "Primeiros passos" opens by itself on the first entry (the game a person opens; the checks, which make their own, leave it off).</summary>
        public bool ShowWelcome { get; set; }
        /// <summary>The guide opened last.</summary>
        public WelcomeWindow Welcome { get; private set; }

        /// <summary>Opens "Primeiros passos" and remembers whether it should open by itself again.</summary>
        public WelcomeWindow OpenWelcome()
        {
            Welcome = new WelcomeWindow(Ui, Settings.WelcomeDone, done => { if (Settings.WelcomeDone == done) return; Settings.WelcomeDone = done; Settings.Save(SettingsPath); });
            Welcome.Open(); return Welcome;
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
            ApiBase = apiBase; Settings = settings ?? UserSettings.Load(SettingsPath); GraphicsQuality.Apply(Settings); Store = new SettingsStore(Settings); Store.Set("world.minimap_open", "false", false);   // as in the Godot client, the game always starts with the mini-map closed
            Store.Changed += OnSettingChanged; if (Store.Bool("general.keep_on_top")) SettingsEffects.KeepOnTop(true); ModelImportWindow.CollisionChosen = shape => Store.Set("build.import_collision", shape, false);
            Ui = UiRoot.Create(Settings); Notices = new NotificationCenter(Ui); Hud = new Hud(Ui, Notices);
            GameKeyboard.Guard(Ui.Root.panel.visualTree, () => Current == Phase.Playing);
            Login = new LoginView(Settings, "Versão " + Application.version + "  •  Unity"); Login.SignIn += OnSignIn; Login.Register += OnRegister; Ui.ScreenLayer.Add(Login);
            Loading = new LoadingView(); Loading.Back.clicked += BackToLogin;
            _api = new IslandEntryClient(apiBase); _api.SessionExpired += () => _sessionGone = true;   // told from a worker thread: the screens change on the next frame
            Context = new ApiContext { Ui = Ui, Api = _api, Notices = Notices, CurrentIsland = () => _island, EnterIsland = id => Teleport(id), TravelTo = (id, x, z) => Teleport(id, x, z) };
            Hud.Map.RegisterCallback<WheelEvent>(e => { if (Store == null) return; ZoomMap(e.delta.y > 0 ? 1.15f : 1f / 1.15f); e.StopPropagation(); });
            Hud.Map.ZoomAsked = ZoomMap; Hud.Map.CloseAsked = () => SetMinimap(false);
            Subtitles = new Captions(); Hud.Chat.Load = LoadConversation; Hud.Chat.SendConversation = SendConversation; Context.OpenConversation = OpenConversation; Context.Live += OnLiveMessage;
            RegisterWindows(); Hud.Chat.Send = text => Client != null && Client.SendChat(text);
            Hud.Chat.IsMine = peer => Client != null && peer == Client.SelfPeerId;
            Hud.Chat.TypingChanged += typing => { if (Player != null) Player.Typing = typing; };
            Hud.Perf.Extra = () => (Store != null && Store.Bool("metrics.cpu_gpu") ? SettingsEffects.BusyNow() + "\n" : "") + (Store != null && Store.Bool("network.ping") ? Network.Describe() + "\n" : "") + (Store != null && Store.Bool("metrics.memory") ? "memória " + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576L) + " MB   " : "") + (Client == null ? "" : "pessoas " + (PeopleViews?.Count ?? 0) + "   objetos " + Client.Objects.Count + " (" + Client.Objects.CulledCount + " fora do alcance)   mensagens do mundo " + Client.Snapshots);
            if (SavedLogin.Load(SavedLoginPath) is { } saved) Login.FillSaved(saved.Login, saved.Password);
            SettingsEffects.Resources(Store); if (Store.Bool("advanced.verify_files")) VerifyFiles();
            ApplyAudio(); StartCoroutine(StartSounds()); StartCoroutine(LoadIslands()); StartCoroutine(WatchServers()); Login.MusicToggled += () => { Settings.LoginMusicMuted = !Settings.LoginMusicMuted; Login.SetMusic(Settings.LoginMusicMuted); Settings.Save(SettingsPath); };
            Sync = new AccountSync(Settings, (m, p, b) => _api.ApiAsync(m, p, b), () => _api.UserId, () => Settings.Save(SettingsPath), Store); UpdateInfo = new UpdateCheck(Application.version); Ui.Root.Then(_api.ApiAsync("GET", UpdateCheck.Route), r => { UpdateInfo.Apply(r.Status, r.Body); ShowUpdate(); });
        }

        /// <summary>Brings the mini-map closer (a factor below 1) or farther, and keeps the choice in the settings.</summary>
        public void ZoomMap(float factor)
        {
            if (Store == null) return;
            Store.Set("world.minimap_zoom", Mathf.RoundToInt(Mathf.Clamp(Hud.Map.ZoomMetres * factor, 40f, 1000f) / 10f) * 10 + ""); SettingsEffects.Interface(Store, Hud, Ui); Settings.Save(SettingsPath);
        }

        /// <summary>Opens the search (or uses the one already open) and looks for what was written on the navigation bar.</summary>
        public void SearchFor(string text)
        {
            var window = Hud.WindowOf("search") as GlobalSearchWindow ?? Hud.Toggle("search", SearchWindow) as GlobalSearchWindow; if (window == null) return;
            window.Raise(); if (!string.IsNullOrWhiteSpace(text)) window.Find(text);
        }

        private FloatingWindow SearchWindow() => new GlobalSearchWindow(Context, Settings, () => Settings.Save(SettingsPath), (island, x, z) => { if (x is { } px && z is { } pz) MoveToSpot(island, px, pz); else Context.EnterIsland(island); });
        private FloatingWindow MapWindow() => new WorldMapWindow(Context, () => Player != null ? Player.GamePosition : (Vector3?)null);
        private FloatingWindow PeopleWindow() => new PeopleHubWindow(Context, NearbyPeople, RecentPeople, OpenPersonActions, name => GoToPerson(name));
        private FloatingWindow SettingsPanel() => new SettingsCatalogWindow(Context, Store, SettingsApplied, id => SettingsEffects.Info(id, this), SettingActions(), () => OpenClassicSettings().Open());
        private FloatingWindow MovePanel() => new MoveWindow(Ui, () => Player, () => Follow);
        private FloatingWindow CameraPanel() => new CameraWindow(Ui, () => Follow, () => Player != null ? Player.HeadingDegrees : 0f);

        /// <summary>The name of the island on the navigation bar: the short code at once, the name when the platform tells it.</summary>
        private void NameIsland()
        {
            var island = _island; Hud.SetIsland("Ilha " + island.ToString().Substring(0, 8));
            Ui.ScreenLayer.Then(_api.ApiAsync("GET", "/api/v1/worlds/islands/" + island), r => { if (r.Status != 200 || island != _island) return; var name = Json.One(r.Body).Text("name"); if (!string.IsNullOrWhiteSpace(name)) Hud.SetIsland(name); });
        }

        // ---- captions and the check of the files ----
        public Captions Subtitles { get; private set; }
        private void TickCaptions()
        {
            if (Subtitles == null) return; var now = Time.unscaledTime;
            if (Store.Bool("accessibility.captions") && Client != null)
            {
                if (Subtitles.parent == null) Ui.HudLayer.Add(Subtitles);
                var views = PeopleViews; if (Hearing != null && views != null) foreach (var peer in views.Peers) if (peer != Client.SelfPeerId && Hearing.IsSpeaking(peer)) Subtitles.Say(views.ViewOf(peer).DisplayName + " está falando", now);
                if (Sounds != null && Follow != null && Follow.Camera != null) foreach (var (media, at) in Sounds.Audible()) { var metres = Vector3.Distance(at, Follow.Focus); if (metres <= 40f) Subtitles.Say(Captions.Sound(media, Mathf.Round(metres / 5f) * 5f), now); }
            }
            Subtitles.Tick(now);
        }

        /// <summary>What the last check of the files said (for the tests).</summary>
        public string FilesChecked { get; private set; } = "";
        public void VerifyFiles()
        {
            var game = System.IO.Path.GetDirectoryName(Application.dataPath); var cache = LifeVerse.Unity.World.WorldJoin.CacheFolder;
            Ui.Root.Then(System.Threading.Tasks.Task.Run(() => LifeVerse.Unity.World.GameFiles.Words(LifeVerse.Unity.World.GameFiles.Verify(game), LifeVerse.Unity.World.GameFiles.VerifyCache(cache))), words => { FilesChecked = words; Notices.Show(words, words.Contains("problema") ? NoticeKind.Warn : NoticeKind.Info, 8f); });
        }

        // ---- private and group conversations, as tabs of the chat ----

        /// <summary>Opens the conversation with a person (or of a group) in the chat.</summary>
        public void OpenConversation(Guid id, string name, bool group)
        {
            if (Hud.Chat.Minimized) Hud.Chat.SetMinimized(false); Hud.Chat.Open(id, group, name); Hud.Chat.Focus();
        }

        public static string ConversationPath(Guid id, bool group) => group ? "/api/v1/social/messages?groupId=" + id + "&page=1&pageSize=50" : "/api/v1/social/messages?channel=private&conversationWith=" + id + "&page=1&pageSize=50";

        private void LoadConversation(ChatTab tab) => Ui.Root.Then(_api.ApiAsync("GET", ConversationPath(tab.Id, tab.Group)), r =>
        {
            if (r.Status != 200) { Notices.Show(UiTasks.Explain(r.Status), NoticeKind.Warn); return; }
            var mine = _api.UserId;
            Hud.Chat.Fill(tab, Json.Rows(r.Body).OrderBy(x => x.Text("createdAt")).Select(x =>
            {
                var own = Guid.TryParse(x.Text("senderUserId"), out var sender) && sender == mine; var who = own ? "Você" : x.First("senderName", "senderUsername", "username");
                return (who.Length > 0 ? who : tab.Group ? "?" : tab.Name, x.Text("content"), own, DateTimeOffset.TryParse(x.Text("createdAt"), out var at) ? at : (DateTimeOffset?)null);
            }).ToList());
        });

        private void SendConversation(ChatTab tab, string text, Action<bool> done)
        {
            var body = tab.Group ? System.Text.Json.JsonSerializer.Serialize(new { content = text, groupId = tab.Id, clientMessageId = Guid.NewGuid().ToString("N") })
                : System.Text.Json.JsonSerializer.Serialize(new { content = text, recipientUserId = tab.Id, clientMessageId = Guid.NewGuid().ToString("N") });
            Ui.Root.Then(_api.ApiAsync("POST", "/api/v1/social/messages", body), r =>
            {
                var sent = r.Status >= 200 && r.Status < 300; if (!sent) Notices.Show(r.Status == 429 ? "Devagar: muitas mensagens seguidas." : UiTasks.Explain(r.Status), NoticeKind.Warn); done(sent);
            });
        }

        private void OnLiveMessage(string type, System.Text.Json.JsonElement data, string json)
        {
            if (Current != Phase.Playing || (type != "message" && type != "group_message")) return;
            var group = type == "group_message"; if (!Guid.TryParse(ApiContext.Field(data, group ? "groupId" : "from"), out var id) || (Guid.TryParse(ApiContext.Field(data, "from"), out var sender) && sender == _api.UserId)) return;   // what one wrote oneself opens nothing
            var name = ApiContext.Field(data, group ? "groupName" : "fromName"); if (name.Length == 0 && !group) name = ApiContext.Field(data, "fromUsername");
            Hud.Chat.Arrived(id, group, name);
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
            if (string.IsNullOrEmpty(LiveAddress)) Live.Begin(ApiBase, () => _api.AccessToken); else Live.BeginAt(LiveAddress, () => _api.AccessToken); CountBell(); WatchPropertyNotices();
        }

        // ---- the real estate notices: asked soon after signing in and then every five minutes (as the Godot client does), each one announced once ----
        private IVisualElementScheduledItem _noticePoll; private NoticeTeller _noticeTeller;
        private void WatchPropertyNotices()
        {
            StopPropertyNotices(); _noticeTeller = new NoticeTeller();
            _noticePoll = Ui.Root.schedule.Execute(PollPropertyNotices).Every(300000).StartingIn(8000);
        }
        private void StopPropertyNotices() { _noticePoll?.Pause(); _noticePoll = null; _noticeTeller = null; }
        public void PollPropertyNotices()
        {
            var teller = _noticeTeller; if (teller == null) return;
            Ui.Root.Then(_api.ApiAsync("GET", NoticesData.Path), r => { if (teller == _noticeTeller && r.Status == 200 && NoticesData.Parse(r.Body) is { } data && teller.Tell(data) is { } words) Notices.Show(words, NoticeKind.Info); });
        }

        /// <summary>Goes to a spot: inside this island the island moves the avatar; on another island the game travels there.</summary>
        public void MoveToSpot(Guid island, float x, float z)
        {
            if (island == _island && Client != null && Current == Phase.Playing) Client.RequestTeleport("", x, z); else Teleport(island, x, z);
        }

        /// <summary>The number on the bell: the unread notifications as the server counts them.</summary>
        public void SetBell(int unread) { Unread = Math.Max(0, unread); Hud?.SetBell(Unread); if (Bell != null) { Bell.text = Unread > 0 ? "Avisos (" + (Unread > 99 ? "99+" : Unread.ToString()) + ")" : "Avisos"; UiKit.Alert(Bell, Unread > 0); } }

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
            Hud.Register("Pessoas", "people", PeopleWindow);
            Hud.Register("Social", "social", () => new SocialWindow(Context));
            Hud.Register("Inventário", "inventory", () => new InventoryManagerWindow(Context, Store));
            Hud.Register("Carteira", "wallet", () => new WalletHubWindow(Context));
            Hud.Register("Buscar", "search", SearchWindow);
            Hud.Register("Construir", "build", () => { ToggleBuild(); return null; });
            VoiceButton = Hud.Register("Voz", "voice", () => { if (Voice == null) return null; if (Voice.Off) { Notices.Show("A voz está desligada em Configurações > Voz.", NoticeKind.Info); return null; } if (Voice.BlockedIn != null) { Notices.Show(LifeVerse.Unity.Audio.VoiceChat.BlockedWords(Voice.BlockedIn), NoticeKind.Warn); return null; } Voice.Locked = !Voice.Locked; Notices.Show(Voice.Locked ? "Microfone aberto: quem está perto ouve você." : "Microfone fechado. Segure " + KeyBindings.KeyName(KeyBindings.Of(GameAction.Talk)) + " para falar.", NoticeKind.Info); return null; });
            Bell = Hud.Register("Avisos", "notifications", () => new NotificationCenterWindow(Context, SetBell));
            Hud.Register("Mapa", "map", MapWindow);
            Hud.Register("Mais", Hud.FixedTool, () => { Hud.ToggleSheet(); return null; });
            Hud.Register("Configurações", "settings", SettingsPanel);
            Hud.Register("Ajuda", "help", () => Hud.HelpWindow(Ui)); Hud.Travel = TravelIslands;
            Hud.Register("Gestos", "gestures", () => { OpenRadial(new Vector2(Screen.width / 2f, Screen.height / 2f) / Mathf.Max(.01f, Ui.Panel.scale)); return null; }); Hud.Register("Foto", "photo", () => { TakePhoto(); return null; });
            Hud.Register("Mini-mapa", "minimap", () => { SetMinimap(!MinimapShown); return null; });
            Hud.Register("Mover", "move", MovePanel); Hud.Register("Câmera", "camera", CameraPanel);
            Hud.SearchAsked = SearchFor; Hud.AddressPressed = () => Hud.Toggle("map", MapWindow); Hud.IslandPressed = () => { if (_island != Guid.Empty) new IslandInfoWindow(Context, _island).Open(); }; Hud.ParcelPressed = () => OpenParcelHere();
            Hud.Reordered += (id, onto) => { Settings.ToolbarOrder = ToolbarLayout.Place(ToolbarLayout.Order(Hud.ToolbarDefaults, Settings.ToolbarOrder), id, onto); if (!Settings.ToolbarCustomized) { Settings.ToolbarHidden = new List<string>(Hud.DefaultHidden); Settings.ToolbarCustomized = true; } Settings.Save(SettingsPath); ApplyInterface(); };
            Hud.SetDefaultOrder("chat", "voice", "gestures", "people", "map", "build", "inventory", "photo", "move", "camera", "minimap", "search", "wallet", "notifications");
            Hud.BuyCoinsPressed = () => Hud.Toggle("wallet", () => new WalletHubWindow(Context));
            Hud.BellPressed = () => Hud.Toggle("notifications", () => new NotificationCenterWindow(Context, SetBell));
            // the choices of the sheet ("Sua varanda")
            Hud.ToolShownAsked = SetToolShown;
            Hud.EdgeAsked = edge => { Settings.ToolbarEdge = edge; Settings.Save(SettingsPath); ApplyInterface(); };
            Hud.ModeAsked = mode => { Store.Set("interface.mode", mode); ApplyInterface(); if (mode == "immersive") Notices.Show("Modo imersivo: aperte F10 para mostrar a interface.", NoticeKind.Info, 8f); };
            // the menus of the top bar, as in the Godot client
            FloatingWindow Open(FloatingWindow w) { w?.Open(); return w; }
            (string, string, Action) Line() => (Hud.MenuLine, "", null);
            Hud.AddMenu("Eu", new (string, string, Action)[]
            {
                ("Meu perfil", "user", () => Open(new ProfileHubWindow(Context, _api.UserId))), ("Avatar: visuais, corpo, roupas e animações", "avatar", () => Open(OpenAvatarStudio())), ("Meu personagem", "user", () => OpenCharacter()?.Open()),
                ("Anexos do corpo", "pin", () => Open(new AttachmentsWindow(Context, OwnAttachmentsChanged, EditAttachment, () => OwnAttachments))), Line(),
                ("Inventário", "bag", () => Hud.Toggle("inventory", () => new InventoryManagerWindow(Context, Store))), ("Carteira", "coin", () => Hud.Toggle("wallet", () => new WalletHubWindow(Context))), ("Minhas propriedades", "home", () => Open(new MyPropertiesWindow(Context))), Line(),
                ("Controles de movimento", "run", () => Hud.Toggle("move", MovePanel)), ("Controles da câmera", "camera", () => Hud.Toggle("camera", CameraPanel)),
                ("Configurações", "settings", () => Hud.Toggle("settings", SettingsPanel)), ("Barra de ferramentas", "layers", () => Open(ToolbarWindow())), Line(), ("Sair da ilha", "power", BackToLogin),
            });
            Hud.AddMenu("Comunicar", new (string, string, Action)[]
            {
                ("Conversa local", "chat", () => Hud.Chat.Focus()), ("Pessoas", "people", () => Hud.Toggle("people", PeopleWindow)),
                ("Mensagens e amigos", "chat-dots", () => Hud.Toggle("social", () => new SocialWindow(Context))), ("Grupos", "people", () => Open(new GroupsHubWindow(Context))), Line(),
                ("Avisos", "bell", () => Hud.Toggle("notifications", () => new NotificationCenterWindow(Context, SetBell))), ("Gestos", "hand", () => OpenRadial(new Vector2(Screen.width / 2f, Screen.height / 2f) / Mathf.Max(.01f, Ui.Panel.scale))),
            });
            Hud.AddMenu("Mundo", new (string, string, Action)[]
            {
                ("Mapa do mundo", "globe", () => Hud.Toggle("map", MapWindow)), ("Ir para a Ilha de Boas-vindas", "home", GoToWelcome), ("Hora e clima só para você", "sun", () => Open(new LocalSkyWindow(Ui, () => Client != null ? Client.Environment : null))), ("Mini-mapa", "map", () => SetMinimap(!MinimapShown)), ("Buscar no LifeVerse", "search", () => Hud.Toggle("search", SearchWindow)),
                ("Eventos, marcos e fotos", "calendar", () => Open(new DiscoveryWindow(Context, this, () => Player != null ? Player.GamePosition : Vector3.zero, () => Player != null ? Player.HeadingDegrees * Mathf.Deg2Rad : 0f))), Line(),
                ("Sobre a ilha", "info", () => Open(new IslandInfoWindow(Context, _island))), ("Gerenciar esta ilha", "settings", () => Open(new IslandManagementWindow(Context, _island))), ("Gerenciar terrenos", "grid", () => Open(new LandManagementWindow(Context, _island, Here))),
                ("Sobre este terreno", "pin", () => OpenParcelHere()), ("Mercado imobiliário", "home", () => Open(new RealEstateMarketWindow(Context))), ("Editar terreno   T", "mountain", ToggleTerrain), Line(),
                ("Tirar foto   F12", "photo", TakePhoto), ("Ilha anterior", "chevron-left", () => TravelIslands(-1)), ("Próxima ilha", "chevron-right", () => TravelIslands(1)),
            });
            Hud.AddMenu("Conteúdo", new (string, string, Action)[]
            {
                ("Construir   B", "hammer", ToggleBuild), ("Mercado", "bag", () => Open(new MarketWindow(Context))), ("Ofertas de itens", "download", () => Open(new OffersWindow(Context))), ("Script de um objeto", "code", () => ScriptOpener()?.Open()),
            });
            Hud.AddMenu("Ajuda", new (string, string, Action)[] { ("Primeiros passos", "help", () => OpenWelcome()), ("Atalhos e controles", "help", () => Hud.Toggle("help", () => Hud.HelpWindow(Ui))), ("Manual dos scripts", "doc", () => Open(new ScriptDocsWindow(Context, _ => { }))) });
            Hud.AddMenu("Desenvolver", new (string, string, Action)[]
            {
                ("Desempenho   F3", "chart", () => Hud.TogglePerformance()), ("Exportar diagnóstico", "download", () => { var path = SettingsEffects.WriteDiagnostics(Store, this); Notices.Show("Diagnóstico salvo em " + path, NoticeKind.Good); }),
            });
            Hud.Presence.RegisterValueChangedCallback(e => { if (Current == Phase.Playing) Api.ApiAsync("PUT", "/api/v1/social/presence", System.Text.Json.JsonSerializer.Serialize(new { status = e.newValue, privacyJson = (string)null })); });
        }

        /// <summary>After a change of what is worn: asks the platform for the avatar again and shows it on the own body.</summary>
        /// <summary>Reads the person's avatar again and redraws them (after a look, a body, a garment or an animation changed in the Avatar window), and tells the others on the island.</summary>
        public void RefreshOwnAvatar() => Ui.Root.Then(Api.ApiAsync("GET", "/api/v1/users/me/avatar"), r => { if (r.Status == 200 && Client != null) { PeopleViews?.ApplyAppearance(Client.SelfPeerId, r.Body); TellIslandOfAppearance(); } });

        private void TellIslandOfAppearance() { if (Client != null && Client.Authenticated) Client.RefreshAppearance(); }

        /// <summary>The person put on, took off or moved a worn object (the platform has accepted it): it shows on their screen and body, and the others on the island see it.</summary>
        public void OwnAttachmentsChanged() { RefreshOwnAttachments(); TellIslandOfAppearance(); }

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
                var window = new CharacterWindow(Context, draft); window.Saved += () => { var view = PeopleViews?.ViewOf(Client.SelfPeerId); view?.ApplyAppearance(AvatarAppearance.Parse(draft.ToAppearanceJson())); TellIslandOfAppearance(); }; window.Open();
            });
            return holder;
        }

        /// <summary>Finds the person with a name that is nearby (the world only knows names) among the platform's people and opens what can be done to them.</summary>
        /// <summary>"Pagar…" on a person (the right button): the wallet opens with that person as who receives (found by the name the avatar shows).</summary>
        public void PayPerson(string displayName)
        {
            Ui.ScreenLayer.Then(Api.ApiAsync("GET", "/api/v1/social/people?q=" + Uri.EscapeDataString(displayName)), r =>
            {
                var match = r.Status == 200 ? Json.Rows(r.Body).FirstOrDefault(p => string.Equals(p.First("displayName", "username"), displayName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Text("username"), displayName, StringComparison.OrdinalIgnoreCase)) : default;
                if (r.Status != 200 || match.Id() == Guid.Empty) { Notices.Show("Não achei " + displayName + " na plataforma.", NoticeKind.Warn); return; }
                var wallet = new WalletWindow(Context); wallet.Open(); wallet.PayTo(match.Id(), displayName);
            });
        }

        private void OpenPersonActions(string displayName)
        {
            Ui.ScreenLayer.Then(Api.ApiAsync("GET", "/api/v1/social/people?q=" + Uri.EscapeDataString(displayName)), r =>
            {
                var match = r.Status == 200 ? Json.Rows(r.Body).FirstOrDefault(p => string.Equals(p.First("displayName", "username"), displayName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Text("username"), displayName, StringComparison.OrdinalIgnoreCase)) : default;
                if (r.Status != 200 || match.Id() == Guid.Empty) { Notices.Show("Não achei " + displayName + " na plataforma.", NoticeKind.Warn); return; }
                new PersonActionsWindow(Context, match.Id(), displayName, id => new ProfileHubWindow(Context, id).Open(), (id, name) => { if (Context.OpenConversation != null) { Context.OpenConversation(id, name, false); return; } var social = new SocialWindow(Context); social.Open(); social.Talk(id, name); }).Open();
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
                if (parcel == Guid.Empty) { if (r.Status == 200) OpenGroundInfo(at.Value); else Notices.Show(UiTasks.Explain(r.Status, r.Body), NoticeKind.Info); return; }
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
            AttachmentEditing?.Close(); AttachmentEditing = new AttachmentEditor(Context, a, () => OwnAttachments, () => Follow != null ? Follow.Camera : null, () => Follow, OwnAttachmentsChanged); return AttachmentEditing;
        }

        /// <summary>"Indicadores de fala": who is talking shows it over the head.</summary>
        public bool SpeakingIndicators = true; private string _openMicApplied;
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

        /// <summary>Opens or closes the mini-map; "Mini-mapa aberto" in Configurações follows it (and the other way round).</summary>
        public void SetMinimap(bool on)
        {
            MinimapShown = on; Hud.Map.style.visibility = on ? Visibility.Visible : Visibility.Hidden; Hud.BottomBar.Q<Button>("btn-minimap")?.EnableInClassList("lv-btn-open", on);
            if (Store != null && Store.Bool("world.minimap_open") != on) Store.Set("world.minimap_open", on ? "true" : "false", false);
        }
        /// <summary>Where "Lembrar senha" keeps the sealed login (the checks point it elsewhere).</summary>
        public string SavedLoginPath { get => _savedLoginPath ??= SavedLogin.DefaultPath; set => _savedLoginPath = value; }   // asked when first needed: Unity refuses the data folder in a field initializer
        private string _savedLoginPath;
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

        private volatile bool _sessionGone;
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
            NameIsland(); Show(Phase.Playing); Notices.Show("Você entrou na ilha.", NoticeKind.Good);
            if (ShowWelcome && !Settings.WelcomeDone && Welcome == null) OpenWelcome();
            Reconnects = 0; if (Settings.IslandYaw.TryGetValue(_island.ToString("N"), out var yaw) && Follow != null) Follow.Yaw = yaw; Islands.Visit(_island); Hud.SetTravel(Islands.CanBack, Islands.CanForward); ApplyInterface(); SetMinimap(Store.Bool("world.minimap_open"));
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
            Plates = new NamePlates(Ui); Follow.PickPoint = PickPoint; Follow.PickFocus = PickFocus; Follow.OverInterface = point => UiKit.IsOver(Ui.Root, point); Follow.DoubleClicked = ray => WalkToGround(ray);
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
            _openMicApplied = null; Voice = new GameObject("Voice").AddComponent<VoiceChat>(); Voice.transform.SetParent(_world.transform); Voice.Attach(Client); Hearing = new GameObject("Hearing").AddComponent<VoiceListener>(); Hearing.transform.SetParent(_world.transform); Hearing.Attach(Client);
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
        public Button VoiceButton { get; private set; }

        /// <summary>The parcel under the person may switch voice off: the microphone is then kept closed, the button says so and the person is told once.</summary>
        public void ApplyLandVoice()
        {
            var off = ParcelMusic != null ? ParcelMusic.VoiceOffIn : null; if (Voice.BlockedIn == off) return;
            Voice.BlockedIn = off; if (VoiceButton != null) { VoiceButton.text = off != null ? "Sem voz" : "Voz"; VoiceButton.tooltip = off != null ? LifeVerse.Unity.Audio.VoiceChat.BlockedWords(off) : "Voz"; }
            if (off != null && !Voice.Off) Notices.Show(LifeVerse.Unity.Audio.VoiceChat.BlockedWords(off), NoticeKind.Info);
        }
        public BusSource LoginTheme { get; private set; }
        private MicTest _micTest;
        private float _nextSplash, _footprintClock = 2f;

        /// <summary>Puts the volumes and the voice settings on the sound system (called at the start and whenever the settings change).</summary>
        public void ApplyAudio()
        {
            KeyBindings.Load(Settings.Keys); KeyBindings.Blocked = () => Hud != null && Hud.Chat != null && Hud.Chat.Typing || Ui != null && Ui.Root != null && Ui.Root.panel != null && Ui.Root.panel.focusController != null && Ui.Root.panel.focusController.focusedElement is TextField;
            SettingsEffects.Resources(Store); MicDevices.Preferred = Settings.InputDevice; UiSounds.Enabled = Settings.UiSounds; Buses.SetAll(Settings.MasterVolume, Settings.AmbientVolume, Settings.EffectsVolume, Settings.MusicVolume, Settings.MediaVolume, Settings.VoiceVolume);
            if (Voice != null) { Voice.Gate = Settings.VoiceSensitivity; Voice.EchoGuard = Settings.EchoGuard; }
            SettingsEffects.Voice(Store, Voice, Hearing, ref _openMicApplied); SpeakingIndicators = Store == null || Store.Bool("voice.indicators");
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
            if (_sessionGone) { _sessionGone = false; OnSessionExpired(); }
            TickLoginMusic(); SettingsEffects.RenderScale.Tick(Time.unscaledDeltaTime); TickAppearance();
            if (Current == Phase.Playing && Store != null) TickAutoQuality();
            if (Current != Phase.Playing || Client == null) return;
            var views = PeopleViews; Hud.SetConnection(Client.Connected, Mathf.Max(0, (views?.Count ?? 1) - 1));
            SampleNetwork(); if (Hud.PerformanceVisible) Hud.Perf.Frame(Time.unscaledDeltaTime);
            _mapClock += Time.unscaledDeltaTime; TickCaptions();
            if (Hud.Map.Rotate && Follow != null) Hud.Map.Turn(Follow.Yaw);
            if (_mapClock > .25f && Player != null && Client.Terrain != null)
            {
                _mapClock = 0f; Hud.Map.Paint(Client.Terrain.Map);
                var others = new List<Vector3>(); var names = new List<string>(); if (views != null) foreach (var peer in views.Peers) if (peer != Client.SelfPeerId) { var view = views.ViewOf(peer); others.Add(view.GamePosition); names.Add(view.DisplayName); }
                _footprintClock += .25f; if (_footprintClock >= 2f && Client.Objects != null) { _footprintClock = 0f; if (Hud.Map.ShowBuildings) Client.Objects.Footprints(Hud.Map.Buildings); else Hud.Map.Buildings.Clear(); }
                Hud.Map.Place(Player.GamePosition, others, names); Hud.SetPeople(views?.ViewOf(Client.SelfPeerId)?.DisplayName, names); Hud.SetPosition(Player.GamePosition); VoiceButton?.EnableInClassList("lv-btn-open", Voice != null && Voice.Locked);
            }
            _recentClock += Time.unscaledDeltaTime; if (_recentClock > 2f) { _recentClock = 0f; NoteRecent(); }
            Builder?.Tick(Time.unscaledDeltaTime); TerrainTools?.Tick(Time.unscaledDeltaTime);
            if (Ambient != null && Client.Terrain != null && Client.Terrain.Map != null && Player != null)
            {
                var spot = Player.GamePosition; var edge = Client.Terrain.Map.HalfExtent - Mathf.Max(Mathf.Abs(spot.x), Mathf.Abs(spot.z)); var weather = Client.Environment != null ? Client.Environment.WeatherShown : "clear";
                Ambient.SetTargets(AmbientLevels.Compute(edge, Player.State.Swimming, spot.y, weather));
                if (Player.State.Swimming && new Vector2(Player.State.VelocityX, Player.State.VelocityZ).magnitude > .5f && Time.unscaledTime > _nextSplash) { _nextSplash = Time.unscaledTime + 1.05f; Ambient.Splash(); }
            }
            if (Voice != null) { Voice.TalkHeld = KeyBindings.Held(GameAction.Talk); ApplyLandVoice(); }
            if (Hearing != null && PeopleViews != null) foreach (var peer in PeopleViews.Peers) { var view = PeopleViews.ViewOf(peer); if (view != null) view.SetSpeaking(SpeakingIndicators && (peer == Client.SelfPeerId ? Voice != null && Voice.Talking : Hearing.IsSpeaking(peer))); }
            if (Mouse.current is { } pointer && (pointer.leftButton.wasPressedThisFrame || pointer.rightButton.wasPressedThisFrame) && !UiKit.IsOver(Ui.Root, pointer.position.ReadValue())) GameKeyboard.ReleaseFocus(Ui.Root);   // a click on the world takes the keyboard back from a field
            if (Keyboard.current is { } keyboard && !Hud.Chat.Typing)
            {
                if (KeyBindings.Pressed(GameAction.Chat) && Time.frameCount > Hud.Chat.ReleasedFrame + 1) Hud.Chat.Focus();
                if (KeyBindings.Pressed(GameAction.Performance)) Hud.TogglePerformance();
                if (Follow != null && Follow.ZoomTarget.HasValue && (keyboard.escapeKey.wasPressedThisFrame || KeyBindings.Pressed(GameAction.Forward) || KeyBindings.Pressed(GameAction.Back) || KeyBindings.Pressed(GameAction.Left) || KeyBindings.Pressed(GameAction.Right) || KeyBindings.Pressed(GameAction.Jump))) Follow.ZoomTarget = null;
                if (KeyBindings.Pressed(GameAction.CameraReset)) ResetCamera();
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

        /// <summary>A double click on the ground walks there. Not while building or shaping the land, not through the person's own eyes, and not when a person or an object is in front.</summary>
        public bool WalkToGround(Ray ray)
        {
            if (Current != Phase.Playing || Player == null || Client?.Terrain == null || Follow == null || Follow.FirstPerson) return false;
            if ((Builder != null && Builder.Active) || (TerrainTools != null && TerrainTools.Active) || Hud.Radial.IsOpen) return false;
            if (PickFocus(ray) != null || !(Client.Terrain.Raycast(ray) is { } ground)) return false;
            if (!Player.WalkTo(LifeVerse.Unity.World.GameSpace.ToGame(ground))) return false;
            ObjectMenu?.Close(); Follow.ZoomTarget = null; return true;   // walking away: the camera comes back to the person
        }

        /// <summary>R: the camera goes back behind the person, at the usual height and distance.</summary>
        public void ResetCamera() { if (Follow != null && Player != null && !Follow.Mirror) Follow.ResetBehind(Player.HeadingDegrees, Store.Float("camera.default_distance")); }

        /// <summary>What a double click focuses on: the nearest person or object under the ray (the ground is not a target: a double click there still walks).</summary>
        public Vector3? PickFocus(Ray ray)
        {
            Vector3? best = null; void Keep(Vector3 point) { if (best == null || Vector3.Distance(ray.origin, point) < Vector3.Distance(ray.origin, best.Value)) best = point; }
            var person = AvatarPick.Pick(ray, People()); var view = person >= 0 ? PeopleViews?.ViewOf(person) : null; if (view != null) Keep(LifeVerse.Unity.World.GameSpace.ToUnity(view.GamePosition) + Vector3.up * 1.4f);
            if (Client?.Objects != null && Client.Objects.Pick(ray, 80f, out _, out var box)) Keep(box.center);
            if (best is { } b && Client?.Terrain != null && Client.Terrain.Raycast(ray) is { } ground && Vector3.Distance(ray.origin, ground) + .5f < Vector3.Distance(ray.origin, b)) return null;   // the ground is in front
            return best;
        }

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
                    items.Add(new RadialItem("Perfil e ações", () => OpenPersonActions(name), name.Length > 0, name.Length > 0 ? "Ver o perfil, adicionar como amigo, mandar mensagem e mais." : "O nome desta pessoa ainda não chegou."));
                    items.Add(new RadialItem("Dar zoom", () => { if (Follow == null) return; Follow.ZoomTarget = head; Follow.ZoomDistance = 2.5f; }));
                    items.Add(new RadialItem("Pagar…", () => PayPerson(name), name.Length > 0, name.Length > 0 ? "Mandar moedas para " + name + " pela carteira (você confirma o valor)." : "O nome desta pessoa ainda não chegou."));
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
                items.Add(new RadialItem("Ir até aqui", () => Player?.WalkTo(spot), true, "Anda até este ponto."));
                items.Add(new RadialItem("Teleportar para cá", () => Client?.RequestTeleport("", spot.x, spot.z)));
                items.Add(new RadialItem("Editar terreno", () => { if (TerrainTools != null && !TerrainTools.Active) ToggleTerrain(); }, true, "Abre as ferramentas de terreno: elevar, abaixar, nivelar, suavizar e pintar."));
                items.Add(new RadialItem("Sobre este terreno", () => OpenParcelAt(spot), true, "De quem é este chão, o tamanho e o que se pode fazer nele."));
                items.Add(new RadialItem("Gerenciar terrenos", () => new LandManagementWindow(Context, _island, () => spot).Open(), true, "Demarcar, vender, alugar e dar permissões."));
                items.Add(new RadialItem("Construir aqui", ToggleBuild)); items.Add(new RadialItem("Olhar para cá", () => Follow?.FocusAt(ray)));
                items.Add(new RadialItem("Copiar local", () => { GUIUtility.systemCopyBuffer = PlaceText(spot); Notices.Show("Localização copiada: " + PlaceText(spot), NoticeKind.Good); }));
            }
            return items;
        }

        private Vector2 UiPoint(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y) / Ui.Panel.scale;

        /// <summary>The menu of the right button for a point of the screen (nothing when there is nothing there).</summary>
        public bool OpenContextMenu(Vector2 screen) { var items = ContextItems(screen); if (items.Count == 0) return false; Hud.Radial.Open(Ui, items, UiPoint(screen)); return true; }

        private FloatingWindow OpenAvatarStudio() => new AvatarStudioWindow(Context, () => OpenCharacter()?.Open(), on => { if (Follow != null) Follow.Mirror = on; }, RefreshOwnAvatar);

        /// <summary>The interface mode (full, compact, immersive) and the toolbar (order, hidden buttons, small) from the settings.</summary>
        public void ApplyInterface() { Hud.SetToolbarEdge(Settings.ToolbarEdge); Hud.ApplyLayout(Store.Get("interface.mode"), Store.Bool("interface.compact_toolbar"), Settings.ToolbarOrder, Settings.ToolbarCustomized ? Settings.ToolbarHidden : (IReadOnlyCollection<string>)Hud.DefaultHidden); }

        /// <summary>To the welcome island: the platform says which it is.</summary>
        public void GoToWelcome()
        {
            if (Current != Phase.Playing) return;
            Ui.Root.Then(_api.ApiAsync("GET", "/api/v1/worlds/welcome"), answer =>
            {
                var island = Guid.Empty;
                if (answer.Status == 200)
                    try { using var json = System.Text.Json.JsonDocument.Parse(answer.Body); island = SectionWindow.G(json.RootElement, "islandId"); }
                    catch (System.Text.Json.JsonException) { }
                if (island == Guid.Empty) { Notices.Show("Não foi possível achar a Ilha de Boas-vindas agora.", NoticeKind.Bad); return; }
                if (island == _island) { Notices.Show("Você já está nesta ilha.", NoticeKind.Info); return; }
                Notices.Show("Indo para a Ilha de Boas-vindas…", NoticeKind.Info); Teleport(island);
            });
        }

        /// <summary>Back (-1) or forward (+1) among the islands visited in this session.</summary>
        public void TravelIslands(int step)
        {
            var island = step < 0 ? Islands.PeekBack : Islands.PeekForward; if (island == Guid.Empty || Current != Phase.Playing) return;
            Notices.Show(step < 0 ? "Voltando à ilha anterior…" : "Indo para a próxima ilha…", NoticeKind.Info); Teleport(island);
        }

        /// <summary>Shows or hides a button of the bar for good (the bar keeps at least one).</summary>
        public void SetToolShown(string id, bool shown)
        {
            if (id == Hud.FixedTool) return;
            if (!Settings.ToolbarCustomized) { Settings.ToolbarHidden = new List<string>(Hud.DefaultHidden); Settings.ToolbarCustomized = true; }
            if (!shown && !ToolbarLayout.CanHide(ToolbarLayout.Order(Hud.ToolbarDefaults, Settings.ToolbarOrder).Where(x => x != Hud.FixedTool).ToList(), Settings.ToolbarHidden, id)) { Notices.Show("A barra precisa de pelo menos um botão.", NoticeKind.Warn); return; }
            if (shown) Settings.ToolbarHidden.Remove(id); else if (!Settings.ToolbarHidden.Contains(id)) Settings.ToolbarHidden.Add(id);
            Settings.Save(SettingsPath); ApplyInterface();
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
                    if (id == Hud.FixedTool) continue;   // "Mais" is always on the bar
                    var key = id; var label = Hud.BottomBar.Q<Button>("btn-" + id)?.text ?? id; var hidden = Settings.ToolbarHidden.Contains(id);
                    var toggle = new Toggle { value = !hidden }; toggle.RegisterValueChangedCallback(e => { if (!e.newValue && !ToolbarLayout.CanHide(order, Settings.ToolbarHidden, key)) { toggle.SetValueWithoutNotify(true); Notices.Show("A barra precisa de pelo menos um botão.", NoticeKind.Warn); return; } if (e.newValue) Settings.ToolbarHidden.Remove(key); else if (!Settings.ToolbarHidden.Contains(key)) Settings.ToolbarHidden.Add(key); Settings.Save(SettingsPath); ApplyInterface(); });
                    list.Add(UiKit.Row(toggle, UiKit.Text(label, "lv-body"), UiKit.Spacer(), UiKit.Button("◀", () => { Settings.ToolbarOrder = ToolbarLayout.Move(order, key, -1); Settings.Save(SettingsPath); ApplyInterface(); Fill(); }), UiKit.Button("▶", () => { Settings.ToolbarOrder = ToolbarLayout.Move(order, key, 1); Settings.Save(SettingsPath); ApplyInterface(); Fill(); })));
                }
            }
            Fill();
            var edges = new List<string> { "Embaixo", "À esquerda", "À direita" }; var values = new[] { "bottom", "left", "right" };
            var where = new DropdownField("Onde fica a barra", edges, Mathf.Max(0, Array.IndexOf(values, Settings.ToolbarEdge))) { name = "toolbar-edge" };
            where.RegisterValueChangedCallback(_ => { Settings.ToolbarEdge = values[Mathf.Clamp(where.index, 0, 2)]; Settings.Save(SettingsPath); ApplyInterface(); }); window.Body.Add(where);
            window.Body.Add(UiKit.Button("Restaurar a barra", () => { RestoreToolbar(); where.SetValueWithoutNotify(edges[0]); Fill(); })); return window;
        }

        public void RestoreToolbar() { Settings.ToolbarEdge = "bottom"; Settings.ToolbarOrder.Clear(); Settings.ToolbarHidden.Clear(); Settings.ToolbarCustomized = false; Store.Set("interface.compact_toolbar", "false"); Settings.Save(SettingsPath); ApplyInterface(); }

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
                if (parcel == Guid.Empty) { if (r.Status == 200) OpenGroundInfo(game); else Notices.Show(UiTasks.Explain(r.Status, r.Body), NoticeKind.Info); return; }
                LastParcelInfo = new ParcelInfoWindow(Context, _island, parcel, Here); LastParcelInfo.Open();
            });
        }

        private static string PlaceText(Vector3 game) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.#}, {1:0.#}, {2:0.#}", game.x, game.y, game.z);

        /// <summary>The window about ground that is in no parcel (for the checks).</summary>
        public FloatingWindow LastGroundInfo { get; private set; }

        /// <summary>"Sobre este terreno" where no parcel was marked out: whose ground it is and what can be done there.</summary>
        public FloatingWindow OpenGroundInfo(Vector3 game)
        {
            LastGroundInfo?.Close();
            var window = new FloatingWindow(Ui, "ground-info", "Sobre este terreno", 380f, -1f, new Vector2(430, 120)); LastGroundInfo = window; window.SetIcon("pin");
            void Line(string text, params string[] classes) { var label = UiKit.Text(text, classes); label.style.whiteSpace = WhiteSpace.Normal; window.Body.Add(label); }
            Line("Aqui não há um terreno demarcado (parcela): este chão é da ilha.", "lv-bold");
            Line("Ponto: " + PlaceText(game), "lv-small", "lv-muted");
            Line(Client != null && Client.CanEditTerrain ? "Você administra o relevo desta ilha: pode editar o chão e demarcar terrenos." : "Só quem administra a ilha pode mudar o relevo aqui e demarcar terrenos.", "lv-body");
            window.Body.Add(UiKit.Button("Editar o terreno", () => { window.Close(); if (TerrainTools != null && !TerrainTools.Active) ToggleTerrain(); }, "lv-btn-primary"));
            window.Body.Add(UiKit.Button("Gerenciar terrenos (demarcar)", () => { window.Close(); new LandManagementWindow(Context, _island, () => game).Open(); }));
            window.Body.Add(UiKit.Button("Sobre a ilha", () => { window.Close(); new IslandInfoWindow(Context, _island).Open(); }));
            window.Open(); return window;
        }

        /// <summary>The values of the settings catalog (the Configurações window), over <see cref="Settings"/>.</summary>
        public SettingsStore Store { get; private set; }

        /// <summary>After a change in Configurações: the engine, the interface, the file, the world, and the account when the sync is on.</summary>
        public void SettingsApplied()
        {
            Settings.Clamped(); GraphicsQuality.Apply(Settings); Ui.ApplySettings(); Settings.Save(SettingsPath); ApplySettingsToWorld();
            if (Sync != null && Sync.Enabled) _ = Sync.PushAppliedAsync();
        }

        /// <summary>"Ping, jitter e perda de pacotes": the connection to the island, sampled once a second while the setting is on.</summary>
        public LifeVerse.Unity.World.NetworkQuality Network { get; } = new LifeVerse.Unity.World.NetworkQuality();
        private float _nextPing;
        private void SampleNetwork()
        {
            if (Time.unscaledTime < _nextPing) return; _nextPing = Time.unscaledTime + 1f;
            if (Client == null || !Client.Connected) Network.Clear(); else if (Store != null && Store.Bool("network.ping")) Network.Add(Client.RoundTripMilliseconds, Client.PacketLossPercent);
        }

        private void OnSettingChanged(string id)
        {
            if (id.StartsWith("metrics.", StringComparison.Ordinal) || id == "network.ping") SettingsEffects.Metrics(Store, Hud);
            else if (id == "audio.interface" || id == "perf.object_lod" || id == "network.download_limit") SettingsEffects.Resources(Store);
            else if (id == "advanced.verify_files") { if (Store.Bool(id)) VerifyFiles(); }
            else if (id == "accessibility.captions") { if (!Store.Bool(id)) Subtitles?.Clear(); }
            else if (id == "perf.resource_cache") { SettingsEffects.Resources(Store); LifeVerse.Unity.World.AssetPreloader.Prune(LifeVerse.Unity.World.WorldJoin.CacheFolder, LifeVerse.Unity.World.AssetPreloader.LimitBytes); }
            else if (id == "camera.default_distance" && Follow != null) Follow.Distance = Store.Float(id);
            else if (id == "camera.default_mode" && Follow != null) Follow.SetFirstPerson(Store.Get(id) == "first");
            else if (id == "world.minimap_open" && Store.Bool(id) != MinimapShown) SetMinimap(Store.Bool(id));
            else if (id == "general.keep_on_top") SettingsEffects.KeepOnTop(Store.Bool(id));
        }

        /// <summary>The buttons of Configurações that do something once.</summary>
        public Dictionary<string, Action> SettingActions()
        {
            var all = OwnActions(); foreach (var page in SettingsCatalog.AccountPages) { var section = page.Value; all[page.Key] = () => OpenAccountPrivacy(section); }
            return all;
        }

        /// <summary>"Privacidade e sessões" on the page asked for (it needs a signed-in account).</summary>
        public AccountPrivacyWindow OpenAccountPrivacy(string section)
        {
            if (Context?.Api == null || Context.Api.UserId == Guid.Empty) { Notices.Show("Entre na conta para ver isto.", NoticeKind.Info); return null; }
            var window = new AccountPrivacyWindow(Context, section); window.Open(); return window;
        }

        private Dictionary<string, Action> OwnActions() => new Dictionary<string, Action>
        {
            ["account.leave_island"] = () => { if (Current == Phase.Playing) BackToLogin(); },
            ["account.quit"] = Application.Quit,
            ["general.download_update"] = () => { var release = UpdateInfo?.Release; if (release != null && UpdateInfo.Status != LifeVerse.Shared.Updates.UpdateStatus.UpToDate && UpdateInfo.CanDownload) Application.OpenURL(release.DownloadUrl); else Notices.Show(UpdateInfo == null ? "Ainda não foi possível saber se há versão nova." : "Você já tem a versão mais recente.", NoticeKind.Info); },
            ["avatar.open_mirror"] = () => { if (Current == Phase.Playing) OpenAvatarStudio().Open(); else Notices.Show("O espelho abre dentro de uma ilha.", NoticeKind.Info); },
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
            SettingsEffects.World(Store, PeopleViews, _world != null ? _world.GetComponentInChildren<LifeVerse.Unity.World.OceanView>() : null, Client != null ? Client.Objects : null);
            SettingsEffects.Toasts(Store, Notices); SettingsEffects.Build(Store, Builder); LifeVerse.Unity.Avatar.AvatarSounds.FootstepsEnabled = Store.Bool("avatar.footsteps");
            if (Store.Bool("metrics.fps") || Store.Bool("metrics.frame_time") || Store.Bool("metrics.memory") || Store.Bool("network.ping")) SettingsEffects.Metrics(Store, Hud);
        }

        /// <summary>"Qualidade automática": lowers the preset one level when the frames stay well below the target (see <see cref="SettingsEffects.AutoQuality"/>).</summary>
        private void TickAutoQuality()
        {
            var preset = Store.Get("graphics.preset"); if (preset == "custom") return;
            if (preset == "auto") preset = SettingsStore.PresetKeyOf(SettingsStore.PresetIndexOf("auto"));
            var lower = SettingsEffects.AutoQuality.Tick(Time.unscaledDeltaTime, 1f / Mathf.Max(.0001f, Time.unscaledDeltaTime), SettingsEffects.AutoQuality.TargetFps(Settings), preset);
            if (lower == null) return;
            Store.Set("graphics.preset", lower, false); SettingsApplied();
            Notices.Show("O jogo estava abaixo da meta de quadros: a qualidade gráfica passou para " + (SettingsCatalog.Find("graphics.preset")?.Choices.FirstOrDefault(c => c.Id == lower).Label ?? lower) + ".", NoticeKind.Info, 8f);
        }

        /// <summary>The paper of the interface, light or dark ("Aparência da interface"): automatic follows the sky on screen, with a margin so dusk does not make it flicker.</summary>
        public void TickAppearance()
        {
            if (Store == null || Ui == null) return;
            var chosen = Store.Get("interface.appearance"); var sky = Current == Phase.Playing && Client != null ? Client.Environment : null;
            Ui.SetNight(chosen == "night" || chosen != "day" && sky != null && NightFor(sky.DayFactor, Ui.Night));
        }

        /// <summary>Whether the sky is dark enough for the dark paper (0 is night, 1 day): it takes less light to leave the night than to enter it.</summary>
        public static bool NightFor(float day, bool night) => day < (night ? .55f : .35f);

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
            Live?.End(); SetBell(0); StopPropertyNotices();
            if (Client != null) { Client.DisconnectedBy -= OnDisconnected; Client.ChatReceived -= OnChat; Client.PlayerSpawned -= OnSpawned; Client.PlayerDespawned -= OnDespawned; }
            if (_entering != null) { StopCoroutine(_entering); _entering = null; }
            TearDownWorld(); Show(Phase.Login); Login.SetBusy(false);
        }

        private void OnDestroy() { _api?.Dispose(); }
    }
}
