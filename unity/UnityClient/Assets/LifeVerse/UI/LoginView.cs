using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The login screen: the name of the game, a card with the user and password (and "lembrar usuário", which keeps only the name, never the password), the way to create an
    /// account (user, e-mail, password), the island to enter, and a line that says what is happening or what went wrong, in Portuguese. The screen only collects and checks what was
    /// typed; <see cref="SignIn"/> and <see cref="Register"/> are told, and the answer comes back through <see cref="Fail"/> or <see cref="Succeed"/>.
    /// </summary>
    public sealed class LoginView : VisualElement
    {
        public readonly TextField User, Password, Email, Island; public readonly DropdownField Islands; public readonly Toggle Remember;
        public readonly Button Enter, Switch; public readonly Label Status, Version;
        public bool Registering { get; private set; }
        public bool Busy { get; private set; }
        public event Action<string, string, Guid> SignIn;
        public event Action<string, string, string, Guid> Register;
        private readonly VisualElement _card, _emailRow; private readonly Label _title; private VisualElement _updateBanner; private Label _updateText; private Button _updateButton;
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
                _updateBanner = UiKit.Glass(); _updateBanner.name = "update"; _updateBanner.style.width = 400; _updateBanner.style.marginBottom = 10; _updateText = UiKit.Text("", "lv-small"); _updateText.style.whiteSpace = WhiteSpace.Normal; _updateBanner.Add(_updateText);
                _updateButton = UiKit.Button("Baixar a nova versão", () => download?.Invoke(), "lv-btn-primary"); _updateBanner.Add(_updateButton); _installButton = UiKit.Button("Baixar e instalar", () => InstallRequested?.Invoke()); _installButton.style.display = DisplayStyle.None; _updateBanner.Add(_installButton); Insert(2, _updateBanner);
            }
            _updateBanner.style.display = DisplayStyle.Flex; _updateText.text = text; _updateText.EnableInClassList("lv-bad", required); _updateButton.style.display = canDownload ? DisplayStyle.Flex : DisplayStyle.None; _updateButton.clickable = new Clickable(() => download?.Invoke());
        }

        public LoginView(UserSettings settings, string versionText = "")
        {
            name = "login"; style.flexGrow = 1; style.alignItems = Align.Center; style.justifyContent = Justify.Center; style.backgroundColor = new Color(.03f, .06f, .08f, .96f); pickingMode = PickingMode.Position;
            var logo = UiKit.Text("LifeVerse", "lv-title-text", "lv-accent"); logo.style.fontSize = 56; logo.style.marginBottom = 4; Add(logo);
            Add(UiKit.Text("Um mundo para viver, criar e se encontrar", "lv-muted")); Children().Last().style.marginBottom = 26;
            _card = UiKit.Glass(); _card.style.width = 400; _card.style.paddingLeft = _card.style.paddingRight = 22; _card.style.paddingTop = _card.style.paddingBottom = 20; Add(_card);
            _title = UiKit.Text("Entrar", "lv-title-text"); _title.style.marginBottom = 8; _card.Add(_title);
            User = UiKit.Field("Usuário ou e-mail", false, 64, settings.LastLogin); User.name = "user"; _card.Add(User);
            _emailRow = new VisualElement(); Email = UiKit.Field("E-mail", false, 128); Email.name = "email"; _emailRow.Add(Email); _emailRow.style.display = DisplayStyle.None; _card.Add(_emailRow);
            Password = UiKit.Field("Senha", true, 128); Password.name = "password"; _card.Add(Password);
            Islands = new DropdownField("Ilha", new System.Collections.Generic.List<string> { "Carregando as ilhas…" }, 0) { name = "islands" }; Islands.RegisterValueChangedCallback(e => { if (Islands.index >= 0 && Islands.index < _islandIds.Count) Island.value = _islandIds[Islands.index].ToString(); }); _card.Add(Islands);
            Island = UiKit.Field("Ilha (código)", false, 64, settings.LastIsland); Island.name = "island"; _card.Add(Island);
            Remember = UiKit.Check("Lembrar usuário", settings.LastLogin.Length > 0, _ => { }); Remember.name = "remember"; _card.Add(Remember);
            Status = UiKit.Text("", "lv-small"); Status.name = "status"; Status.style.minHeight = 34; Status.style.whiteSpace = WhiteSpace.Normal; Status.style.marginTop = 6; _card.Add(Status);
            Enter = UiKit.Button("Entrar", Submit, "lv-btn-primary"); Enter.name = "enter"; Enter.style.marginTop = 6; _card.Add(Enter);
            Switch = UiKit.Button("Criar uma conta", ToggleMode, "lv-btn-flat"); Switch.name = "switch"; _card.Add(Switch);
            Version = UiKit.Text(versionText, "lv-dim", "lv-small"); Version.style.position = Position.Absolute; Version.style.bottom = 14; Version.style.right = 20; Add(Version);
            foreach (var field in new[] { User, Password, Email, Island }) field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit(); });
        }

        private readonly System.Collections.Generic.List<Guid> _islandIds = new System.Collections.Generic.List<Guid>();
        public int IslandCount => _islandIds.Count;

        /// <summary>Fills the list of islands (name and category of each public island) and picks the one used last time.</summary>
        public void SetIslands(System.Collections.Generic.IReadOnlyList<(Guid Id, string Name, string Category)> islands, string last)
        {
            _islandIds.Clear(); var names = new System.Collections.Generic.List<string>(); var pick = 0;
            foreach (var island in islands) { if (island.Id.ToString() == last) pick = names.Count; _islandIds.Add(island.Id); names.Add(island.Name + (string.IsNullOrEmpty(island.Category) ? "" : "  ·  " + island.Category)); }
            if (names.Count == 0) { names.Add("Nenhuma ilha pública. Digite o código abaixo."); }
            Islands.choices = names; Islands.index = pick; if (_islandIds.Count > 0 && Island.value.Trim().Length == 0) Island.value = _islandIds[pick].ToString();
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
            if (!Guid.TryParse((island ?? "").Trim(), out islandId)) return "Digite o código da ilha (um identificador como 3f2b…).";
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

        public void SetBusy(bool busy) { Busy = busy; Enter.SetEnabled(!busy); Switch.SetEnabled(!busy); User.SetEnabled(!busy); Password.SetEnabled(!busy); Email.SetEnabled(!busy); Island.SetEnabled(!busy); }

        public void Fail(string message) { SetBusy(false); Say(message, true); }

        public void Succeed() { SetBusy(false); Password.value = ""; Say("", false); }

        /// <summary>The user name to remember, or "" when the person did not ask for it.</summary>
        public string UserToRemember => Remember.value ? User.value.Trim() : "";
    }

    internal static class ElementExtensions
    {
        public static VisualElement Last(this System.Collections.Generic.IEnumerable<VisualElement> children) { VisualElement last = null; foreach (var child in children) last = child; return last; }
    }
}
