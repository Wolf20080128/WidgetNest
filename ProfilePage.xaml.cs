using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WidgetNest;

/// <summary>
/// 我的主页：本机账号的注册 / 登录 / 签到 / 个性签名 / 退出。
/// 数据全部落在 %LOCALAPPDATA%\WidgetNest\account.json，不联网。
/// </summary>
public partial class ProfilePage : UserControl {
    private static readonly (Color From, Color To)[] AvatarPalette = {
        (Color.FromRgb(0x4C, 0x8D, 0xFF), Color.FromRgb(0x7A, 0x5C, 0xFF)),
        (Color.FromRgb(0xFF, 0x8A, 0x5C), Color.FromRgb(0xFF, 0x5C, 0x8A)),
        (Color.FromRgb(0x2E, 0xC4, 0xB6), Color.FromRgb(0x3D, 0x9B, 0xFF)),
        (Color.FromRgb(0xF5, 0xA6, 0x23), Color.FromRgb(0xFF, 0x6B, 0x6B)),
        (Color.FromRgb(0x9B, 0x6B, 0xFF), Color.FromRgb(0x5C, 0x8A, 0xFF)),
    };

    private AccountBook _book = new();
    private UserAccount? _account;

    /// <summary>昵称是否处于行内编辑状态。</summary>
    private bool _editingName;

    public ProfilePage() {
        InitializeComponent();

        // PasswordBox.Password 不是依赖属性，占位文字的显隐只能靠事件同步
        BindPasswordPlaceholder(LoginPassword, LoginPasswordHint);
        BindPasswordPlaceholder(RegisterPassword, RegisterPasswordHint);

        // 每次挂到窗口上（从更多菜单进来）都重新读一次，跨天后签到状态才是对的
        Loaded += (_, _) => Refresh();
    }

