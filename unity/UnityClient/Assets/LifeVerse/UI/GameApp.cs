using System;
using System.Collections;
using System.Collections.Generic;
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
            ApiBase = apiBase; Settings = settings ?? UserSettings.Load(SettingsPath); GraphicsQuality.Apply(Settings);
            Ui = UiRoot.Create(Settings); Notices = new NotificationCenter(Ui); Hud = new Hud(Ui, Notices);
            Login = new LoginView(Settings, "Versão " + Application.version + "  •  Unity"); Login.SignIn += OnSignIn; Login.Register += OnRegister; Ui.ScreenLayer.Add(Login);
            Loading = new LoadingView(); Loading.Back.clicked += BackToLogin;
            _api = new IslandEntryClient(apiBase); _api.SessionExpired += OnSessionExpired;
            Context = new ApiContext { Ui = Ui, Api = _api, Notices = Notices, CurrentIsland = () => _island, EnterIsland = SwitchIsland };
            RegisterWindows(); Hud.Chat.Send = text => Client != null && Client.SendChat(text);
            Hud.Chat.TypingChanged += typing => { if (Player != null) Player.Typing = typing; };
            Hud.Perf.Extra = () => Client == null ? "" : "pessoas " + (PeopleViews?.Count ?? 0) + "   objetos " + Client.Objects.Count + " (" + Client.Objects.CulledCount + " fora do alcance)   mensagens do mundo " + Client.Snapshots;
            ApplyAudio(); StartCoroutine(StartSounds()); StartCoroutine(LoadIslands());
        }

        private void RegisterWindows()
        {
            Hud.Register("Chat", "chat", () => { Hud.Chat.Focus(); return null; });
            Hud.Register("Pessoas", "people", () => Hud.PeopleWindow(Ui, PeopleViews, Client.SelfPeerId, () => Player.GamePosition, OpenPersonActions));
            Hud.Register("Social", "social", () => new SocialWindow(Context));
            Hud.Register("Inventário", "inventory", () => new InventoryWindow(Context));
            Hud.Register("Carteira", "wallet", () => new WalletWindow(Context));
            Hud.Register("Buscar", "search", () => new SearchWindow(Context));
            Hud.Register("Construir", "build", () => { ToggleBuild(); return null; });
            Hud.Register("Voz", "voice", () => { if (Voice == null) return null; Voice.Locked = !Voice.Locked; Notices.Show(Voice.Locked ? "Microfone aberto: quem está perto ouve você." : "Microfone fechado. Segure " + KeyBindings.KeyName(KeyBindings.Of(GameAction.Talk)) + " para falar.", NoticeKind.Info); return null; });
            Hud.Register("Mais", "more", MoreWindow);
            Hud.Register("Configurações", "settings", () => { var window = new SettingsWindow(Ui, SettingsPath) { Mic = _micTest ??= gameObject.AddComponent<MicTest>() }; window.Changed += ApplySettingsToWorld; window.Closed += () => _micTest?.End(); return window; });
            Hud.Register("Ajuda", "help", () => Hud.HelpWindow(Ui));
            Hud.Presence.RegisterValueChangedCallback(e => { if (Current == Phase.Playing) Api.ApiAsync("PUT", "/api/v1/social/presence", System.Text.Json.JsonSerializer.Serialize(new { status = e.newValue, privacyJson = (string)null })); });
        }

        /// <summary>The windows that do not fit in the bar: profile, groups, market, events and photos, the island and its management, item offers, scripts and the character.</summary>
        private FloatingWindow MoreWindow()
        {
            var window = new FloatingWindow(Ui, "more", "Mais", 300f, -1f, new Vector2(520, 380));
            void Add(string label, Func<FloatingWindow> create) { var button = UiKit.Button(label, () => { window.Close(); create()?.Open(); }); button.style.marginBottom = 3; window.Body.Add(button); }
            Add("Perfil", () => new ProfileWindow(Context)); Add("Grupos", () => new GroupsWindow(Context)); Add("Mercado", () => new MarketWindow(Context));
            Add("Eventos, marcos e fotos", () => new DiscoveryWindow(Context, this, () => Player != null ? Player.GamePosition : Vector3.zero, () => Player != null ? Player.HeadingDegrees * Mathf.Deg2Rad : 0f));
            Add("Ofertas de itens", () => new OffersWindow(Context)); Add("Sobre a ilha", () => new IslandInfoWindow(Context, _island)); Add("Gestão da ilha", () => new IslandManageWindow(Context, _island));
            Add("Script de um objeto", ScriptOpener); Add("Meu personagem", OpenCharacter); Add("Anexos do corpo", () => new AttachmentsWindow(Context, RefreshOwnAttachments));
            return window;
        }

        /// <summary>After a change of what is worn: asks the platform for the avatar again and shows it on the own body.</summary>
        public void RefreshOwnAttachments() => Ui.Root.Then(Api.ApiAsync("GET", AttachmentsWindow.Route), r =>
        {
            if (r.Status != 200) return; var worn = PeopleViews?.ViewOf(Client != null ? Client.SelfPeerId : 0)?.GetComponent<AvatarAttachments>(); if (worn != null) worn.Apply(AttachmentData.ParseList(r.Body, out _));
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
                new PersonActionsWindow(Context, match.Id(), displayName, id => { var window = new ProfileWindow(Context); window.Open(); window.Load(id); }, (id, name) => { var social = new SocialWindow(Context); social.Open(); social.Talk(id, name); }).Open();
            });
        }

        /// <summary>The script editor needs to know which object: until the building tools pick one with the mouse, the person types its identifier.</summary>
        private FloatingWindow ScriptOpener()
        {
            var window = new FloatingWindow(Ui, "script-open", "Abrir script de um objeto", 380f, -1f, new Vector2(300, 140)); var id = UiKit.Field("Identificador do objeto", false, 40); var note = UiKit.Text("", "lv-small", "lv-bad"); window.Body.Add(id); window.Body.Add(note);
            window.Body.Add(UiKit.Button("Abrir o editor", () => { if (!Guid.TryParse(id.value.Trim(), out var obj)) { note.text = "Digite o identificador do objeto."; return; } window.Close(); new LuaEditorWindow(Context, _island, obj).Open(); }, "lv-btn-primary")); return window;
        }

        private PlayerViews PeopleViews => _world != null ? _world.GetComponentInChildren<PlayerViews>() : null;

        // ---- the list of islands, before signing in ----

        private IEnumerator LoadIslands()
        {
            var listing = _api.GetIslandsAsync(); while (!listing.IsCompleted) yield return null;
            var islands = new List<(Guid, string, string)>();
            if (listing.Result != null) foreach (var row in Json.Rows(listing.Result)) if (row.Id() != Guid.Empty) islands.Add((row.Id(), row.Text("name", "Ilha"), row.Text("category")));
            Login.SetIslands(islands, Settings.LastIsland);
        }

        // ---- signing in ----

        private void OnSignIn(string user, string password, Guid island) => StartCoroutine(SignInFlow(user, password, null, island));
        private void OnRegister(string user, string email, string password, Guid island) => StartCoroutine(SignInFlow(user, password, email, island));

        private IEnumerator SignInFlow(string user, string password, string email, Guid island)
        {
            _island = island;
            var attempt = email == null ? _api.LoginAsync(user, password) : _api.RegisterAsync(user, email, password);
            while (!attempt.IsCompleted) yield return null;
            var result = attempt.Result;
            if (!result.Ok) { Login.Fail(string.IsNullOrEmpty(result.Message) ? "Não foi possível entrar. Tente de novo." : result.Message); yield break; }
            Settings.LastLogin = Login.UserToRemember; Settings.LastIsland = island.ToString(); Settings.Save(SettingsPath); Login.Succeed();
            yield return EnterWorld();
        }

        private void OnSessionExpired() { if (Current == Phase.Login) return; BackToLogin(); Login.Fail("Sua sessão acabou. Entre de novo."); }

        // ---- opening the island ----

        private IEnumerator EnterWorld()
        {
            Show(Phase.Loading); Loading.SetStatus("requesting_ticket");
            BuildWorld();
            var join = Client.gameObject.AddComponent<WorldJoin>(); join.Client = Client;
            var running = join.Join(_api, _island, true);
            while (running.MoveNext()) { Loading.SetStatus(join.Status, join.Failure); yield return running.Current; }
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

        /// <summary>Goes from the island the person is in to another one (a search result, a friend's invitation).</summary>
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
            Follow = _world.AddComponent<FollowCamera>(); Follow.Camera = cam; Follow.Terrain = terrain; Player = _world.AddComponent<LocalPlayer>();
            Hud.Chat.Send = text => Client != null && Client.SendChat(text);
            // what scripts of objects ask of the person: dialogs, permission requests and panels (the island checks every answer)
            ScriptDialogs = new ScriptDialogs(Ui) { Send = json => { if (Client != null) Client.SendScriptResponse(json); } }; ScriptPanels = new ScriptHud(Ui) { Send = json => { if (Client != null) Client.SendScriptResponse(json); } };
            Client.ScriptOperation += json => { ScriptDialogs.OnOperation(json); ScriptPanels.OnOperation(json); };
            Builder = new BuildEditor(Context, () => Client, () => Follow != null ? Follow.Camera : null, () => Player != null ? Player.GamePosition : Vector3.zero, () => Client != null ? Client.Terrain : null);
            Builder.Attach();
            // sound: the soundscape, what scripts play, the music of the parcel, and proximity voice
            Ambient = new GameObject("Ambient").AddComponent<AmbientSound>(); Ambient.transform.SetParent(_world.transform);
            Sounds = new GameObject("WorldSounds").AddComponent<WorldSounds>(); Sounds.transform.SetParent(_world.transform); Sounds.Attach(Client); Sounds.AssetLoader = asset => Client != null && Client.AssetLoader != null ? Client.AssetLoader(asset) : System.Threading.Tasks.Task.FromResult<byte[]>(null);
            AvatarAttachments.Loader = asset => Client != null && Client.AssetLoader != null ? Client.AssetLoader(asset) : System.Threading.Tasks.Task.FromResult<byte[]>(null);
            objects.MediaAudio = host => BusSource.Make(host, Bus.Media, .8f, true, 25f, true).Source;
            ParcelMusic = new GameObject("ParcelMusic").AddComponent<LandMusic>(); ParcelMusic.transform.SetParent(_world.transform); ParcelMusic.Setup(_api, () => _island, () => Player != null ? Player.GamePosition : (Vector3?)null); ParcelMusic.IslandFinder = () => Client != null && Client.Environment != null ? Client.Environment.GetComponent<IslandLookService>() : null; ParcelMusic.Load();
            Voice = new GameObject("Voice").AddComponent<VoiceChat>(); Voice.transform.SetParent(_world.transform); Voice.Attach(Client); Hearing = new GameObject("Hearing").AddComponent<VoiceListener>(); Hearing.transform.SetParent(_world.transform); Hearing.Attach(Client);
            Hearing.PositionOf = peer => PeopleViews != null && PeopleViews.ViewOf(peer) != null ? PeopleViews.ViewOf(peer).GamePosition : (Vector3?)null; Voice.HearingOthers = () => Hearing.AnySpeaking(); ApplyAudio();
            TerrainTools = new TerrainEditor(Context, () => Client, () => Follow != null ? Follow.Camera : null, () => Client != null ? Client.Terrain : null);
        }

        public ScriptDialogs ScriptDialogs { get; private set; }
        public ScriptHud ScriptPanels { get; private set; }
        public AmbientSound Ambient { get; private set; }
        public WorldSounds Sounds { get; private set; }
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

        private void TearDownWorld() { Ambient = null; Sounds = null; ParcelMusic = null; Voice = null; Hearing = null; BuildPanel?.Close(); TerrainPanel?.Close(); Builder?.SetActive(false); TerrainTools?.SetActive(false); BuildPanel = null; TerrainPanel = null; Builder = null; TerrainTools = null; ScriptDialogs?.Clear(); ScriptPanels?.Clear(); if (_world != null) Destroy(_world); _world = null; Client = null; Player = null; Follow = null; }

        // ---- while playing ----

        private void OnChat(LifeVerse.Wire.ReceiveChatMessage chat) => Hud.Chat.Add(chat.PeerId, chat.DisplayName, chat.Message, DateTimeOffset.FromUnixTimeMilliseconds(chat.UnixMilliseconds));
        private void OnSpawned(LifeVerse.Wire.SpawnPlayerMessage spawn) { if (spawn.PeerId != Client.SelfPeerId) Notices.Show(spawn.DisplayName + " chegou.", NoticeKind.Info, 3f); }
        private void OnDespawned(LifeVerse.Wire.DespawnPlayerMessage gone) { var view = PeopleViews?.ViewOf(gone.PeerId); if (view != null && gone.PeerId != Client.SelfPeerId) Notices.Show(view.DisplayName + " saiu.", NoticeKind.Info, 3f); }

        private void OnDisconnected(string reason) { if (Current != Phase.Playing) return; BackToLogin(); Login.Fail("A conexão com a ilha foi perdida. Entre de novo."); }

        private float _mapClock;

        private void Update()
        {
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
                if ((Builder == null || !Builder.Active) && (TerrainTools == null || !TerrainTools.Active) && Mouse.current is { } mouse && mouse.leftButton.wasPressedThisFrame && UnityEngine.Cursor.lockState != CursorLockMode.Locked && !UiKit.IsOver(Ui.Root, mouse.position.ReadValue())) ClickWorld(mouse.position.ReadValue());
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

        /// <summary>Puts the settings on the world: mouse, run, camera comfort, shadows and rain by the graphics preset.</summary>
        public void ApplySettingsToWorld()
        {
            if (Follow != null) { Follow.Sensitivity = Settings.MouseSensitivity; Follow.InvertY = Settings.InvertY; Follow.Options = Follow.Options with { HeadBob = !Settings.ReduceMotion, Sway = !Settings.ReduceMotion }; }
            if (Player != null) Player.AlwaysRun = Settings.AlwaysRun;
            Hud.Chat.ApplySettings(); ApplyAudio();
            var environment = _world != null ? _world.GetComponentInChildren<IslandEnvironment>() : null;
            if (environment != null) { var preset = GraphicsQuality.Current; environment.RainQuality = preset.RainAmount; environment.SetShadows(preset.Shadows, preset.ShadowDistance); }
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
            if (Client != null) { Client.DisconnectedBy -= OnDisconnected; Client.ChatReceived -= OnChat; Client.PlayerSpawned -= OnSpawned; Client.PlayerDespawned -= OnDespawned; }
            if (_entering != null) { StopCoroutine(_entering); _entering = null; }
            TearDownWorld(); Show(Phase.Login); Login.SetBusy(false);
        }

        private void OnDestroy() { _api?.Dispose(); }
    }
}
