using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The login screen, as the one of the Second Life viewer: the picture of the world fills the window with the name of the game over it, and a bar at the bottom has the user, the password,
    /// where to start and the button to enter ("lembrar usuário" keeps only the name), with the way to create an account and, under it, the state of the servers, the news and the version.
    /// The screen only collects and checks what was typed; <see cref="SignIn"/> and <see cref="Register"/> are told, and the answer comes back through <see cref="Fail"/> or
    /// <see cref="Succeed"/>.
    /// </summary>
    public sealed class LoginView : VisualElement
    {
        public readonly TextField User, Password, Email, Island; public readonly DropdownField Islands; public readonly Toggle Remember;
        public readonly Button Enter, Switch; public readonly Label Status, Version;
        public bool Registering { get; private set; }
        public bool Busy { get; private set; }
        public event Action<string, string, Guid> SignIn;
        public event Action<string, string, string, Guid> Register;
        private readonly VisualElement _dock, _stage, _emailRow; private readonly Label _title; private VisualElement _updateBanner; private Label _updateText; private Button _updateButton;
        public string UpdateNotice => _updateText?.text ?? "";
        public bool UpdateDownloadShown => _updateButton != null && _updateButton.style.display != DisplayStyle.None;
        private Button _installButton; public bool InstallShown => _installButton != null && _installButton.style.display != DisplayStyle.None;
        public System.Action InstallRequested;
        /// <summary>The progress or the result of downloading the installer, under the notice.</summary>
        public void ShowInstall(string text, bool canInstall) { if (_updateText == null) return; _installButton.style.display = canInstall ? DisplayStyle.Flex : DisplayStyle.None; if (!string.IsNullOrEmpty(text)) _updateText.text = text; }

        /// <summary>A notice above the form: a newer version exists (a link to get it) or this one can no longer sign in (<paramref name="required"/>). An empty text takes it away.</summary>
        public void ShowUpdate(string text, bool required, bool canDownload, System.Action download)
        {
            if (string.IsNullOrEmpty(text)) { if (_updateBanner != null) _updateBanner.style.display = DisplayStyle.None; return; }
            if (_updateBanner == null)
            {
                _updateBanner = UiKit.Glass(); _updateBanner.name = "update"; _updateBanner.style.width = 440; _updateBanner.style.marginTop = 18; _updateText = UiKit.Text("", "lv-small"); _updateText.style.whiteSpace = WhiteSpace.Normal; _updateBanner.Add(_updateText);
                _updateButton = UiKit.Button("Baixar a nova versão", () => download?.Invoke(), "lv-btn-primary"); _updateBanner.Add(_updateButton); _installButton = UiKit.Button("Baixar e instalar", () => InstallRequested?.Invoke()); _installButton.style.display = DisplayStyle.None; _updateBanner.Add(_installButton); _stage.Add(_updateBanner);
            }
            _updateBanner.style.display = DisplayStyle.Flex; _updateText.text = text; _updateText.EnableInClassList("lv-bad", required); _updateButton.style.display = canDownload ? DisplayStyle.Flex : DisplayStyle.None; _updateButton.clickable = new Clickable(() => download?.Invoke());
        }

        public LoginView(UserSettings settings, string versionText = "")
        {
            name = "login"; style.flexGrow = 1; style.alignItems = Align.Stretch; style.backgroundColor = new Color(.03f, .06f, .08f, 1f); pickingMode = PickingMode.Position;
            // the panoramic picture of the Godot client, covering the window without stretching, under a light veil that keeps the words readable
            Background = Resources.Load<Texture2D>("LifeVerse/Login/background");
            if (Background != null) { style.backgroundImage = Background; style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover); style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center); style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center); }
            var veil = new VisualElement { name = "veil", pickingMode = PickingMode.Ignore }; veil.style.position = Position.Absolute; veil.style.left = veil.style.top = veil.style.right = veil.style.bottom = 0; veil.style.backgroundColor = new Color(.03f, .05f, .09f, .22f); Add(veil);
            Lights = new LoginLightShow { Still = settings.ReduceMotion }; Add(Lights);
            // the middle: the name of the game (and, when there is one, the notice of a new version)
            _stage = new VisualElement { name = "login-stage", pickingMode = PickingMode.Ignore }; _stage.style.flexGrow = 1; _stage.style.flexShrink = 1; _stage.style.minHeight = 0; _stage.style.alignItems = Align.Center; _stage.style.justifyContent = Justify.Center; Add(_stage);
            var logo = UiKit.Text("LifeVerse", "lv-brand"); _stage.Add(logo);
            _stage.Add(UiKit.Text("Um mundo para viver, criar e se encontrar", "lv-slogan"));
            // the bar to enter
            _dock = new VisualElement { name = "login-dock" }; _dock.AddToClassList("lv-login-dock"); Add(_dock);
            _title = UiKit.Text("Entrar", "lv-title-text", "lv-accent"); _title.style.unityTextAlign = TextAnchor.MiddleCenter; _title.style.marginBottom = 4; _dock.Add(_title);
            var fields = new VisualElement { name = "login-fields" }; fields.AddToClassList("lv-login-fields"); _dock.Add(fields);
            VisualElement Column(float width, params VisualElement[] children) { var column = new VisualElement(); column.AddToClassList("lv-login-column"); column.style.width = width; foreach (var child in children) column.Add(child); fields.Add(column); return column; }
            // every column is a field with one line under it, so the fields stay level
            VisualElement Under(VisualElement child) { var line = new VisualElement(); line.style.height = 28; line.style.justifyContent = Justify.Center; if (child != null) line.Add(child); return line; }
            User = UiKit.Field("Usuário ou e-mail", false, 64, settings.LastLogin); User.name = "user";
            Remember = UiKit.Check("Lembrar usuário", settings.LastLogin.Length > 0, _ => { }); Remember.name = "remember";
            Column(180f, User, Under(Remember));
            Email = UiKit.Field("E-mail", false, 128); Email.name = "email"; _emailRow = Column(180f, Email, Under(null)); _emailRow.style.display = DisplayStyle.None;
            Password = UiKit.Field("Senha", true, 128); Password.name = "password";
            RememberPassword = UiKit.Check("Lembrar a senha neste PC", false, on => { if (on) Remember.value = true; }); RememberPassword.name = "remember-password"; RememberPassword.tooltip = "A senha fica guardada pelo Windows, só para a sua conta deste computador. Desmarque para esquecer."; RememberPassword.style.display = SavedLogin.Available ? DisplayStyle.Flex : DisplayStyle.None;
            Column(222f, Password, Under(RememberPassword));
            Islands = new DropdownField("Começar em", new System.Collections.Generic.List<string> { "Carregando as ilhas…" }, 0) { name = "islands" }; Islands.RegisterValueChangedCallback(e => { if (Islands.index >= 0 && Islands.index < _islandIds.Count) Island.value = _islandIds[Islands.index] == Guid.Empty ? "" : _islandIds[Islands.index].ToString(); });
            Column(232f, Islands, Under(null));
            Island = UiKit.Field("Código da ilha (opcional)", false, 64, settings.LastIsland); Island.name = "island"; Island.tooltip = "Em branco, o servidor escolhe: onde você parou ou a ilha de boas-vindas.";
            Column(168f, Island, Under(null));
            Enter = UiKit.Button("Entrar", Submit, "lv-btn-primary"); Enter.name = "enter"; Enter.AddToClassList("lv-login-enter"); Enter.style.marginTop = 17; Enter.style.marginLeft = Enter.style.marginRight = 0;
            Switch = UiKit.Button("Criar uma conta", ToggleMode, "lv-btn-flat"); Switch.name = "switch"; Switch.AddToClassList("lv-small");
            Column(172f, Enter, Under(Switch));
            Status = UiKit.Text("", "lv-small"); Status.name = "status"; Status.style.minHeight = 18; Status.style.whiteSpace = WhiteSpace.Normal; Status.style.unityTextAlign = TextAnchor.MiddleCenter; _dock.Add(Status);
            // the strip under it: the state of the servers (the API's /health, every 20 seconds), the news, the forgotten password, the music and the version
            var bottom = new VisualElement { name = "login-bar" }; bottom.AddToClassList("lv-login-strip"); Add(bottom);
            ServerState = UiKit.Text("● Verificando servidores…", "lv-small", "lv-muted"); ServerState.name = "servers"; bottom.Add(ServerState);
            News = UiKit.Text(NoNews, "lv-small", "lv-muted"); News.name = "news"; News.style.flexGrow = 1; News.style.flexShrink = 1; News.style.unityTextAlign = TextAnchor.MiddleCenter; bottom.Add(News);
            Forgot = UiKit.Button("Esqueceu sua senha?", ShowRecovery, "lv-btn-flat"); Forgot.name = "forgot"; Forgot.AddToClassList("lv-small"); bottom.Add(Forgot);
            Music = UiKit.Button(settings.LoginMusicMuted ? "♪ Música: desligada" : "♪ Música: ligada", () => MusicToggled?.Invoke(), "lv-btn-flat"); Music.name = "music"; Music.AddToClassList("lv-small"); bottom.Add(Music);
            Version = UiKit.Text(versionText, "lv-dim", "lv-small"); Version.style.marginLeft = 14; bottom.Add(Version);
            foreach (var field in new[] { User, Password, Email, Island }) field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit(); });
        }

        private readonly System.Collections.Generic.List<Guid> _islandIds = new System.Collections.Generic.List<Guid>();
        /// <summary>The public islands listed (the first choice, "where I stopped", is not one).</summary>
        public int IslandCount => Math.Max(0, _islandIds.Count - 1);
        public const string AutoIsland = "Onde parei (último lugar ou a ilha de boas-vindas)";
        public const string NoNews = "Sem novidades no momento.";
        public readonly Toggle RememberPassword; public readonly LoginLightShow Lights;
        /// <summary>Fills the form with what "Lembrar senha" kept.</summary>
        public void FillSaved(string login, string password) { User.value = login; Password.value = password; Remember.value = true; RememberPassword.value = true; }
        public readonly Texture2D Background; public readonly Label ServerState, News; public readonly Button Forgot, Music; public event Action MusicToggled;
        public enum Servers { Checking, Online, Offline }
        public Servers ServersNow { get; private set; }

        /// <summary>"Servidores Online / Indisponíveis" from the answer of /health (0 when it could not be reached); never a made-up state.</summary>
        public void SetServers(int status)
        {
            ServersNow = status >= 200 && status < 300 ? Servers.Online : Servers.Offline;
            ServerState.text = ServersNow == Servers.Online ? "● Servidores Online" : "● Servidores Indisponíveis"; ServerState.EnableInClassList("lv-good", ServersNow == Servers.Online); ServerState.EnableInClassList("lv-bad", ServersNow == Servers.Offline); ServerState.EnableInClassList("lv-muted", false);
        }

        /// <summary>What the client really knows to tell (a new version…); several rotate every few seconds, none says so.</summary>
        public void SetNews(System.Collections.Generic.IReadOnlyList<string> items) { _news.Clear(); if (items != null) foreach (var i in items) if (!string.IsNullOrWhiteSpace(i)) _news.Add(i); _newsAt = 0; News.text = _news.Count > 0 ? _news[0] : NoNews; News.EnableInClassList("lv-muted", _news.Count == 0); }
        public void NextNews() { if (_news.Count < 2) return; _newsAt = (_newsAt + 1) % _news.Count; News.text = _news[_newsAt]; }
        private readonly System.Collections.Generic.List<string> _news = new System.Collections.Generic.List<string>(); private int _newsAt;
        public void SetMusic(bool muted) => Music.text = muted ? "♪ Música: desligada" : "♪ Música: ligada";

        /// <summary>The server has no password reset (it needs an e-mail provider): this says so plainly instead of pretending to send something.</summary>
        public void ShowRecovery() => Say("A recuperação de senha por e-mail ainda não está disponível neste servidor. Se você perdeu o acesso, fale com a administração do LifeVerse informando o seu nome de usuário.", false);

        /// <summary>Fills the list of islands (name and category of each public island) and picks the one used last time.</summary>
        public void SetIslands(System.Collections.Generic.IReadOnlyList<(Guid Id, string Name, string Category)> islands, string last)
        {
            _islandIds.Clear(); var names = new System.Collections.Generic.List<string> { AutoIsland }; _islandIds.Add(Guid.Empty); var pick = 0;
            foreach (var island in islands) { if (island.Id.ToString() == (string.IsNullOrWhiteSpace(last) ? Island.value.Trim() : last.Trim())) { pick = names.Count; Island.value = island.Id.ToString(); } _islandIds.Add(island.Id); names.Add(island.Name + (string.IsNullOrEmpty(island.Category) ? "" : "  ·  " + island.Category)); }
            Islands.choices = names; Islands.index = pick;
        }

        public void ToggleMode()
        {
            Registering = !Registering; _title.text = Registering ? "Criar conta" : "Entrar"; Enter.text = Registering ? "Criar conta e entrar" : "Entrar"; Switch.text = Registering ? "Já tenho uma conta" : "Criar uma conta";
            User.label = Registering ? "Nome de usuário" : "Usuário ou e-mail"; _emailRow.style.display = Registering ? DisplayStyle.Flex : DisplayStyle.None; Say("", false);
        }

        /// <summary>The checks made before anything is sent. Null when the form is fine, otherwise what to tell the person.</summary>
        public static string Validate(bool registering, string user, string email, string password, string island, out Guid islandId)
        {
            islandId = Guid.Empty;
            if (string.IsNullOrWhiteSpace(user)) return registering ? "Escolha um nome de usuário." : "Digite o usuário ou o e-mail.";
            if (registering && (user.Trim().Length < 3 || user.Trim().Length > 32 || !System.Linq.Enumerable.All(user.Trim(), c => char.IsLetterOrDigit(c) || c == '_'))) return "O nome de usuário precisa ter de 3 a 32 letras, números ou _.";
            if (registering && (email == null || !email.Contains("@") || email.Trim().Length < 5)) return "Digite um e-mail válido.";
            if (string.IsNullOrEmpty(password)) return "Digite a senha.";
            if (registering && !StrongPassword(password)) return "A senha precisa ter de 6 a 128 caracteres, com maiúscula, minúscula e número.";
            if ((island ?? "").Trim().Length == 0) return null;   // the server chooses: the island where the person stopped, or the welcome island
            if (!Guid.TryParse(island.Trim(), out islandId)) return "Digite o código da ilha (um identificador como 3f2b…) ou deixe em branco para o servidor escolher.";
            return null;
        }

        public static bool StrongPassword(string password)
        {
            if (password == null || password.Length < 6 || password.Length > 128) return false;
            var upper = false; var lower = false; var digit = false; foreach (var c in password) { upper |= char.IsUpper(c); lower |= char.IsLower(c); digit |= char.IsDigit(c); }
            return upper && lower && digit;
        }

        public void Submit()
        {
            if (Busy) return;
            var problem = Validate(Registering, User.value, Email.value, Password.value, Island.value, out var island);
            if (problem != null) { Say(problem, true); return; }
            Say(Registering ? "Criando a conta…" : "Conectando…", false); SetBusy(true);
            if (Registering) Register?.Invoke(User.value.Trim(), Email.value.Trim(), Password.value, island); else SignIn?.Invoke(User.value.Trim(), Password.value, island);
        }

        public void Say(string text, bool error) { Status.text = text; Status.EnableInClassList("lv-bad", error); Status.EnableInClassList("lv-muted", !error); }

        public void SetBusy(bool busy) { Busy = busy; Enter.SetEnabled(!busy); Switch.SetEnabled(!busy); Forgot.SetEnabled(!busy); User.SetEnabled(!busy); Password.SetEnabled(!busy); Email.SetEnabled(!busy); Island.SetEnabled(!busy); }

        public void Fail(string message) { SetBusy(false); Say(message, true); }

        public void Succeed() { SetBusy(false); Password.value = ""; Say("", false); }

        /// <summary>The user name to remember, or "" when the person did not ask for it.</summary>
        public string UserToRemember => Remember.value ? User.value.Trim() : "";
    }
}