    private static void BindPasswordPlaceholder(PasswordBox box, TextBlock hint) {
        void Sync() {
            hint.Visibility = box.Password.Length == 0 && !box.IsKeyboardFocused
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        box.PasswordChanged += (_, _) => Sync();
        box.GotKeyboardFocus += (_, _) => Sync();
        box.LostKeyboardFocus += (_, _) => Sync();

        Sync();
    }

    /// <summary>
    /// 按存档决定显示：已登录 → 资料页；未登录 → 登录/注册入口。
    /// </summary>
    public void Refresh() {
        _book = AccountStorage.Load();
        _account = _book.Active;

        if (_account is not null)
            ShowProfile();
        else
            ShowGuest();
    }

    // ===== 视图切换 =====

    private void ShowGuest() {
        GuestView.Visibility = Visibility.Visible;
        AuthView.Visibility = Visibility.Collapsed;
        ProfileView.Visibility = Visibility.Collapsed;

        // 多账号：本机已有账号也照样能注册新号，旧号用邮箱/昵称还能登回来
        GuestRegisterButton.Visibility = Visibility.Visible;

        GuestHint.Text = _book.Accounts.Count switch {
            0 => "数据只保存在本机，不会上传",
            1 => $"本机账号：{_book.Accounts[0].Nickname}",
            _ => $"本机已有 {_book.Accounts.Count} 个账号"
        };
    }

    private void ShowLogin() {
        GuestView.Visibility = Visibility.Collapsed;
        AuthView.Visibility = Visibility.Visible;
        ProfileView.Visibility = Visibility.Collapsed;

        LoginForm.Visibility = Visibility.Visible;
        RegisterForm.Visibility = Visibility.Collapsed;
        AuthTitle.Text = "登录";
        LoginError.Text = "";
        RegisterError.Text = "";

        // 多账号下注册入口一直保留
        GoRegisterButton.Visibility = Visibility.Visible;

        // 有账号就把最近一个的身份预填上，少打一次字
        var last = _book.Accounts.LastOrDefault();
        if (last is not null && string.IsNullOrEmpty(LoginId.Text))
            LoginId.Text = last.Nickname;

        FocusLater(LoginId);
    }

    private void ShowRegister() {
        GuestView.Visibility = Visibility.Collapsed;
        AuthView.Visibility = Visibility.Visible;
        ProfileView.Visibility = Visibility.Collapsed;

        LoginForm.Visibility = Visibility.Collapsed;
        RegisterForm.Visibility = Visibility.Visible;
        AuthTitle.Text = "注册";
        LoginError.Text = "";
        RegisterError.Text = "";

        RegisterEmail.Text = "";
        RegisterNickname.Text = "";
        RegisterPassword.Clear();

        FocusLater(RegisterEmail);
    }

    private void ShowProfile() {
        GuestView.Visibility = Visibility.Collapsed;
        AuthView.Visibility = Visibility.Collapsed;
        ProfileView.Visibility = Visibility.Visible;

        EndNameEdit();
        UpdateProfileVisuals();
    }

    private static void FocusLater(Control control, bool selectAll = false) {
        // 控件刚变可见时立即 Focus 有时会被窗口激活流程抢走，排到输入优先级之后
        control.Dispatcher.BeginInvoke(new Action(() => {
            control.Focus();
            Keyboard.Focus(control);

            if (selectAll && control is TextBox box)
                box.SelectAll();
        }), DispatcherPriority.Input);
    }

    // ===== 资料渲染 =====

    private void UpdateProfileVisuals() {
        if (_account is null) {
            ShowGuest();
            return;
        }

        ApplyAvatar();

        NameText.Text = _account.Nickname;
        LevelText.Text = $"Lv.{_account.Level}";
        SignatureBox.Text = _account.Signature;
        NameError.Visibility = Visibility.Collapsed;

        ExpText.Text = $"经验 {_account.Experience % 100} / 100";

        if (_account.SignedInToday) {
            SignInText.Text = "今日已签到";
            SignInButton.IsEnabled = false;
            SignInIcon.Fill = ThemeManager.InkBrush(0x66);
            SignInHint.Text = $"已连续签到 {_account.SignInStreak} 天 · 明天再来";
        }
        else {
            SignInText.Text = "签到";
            SignInButton.IsEnabled = true;
            SignInIcon.Fill = ThemeManager.Brush(ThemeManager.TextOnAccent);
            SignInHint.Text = _account.SignInStreak > 0
                ? $"每天签到 +10 经验 · 已连续 {_account.SignInStreak} 天"
                : "每天签到 +10 经验";
        }
    }

    // ===== 头像 =====

    private void ApplyAvatar() {
        if (_account is null)
            return;

        string path = AccountStorage.ResolveAvatarPath(_account.AvatarFile);
        BitmapSource? image = File.Exists(path) ? LoadAvatar(path) : null;

        if (image is not null) {
            var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            brush.Freeze();

            AvatarCircle.Fill = brush;
            AvatarInitial.Visibility = Visibility.Collapsed;
            return;
        }

        // 没换过头像：昵称首字 + 渐变兜底
        AvatarCircle.Fill = AvatarBrush(_account.Nickname);
        AvatarInitial.Text = InitialOf(_account.Nickname);
        AvatarInitial.Visibility = Visibility.Visible;
    }

    private static BitmapSource? LoadAvatar(string path) {
        try {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 128;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch {
            return null;
        }
    }

    private void Avatar_Click(object sender, MouseButtonEventArgs e) {
        if (_account is null)
            return;

        e.Handled = true;

        var dialog = new OpenFileDialog {
            Title = "选择头像图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*",
            CheckFileExists = true
        };

        var owner = Window.GetWindow(this);

        // WPF 的选择框没有 Owner 属性，靠 ShowDialog(owner) 指定宿主
        bool? picked = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();

        if (picked != true)
            return;

        string? avatarFile = AccountStorage.TrySaveAvatar(_account.Email, dialog.FileName);

        if (avatarFile is null) {
            MessageBox.Show("这张图片读不出来，换一张试试。", "头像",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _account.AvatarFile = avatarFile;
        AccountStorage.Save(_book);
        ApplyAvatar();

        owner?.Activate();
    }

    // ===== 昵称行内编辑 =====

    private void NameText_Click(object sender, MouseButtonEventArgs e) {
        e.Handled = true;
        BeginNameEdit();
    }

    private void BeginNameEdit() {
        if (_account is null || _editingName)
            return;

        _editingName = true;
        NameError.Visibility = Visibility.Collapsed;
        NameEdit.Text = _account.Nickname;
        NameText.Visibility = Visibility.Collapsed;
        NameEdit.Visibility = Visibility.Visible;

        FocusLater(NameEdit, selectAll: true);
    }

    private void EndNameEdit() {
        _editingName = false;
        NameEdit.Visibility = Visibility.Collapsed;
        NameText.Visibility = Visibility.Visible;
    }

    private void NameEdit_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Enter) {
            e.Handled = true;
            CommitName(keepOnError: true);
        }
        else if (e.Key == Key.Escape) {
            e.Handled = true;
            NameError.Visibility = Visibility.Collapsed;
            EndNameEdit();
        }
    }

    private void NameEdit_LostFocus(object sender, RoutedEventArgs e) {
        if (_editingName)
            CommitName(keepOnError: false);
    }

    /// <summary>
    /// 提交昵称。回车时校验失败就留在编辑态让用户改；
    /// 失焦时没法停留，失败就放弃这次修改并保留提示。
    /// </summary>
    private void CommitName(bool keepOnError) {
        if (!_editingName || _account is null)
            return;

        string nickname = NameEdit.Text.Trim();
        string? error = ValidateNickname(nickname, _account);

        if (error is not null) {
            NameError.Text = error;
            NameError.Visibility = Visibility.Visible;

            if (keepOnError) {
                FocusLater(NameEdit);
                return;
            }

            EndNameEdit();
            return;
        }

        if (nickname != _account.Nickname) {
            _account.Nickname = nickname;
            AccountStorage.Save(_book);
        }

        NameError.Visibility = Visibility.Collapsed;
        EndNameEdit();
        UpdateProfileVisuals();
    }

    private string? ValidateNickname(string nickname, UserAccount self) {
        if (nickname.Length < 2 || nickname.Length > 16)
            return "昵称需要 2–16 个字符";

        if (nickname.Any(char.IsWhiteSpace))
            return "昵称不能包含空格";

        bool taken = _book.Accounts.Any(a => !ReferenceEquals(a, self)
                                             && string.Equals(a.Nickname, nickname,
                                                 StringComparison.OrdinalIgnoreCase));

        return taken ? "这个昵称已被占用" : null;
    }

    private static string InitialOf(string nickname) {
        char first = nickname.FirstOrDefault(c => !char.IsWhiteSpace(c));
        if (first == '\0')
            return "?";

        return char.ToUpperInvariant(first).ToString();
    }

    private static Brush AvatarBrush(string nickname) {
        int hash = 0;
        foreach (char c in nickname)
            hash = (hash * 31 + c) & 0x7FFFFFFF;

        var (from, to) = AvatarPalette[hash % AvatarPalette.Length];

        var brush = new LinearGradientBrush(from, to, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    // ===== 注册 =====

    private void Register_Click(object sender, RoutedEventArgs e) {
        string email = RegisterEmail.Text.Trim();
        string nickname = RegisterNickname.Text.Trim();
        string password = RegisterPassword.Password;

        if (!LooksLikeEmail(email)) {
            RegisterError.Text = "请输入有效的邮箱地址";
            RegisterEmail.Focus();
            return;
        }

        if (nickname.Length < 2 || nickname.Length > 16) {
            RegisterError.Text = "昵称需要 2–16 个字符";
            RegisterNickname.Focus();
            return;
        }

        if (nickname.Any(char.IsWhiteSpace)) {
            RegisterError.Text = "昵称不能包含空格";
            RegisterNickname.Focus();
            return;
        }

        if (password.Length < 6) {
            RegisterError.Text = "密码至少 6 位";
            RegisterPassword.Focus();
            return;
        }

        var book = AccountStorage.Load();

        if (book.Accounts.Any(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase))) {
            RegisterError.Text = "这个邮箱已经注册过了";
            RegisterEmail.Focus();
            return;
        }

        if (book.Accounts.Any(a => string.Equals(a.Nickname, nickname, StringComparison.OrdinalIgnoreCase))) {
            RegisterError.Text = "这个昵称已被占用";
            RegisterNickname.Focus();
            return;
        }

        var (hash, salt) = AccountStorage.HashPassword(password);

        var account = new UserAccount {
            Email = email,
            Nickname = nickname,
            PasswordHash = hash,
            PasswordSalt = salt,
            CreatedAt = DateTime.Now
        };

        book.Accounts.Add(account);
        book.ActiveEmail = account.Email;

        AccountStorage.Save(book);
        _book = book;
        _account = account;

        RegisterPassword.Clear();
        ShowProfile();
    }

    // ===== 登录 =====

    private void Login_Click(object sender, RoutedEventArgs e) {
        string id = LoginId.Text.Trim();
        string password = LoginPassword.Password;

        if (id.Length == 0) {
            LoginError.Text = "请输入邮箱或昵称";
            LoginId.Focus();
            return;
        }

        if (password.Length == 0) {
            LoginError.Text = "请输入密码";
            LoginPassword.Focus();
            return;
        }

        var book = AccountStorage.Load();

        if (book.Accounts.Count == 0) {
            LoginError.Text = "本机还没有账号，先注册一个";
            return;
        }

        var account = book.Accounts.FirstOrDefault(a =>
            string.Equals(a.Email, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.Nickname, id, StringComparison.OrdinalIgnoreCase));

        if (account is null || !AccountStorage.VerifyPassword(account, password)) {
            LoginError.Text = "账号或密码不正确";
            LoginPassword.Clear();
            LoginPassword.Focus();
            return;
        }

        book.ActiveEmail = account.Email;
        AccountStorage.Save(book);
        _book = book;
        _account = account;

        LoginPassword.Clear();
        ShowProfile();
    }

    // ===== 签到 =====

    private void SignIn_Click(object sender, RoutedEventArgs e) {
        if (_account is null || _account.SignedInToday)
            return;

        int previousLevel = _account.Level;
        DateTime today = DateTime.Today;

        bool continuous = _account.LastSignInDate is { } last && last.Date == today.AddDays(-1);

        _account.SignInStreak = continuous ? _account.SignInStreak + 1 : 1;
        _account.LastSignInDate = today;
        _account.Experience += 10;

        AccountStorage.Save(_book);
        UpdateProfileVisuals();

        SignInHint.Text = _account.Level > previousLevel
            ? $"升级到 Lv.{_account.Level} 了，明天继续"
            : $"签到 +10 经验 · 已连续 {_account.SignInStreak} 天";
    }

    // ===== 个性签名 =====

    private void SignatureBox_LostFocus(object sender, RoutedEventArgs e) => CommitSignature();

    private void SignatureBox_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        CommitSignature();
        Keyboard.ClearFocus();
    }

    private void CommitSignature() {
        if (_account is null)
            return;

        string signature = SignatureBox.Text.Trim();

        if (signature.Length > 40)
            signature = signature[..40];

        if (signature == _account.Signature)
            return;

        _account.Signature = signature;
        SignatureBox.Text = signature;
        AccountStorage.Save(_book);
    }

    // ===== 退出 =====

    private void Logout_Click(object sender, RoutedEventArgs e) {
        if (_account is null)
            return;

        // 只清当前登录态，账号本身留在本机，之后还能用邮箱/昵称登回来
        _book.ActiveEmail = "";
        AccountStorage.Save(_book);
        _account = null;

        LoginId.Text = "";
        LoginPassword.Clear();
        ShowGuest();
    }

    // ===== 导航 =====

    private void GuestLogin_Click(object sender, RoutedEventArgs e) => ShowLogin();

    private void GuestRegister_Click(object sender, RoutedEventArgs e) => ShowRegister();

    private void GoRegister_Click(object sender, RoutedEventArgs e) {
        // 从登录表单跳过去时把已输入的邮箱带过去
        string pendingEmail = LoginId.Text.Trim();

        ShowRegister();

        if (pendingEmail.Contains('@'))
            RegisterEmail.Text = pendingEmail;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowGuest();

    // ===== 输入辅助 =====

    private void Field_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (RegisterForm.Visibility == Visibility.Visible)
            Register_Click(this, new RoutedEventArgs());
        else
            Login_Click(this, new RoutedEventArgs());
    }

    private static bool LooksLikeEmail(string text) {
        int at = text.IndexOf('@');
        if (at <= 0 || at != text.LastIndexOf('@') || at == text.Length - 1)
            return false;

        string domain = text[(at + 1)..];

        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.')
               && !char.IsWhiteSpace(text[0]) && !text.Contains(' ');
    }
}
