using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WidgetNest;

/// <summary>
/// 本机账号。密码只存 PBKDF2-SHA256 派生值与随机盐，不存明文。
/// </summary>
public sealed class UserAccount {
    public string Email { get; set; } = "";

    public string Nickname { get; set; } = "";

    /// <summary>个性签名，最多 40 字。</summary>
    public string Signature { get; set; } = "";

    /// <summary>Avatars 目录内的头像文件名，如 "a1b2c3d4e5f60718.png"；空 = 用昵称首字兜底。</summary>
    public string AvatarFile { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public string PasswordSalt { get; set; } = "";

    /// <summary>经验值，每次签到 +10，满 100 升一级。</summary>
    public int Experience { get; set; }

    /// <summary>连续签到天数。</summary>
    public int SignInStreak { get; set; }

    public DateTime? LastSignInDate { get; set; }

    public DateTime CreatedAt { get; set; }

    [JsonIgnore]
    public int Level => 1 + Experience / 100;

    [JsonIgnore]
    public bool SignedInToday => LastSignInDate is { } last && last.Date == DateTime.Today;
}

/// <summary>
/// 本机账号簿：可以存多个账号，ActiveEmail 指出当前登录的是谁（空 = 未登录）。
/// </summary>
public sealed class AccountBook {
    public List<UserAccount> Accounts { get; set; } = new();

    public string ActiveEmail { get; set; } = "";

    [JsonIgnore]
    public UserAccount? Active => Accounts.FirstOrDefault(a =>
        string.Equals(a.Email, ActiveEmail, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 账号持久化：%LOCALAPPDATA%\WidgetNest\account.json。
/// 兼容旧的「单账号对象」格式，读到就自动迁移成账号簿。
/// </summary>
internal static class AccountStorage {
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>头像目录，与图标目录平级。</summary>
    public static string AvatarDir => Path.Combine(AppStorage.RootDir, "Avatars");

    public static string FilePath => Path.Combine(AppStorage.RootDir, "account.json");

    public static AccountBook Load() {
        try {
            if (!File.Exists(FilePath))
                return new AccountBook();

            string json = File.ReadAllText(FilePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return new AccountBook();

            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            // 旧格式：整个文件就是一个账号对象（带 IsLoggedIn 字段）
            if (root.ValueKind == JsonValueKind.Object
                && !HasProperty(root, "Accounts")
                && !HasProperty(root, "ActiveEmail")) {
                var legacy = JsonSerializer.Deserialize<UserAccount>(json, JsonOptions);

                if (legacy is null || string.IsNullOrEmpty(legacy.Email))
                    return new AccountBook();

                bool loggedIn = !root.TryGetProperty("IsLoggedIn", out JsonElement flag)
                                || flag.ValueKind != JsonValueKind.False;

                var migrated = new AccountBook {
                    Accounts = { legacy },
                    ActiveEmail = loggedIn ? legacy.Email : ""
                };

                // 读到旧格式就顺手写成新格式，之后只维护一种结构
                Save(migrated);
                return migrated;
            }

            return JsonSerializer.Deserialize<AccountBook>(json, JsonOptions) ?? new AccountBook();
        }
        catch {
            // 文件损坏就当作没有账号，用户重新注册即可
            return new AccountBook();
        }
    }

    private static bool HasProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out _) || element.TryGetProperty(name.ToLowerInvariant(), out _);

    public static void Save(AccountBook book) {
        try {
            AppStorage.EnsureCreated();

            string json = JsonSerializer.Serialize(book, JsonOptions);
            string temp = FilePath + ".tmp";

            File.WriteAllText(temp, json, Utf8NoBom);

            // 与 apps.json 一致：先写临时文件再替换，避免写一半把账号写坏
            if (File.Exists(FilePath))
                File.Replace(temp, FilePath, null);
            else
                File.Move(temp, FilePath);
        }
        catch {
            // 落盘失败不影响本次会话
        }
    }

    public static (string Hash, string Salt) HashPassword(string password) {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations,
            HashAlgorithmName.SHA256, KeySize);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool VerifyPassword(UserAccount account, string password) {
        try {
            if (string.IsNullOrEmpty(account.PasswordHash) || string.IsNullOrEmpty(account.PasswordSalt))
                return false;

            byte[] salt = Convert.FromBase64String(account.PasswordSalt);
            byte[] expected = Convert.FromBase64String(account.PasswordHash);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations,
                HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch {
            return false;
        }
    }

    // ===== 头像 =====

    /// <summary>
    /// 把用户选的图片居中裁成正方形、缩到 128×128 存进 Avatars 目录，返回文件名；失败返回 null。
    /// 文件名按邮箱哈希，换头像直接覆盖同一个文件，不产生垃圾。
    /// </summary>
    public static string? TrySaveAvatar(string email, string sourcePath) {
        try {
            BitmapSource? image = LoadSquare(sourcePath, 128);
            if (image is null)
                return null;

            Directory.CreateDirectory(AvatarDir);

            string fileName = HashName(email) + ".png";
            string target = Path.Combine(AvatarDir, fileName);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using (var stream = File.Create(target))
                encoder.Save(stream);

            return fileName;
        }
        catch {
            // 图片损坏 / 无权限等情况，保持原头像
            return null;
        }
    }

    public static string ResolveAvatarPath(string avatarFile) =>
        string.IsNullOrWhiteSpace(avatarFile) ? "" : Path.Combine(AvatarDir, avatarFile);

    private static BitmapSource? LoadSquare(string path, int size) {
        var frame = BitmapFrame.Create(new Uri(path),
            BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);

        int width = frame.PixelWidth;
        int height = frame.PixelHeight;

        if (width <= 0 || height <= 0)
            return null;

        // 居中裁正方形（头像都用圆/方展示，避免拉伸变形）
        int edge = Math.Min(width, height);
        var crop = new CroppedBitmap(frame,
            new Int32Rect((width - edge) / 2, (height - edge) / 2, edge, edge));

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen()) {
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(crop, new Rect(0, 0, size, size));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }

    private static string HashName(string text) {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));

        // 取前 16 个十六进制字符，足够区分且文件名短
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
