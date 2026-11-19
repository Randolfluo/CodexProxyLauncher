#if NETFRAMEWORK
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using SettingsFormatException = System.Runtime.Serialization.SerializationException;
#else
using System.Text.Json;
using System.Text.Json.Serialization;
using SettingsFormatException = System.Text.Json.JsonException;
#endif

namespace CodexProxyLauncher;

#if !NETFRAMEWORK
[JsonConverter(typeof(JsonStringEnumConverter<AppearanceMode>))]
#endif
internal enum AppearanceMode
{
    System,
    Light,
    Dark,
}

internal sealed record LauncherSettings(string Host, int Port, bool RestartRunning)
{
    private static readonly Uri DefaultProxy = new(LauncherOptions.DefaultProxy);
    public static LauncherSettings Default { get; } = new(DefaultProxy.Host, DefaultProxy.Port, false);

    public AppearanceMode Appearance { get; init; }

    public static bool IsValidHost(string? host) =>
        host is { Length: > 0 and <= 255 } && Uri.CheckHostName(host) != UriHostNameType.Unknown;

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    public Uri CreateProxy() => new UriBuilder(Uri.UriSchemeHttp, Host, Port).Uri;
}

internal sealed class LauncherSettingsStore(string path)
{
    public LauncherSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            LauncherSettings? settings = ReadSettings();
            if (settings is not null && LauncherSettings.IsValidHost(settings.Host) &&
                LauncherSettings.IsValidPort(settings.Port))
            {
                return Enum.IsDefined(typeof(AppearanceMode), settings.Appearance)
                    ? settings
                    : settings with { Appearance = AppearanceMode.System };
            }
            warning = "本地设置无效，已使用默认设置。";
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SettingsFormatException)
        {
            warning = "无法读取本地设置，已使用默认设置。";
        }
        return LauncherSettings.Default;
    }

    public void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteSettings(tempPath, settings);
#if NETFRAMEWORK
            if (File.Exists(path))
                File.Replace(tempPath, path, destinationBackupFileName: null);
            else
                File.Move(tempPath, path);
#else
            File.Move(tempPath, path, overwrite: true);
#endif
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

#if NETFRAMEWORK
    private LauncherSettings? ReadSettings()
    {
        using var file = File.OpenRead(path);
        var serializer = new DataContractJsonSerializer(typeof(SettingsDocument));
        if (serializer.ReadObject(file) is not SettingsDocument document)
            return null;

        Enum.TryParse(document.Appearance, ignoreCase: true, out AppearanceMode appearance);
        return new LauncherSettings(document.Host!, document.Port, document.RestartRunning)
        {
            Appearance = appearance,
        };
    }

    private static void WriteSettings(string filePath, LauncherSettings settings)
    {
        using var file = File.Create(filePath);
        var serializer = new DataContractJsonSerializer(typeof(SettingsDocument));
        serializer.WriteObject(file, new SettingsDocument
        {
            Host = settings.Host,
            Port = settings.Port,
            RestartRunning = settings.RestartRunning,
            Appearance = settings.Appearance.ToString(),
        });
    }

    [DataContract]
    private sealed class SettingsDocument
    {
        [DataMember] public string? Host { get; set; }
        [DataMember] public int Port { get; set; }
        [DataMember] public bool RestartRunning { get; set; }
        [DataMember] public string? Appearance { get; set; }
    }
#else
    private LauncherSettings? ReadSettings() =>
        JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path));

    private static void WriteSettings(string filePath, LauncherSettings settings) =>
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings));
#endif
}
