using System.Security.Cryptography;
using System.Text;
using System.IO;
using AutoChartSwitch.Core;

namespace AutoChartSwitch.App;

public sealed class AppPersistence
{
    public static string RootPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SVC-AS", "AutoChartSwitchV2");
    public static string SettingsPath => Path.Combine(RootPath, "settings.json");

    private readonly AtomicJsonStore _store = new();
    private readonly SemaphoreSlim _settingsGate = new(1, 1);

    public async Task<AutoChartSettings> LoadSettingsAsync()
    {
        try
        {
            var dto = await _store.LoadAsync<SettingsDto>(SettingsPath);
            if (dto is null) return new();
            return dto.ToSettings(Unprotect(dto.EncryptedPassword));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or CryptographicException or UnauthorizedAccessException)
        {
            PreserveCorruptFile(SettingsPath);
            return new();
        }
    }

    public async Task SaveSettingsAsync(AutoChartSettings settings)
    {
        var dto = SettingsDto.FromSettings(settings, Protect(settings.ObsPassword));
        await _settingsGate.WaitAsync();
        try { await _store.SaveAsync(SettingsPath, dto); }
        finally { _settingsGate.Release(); }
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var clear = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clear);
    }

    private static void PreserveCorruptFile(string path)
    {
        try { if (File.Exists(path)) File.Move(path, path + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", true); }
        catch (IOException) { }
    }

    private sealed class SettingsDto
    {
        public string ObsUrl { get; set; } = "ws://127.0.0.1:4455";
        public string EncryptedPassword { get; set; } = "";
        public bool AutoSwitch { get; set; }
        public int BridgePort { get; set; } = 28745;
        public string GamePath { get; set; } = "";
        public string JacketCachePath { get; set; } = "";
        public string DifficultyCustomPath { get; set; } = "";
        public string EntryScene { get; set; } = "";
        public string ExitScene { get; set; } = "";
        public ObsSourceMappings Mappings { get; set; } = new();

        public AutoChartSettings ToSettings(string password) => new()
        {
            ObsUrl = ObsUrl,
            ObsPassword = password,
            AutoSwitch = AutoSwitch,
            BridgePort = BridgePort is >= 1 and <= 65535 ? BridgePort : 28745,
            GamePath = GamePath,
            JacketCachePath = JacketCachePath,
            DifficultyCustomPath = DifficultyCustomPath,
            EntryScene = EntryScene,
            ExitScene = ExitScene,
            Mappings = Mappings ?? new()
        };

        public static SettingsDto FromSettings(AutoChartSettings settings, string protectedPassword) => new()
        {
            ObsUrl = settings.ObsUrl,
            EncryptedPassword = protectedPassword,
            AutoSwitch = settings.AutoSwitch,
            BridgePort = settings.BridgePort,
            GamePath = settings.GamePath,
            JacketCachePath = settings.JacketCachePath,
            DifficultyCustomPath = settings.DifficultyCustomPath,
            EntryScene = settings.EntryScene,
            ExitScene = settings.ExitScene,
            Mappings = settings.Mappings
        };
    }
}
