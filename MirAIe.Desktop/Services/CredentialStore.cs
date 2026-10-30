using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Identity.Client.Extensions.Msal;
using MirAIe.Desktop.Models;

namespace MirAIe.Desktop.Services;

/// <summary>
/// Remembers the MirAIe sign-in (username, country and password) as one entry in the OS secret store:
/// a DPAPI-encrypted file on Windows, the Keychain on macOS, and the Secret Service (GNOME Keyring / KWallet)
/// on Linux. MirAIe has no long-lived refresh flow, so the password is what keeps the session alive
/// across restarts.
/// </summary>
public sealed class CredentialStore
{
    private static readonly string AppDataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MirAIe");

    private readonly Lazy<Storage?> _storage = new(CreateStorage);

    /// <summary>
    /// False when the OS has no usable secret store, e.g. Linux without a running keyring.
    /// </summary>
    public bool CanRemember => _storage.Value is not null;

    public SavedCredentials? Load()
    {
        try
        {
            var data = _storage.Value?.ReadData();
            if (data is not { Length: > 0 })
                return null;

            var saved = JsonSerializer.Deserialize<SavedCredentials>(data);
            return string.IsNullOrEmpty(saved?.Username) ? null : saved;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <param name="password">Pass <c>null</c> to remember only the username.</param>
    public void Save(string username, string? countryCode, string? password)
    {
        try
        {
            _storage.Value?.WriteData(JsonSerializer.SerializeToUtf8Bytes(new SavedCredentials(username, countryCode, password)));
        }
        catch (Exception)
        {
            // Remembering credentials is a convenience; failing to do so must not block sign-in.
        }
    }

    /// <summary>Keeps the username for next time but drops the password, so the app won't sign in automatically.</summary>
    public void ForgetPassword()
    {
        if (Load() is { } saved)
            Save(saved.Username, saved.CountryCode, null);
    }

    public void Clear()
    {
        try
        {
            _storage.Value?.Clear(ignoreExceptions: true);
        }
        catch (Exception)
        {
            // Nothing to forget, or the store went away.
        }
    }

    private static Storage? CreateStorage()
    {
        try
        {
            var properties = new StorageCreationPropertiesBuilder("credentials.bin", AppDataDirectory)
                .WithMacKeyChain(serviceName: "MirAIe.Desktop", accountName: "MirAIe sign-in")
                .WithLinuxKeyring(
                    schemaName: "in.miraie.desktop",
                    collection: MsalCacheHelper.LinuxKeyRingDefaultCollection,
                    secretLabel: "MirAIe sign-in",
                    attribute1: new KeyValuePair<string, string>("Application", "MirAIe.Desktop"),
                    attribute2: new KeyValuePair<string, string>("Purpose", "SignIn"))
                .Build();

            var storage = Storage.Create(properties);
            storage.VerifyPersistence();
            return storage;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
