using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace MirAIe.Core;

/// <summary>
/// REST client for the MirAIe cloud. Re-signs in automatically when the access token is rejected.
/// </summary>
internal sealed class MirAIeApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private string? _username;
    private string? _password;

    public MirAIeApiClient(ILogger logger)
    {
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MirAIe-Desktop/1.0");
    }

    public string? AccessToken { get; private set; }

    public async Task LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["clientId"] = MirAIeEndpoints.ClientId,
            ["password"] = password,
            ["scope"] = MirAIeEndpoints.Scope,
            // Phone numbers are expected with the country code, e.g. +91XXXXXXXXXX.
            [username.Contains('@') ? "email" : "mobile"] = username,
        };

        using var content = new StringContent(body.ToJsonString(), new MediaTypeHeaderValue("application/json"));
        using var response = await _http.PostAsync(MirAIeEndpoints.Login, content, cancellationToken).ConfigureAwait(false);

        if ((int)response.StatusCode is >= 400 and < 500)
            throw new MirAIeAuthenticationException($"MirAIe rejected the sign-in ({(int)response.StatusCode}).");
        if (!response.IsSuccessStatusCode)
            throw new MirAIeException($"MirAIe sign-in is unavailable ({(int)response.StatusCode}).");

        using var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        AccessToken = json.RootElement.GetText("accessToken")
            ?? throw new MirAIeException("MirAIe sign-in response did not include an access token.");
        _username = username;
        _password = password;
        _logger.LogInformation("Signed in to MirAIe");
    }

    /// <summary>
    /// Signs in again with the stored credentials. Falls back to the current token on failure,
    /// so a reconnect can still be attempted.
    /// </summary>
    public async Task<string> RenewTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReauthenticateAsync(AccessToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Token renewal failed; reusing the current token");
        }

        return AccessToken ?? throw new InvalidOperationException("Not signed in.");
    }

    /// <summary>
    /// Sends an authorized GET and returns the parsed JSON body.
    /// </summary>
    public async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        var token = AccessToken;
        var response = await SendGetAsync(url, token, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            await ReauthenticateAsync(token, cancellationToken).ConfigureAwait(false);
            response = await SendGetAsync(url, AccessToken, cancellationToken).ConfigureAwait(false);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new MirAIeException($"MirAIe request failed ({(int)response.StatusCode}).");

            return await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _authLock.Dispose();
    }

    private Task<HttpResponseMessage> SendGetAsync(string url, string? token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _http.SendAsync(request, cancellationToken);
    }

    private async Task ReauthenticateAsync(string? staleToken, CancellationToken cancellationToken)
    {
        if (_username is null || _password is null)
            throw new MirAIeAuthenticationException("Not signed in.");

        await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may already have refreshed the token while we waited.
            if (AccessToken == staleToken)
                await LoginAsync(_username, _password, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _authLock.Release();
        }
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug("Unexpected response from {Url}: {Body}", response.RequestMessage?.RequestUri, body);
            throw new MirAIeException("MirAIe returned an unexpected response.", ex);
        }
    }
}
