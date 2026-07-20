using Microsoft.Win32;

namespace AIQuota.Localization;

/// <summary>Persists an explicit language override, if the user picked one from the menu.</summary>
internal static class LanguagePreference
{
    private const string KeyPath = @"Software\AIQuota";
    private const string ValueName = "Language";

    public static AppLanguage? Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        return (key?.GetValue(ValueName) as string) switch
        {
            "de" => AppLanguage.German,
            "en" => AppLanguage.English,
            _ => null,
        };
    }

    public static void Save(AppLanguage language)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, language == AppLanguage.German ? "de" : "en");
    }
}
