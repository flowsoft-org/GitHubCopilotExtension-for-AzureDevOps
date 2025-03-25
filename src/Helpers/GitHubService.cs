using GitHub;
using GitHub.Octokit.Client;
using GitHub.Octokit.Client.Authentication;
using Microsoft.Kiota.Serialization;
using Org.BouncyCastle.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

public class GitHubService
{
    private readonly string _githubToken;
    private readonly ILogger _logger;
    const string GITHUB_COPILOT_KEYS_ENDPOINT = "https://api.github.com/meta/public_keys/copilot_api";
    private static Dictionary<string, byte[]?> _publicKeyCache = new Dictionary<string, byte[]?>();

    public GitHubService(string githubToken, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(githubToken))
        {
            throw new ArgumentException("GITHUB_TOKEN cannot be null or empty.", nameof(githubToken));
        }
        _githubToken = githubToken;
        _logger = logger;
    }

    private GitHubClient CreateGitHubClient()
    {
        var tokenProvider = new TokenProvider(_githubToken);
        var adapter = RequestAdapter.Create(new TokenAuthProvider(tokenProvider));
        return new GitHubClient(adapter);
    }

    private async Task<GitHub.User.UserRequestBuilder.UserGetResponse?> GetCurrentUserAsync()
    {
        var client = CreateGitHubClient();
        var userGetResponse = await client.User.GetAsync();
        var currentUserJson = await userGetResponse!.PublicUser!.SerializeAsJsonStringAsync();
        _logger.LogDebug("Current user: {CurrentUser}", currentUserJson);
        return userGetResponse;
    }

    public async Task<long> GetUserIdAsync()
    {
        var client = CreateGitHubClient();
        var userGetResponse = await client.User.GetAsync();
        if (userGetResponse?.PublicUser == null)
        {
            return userGetResponse?.PrivateUser?.Id 
                ?? throw new InvalidOperationException("Failed to retrieve user ID.");
        } else {
            return userGetResponse?.PublicUser?.Id 
                ?? throw new InvalidOperationException("Failed to retrieve user ID.");
        }
    }

    public static async Task<bool> IsValidGitHubRequest(string payload, string keyID, string signature, ILogger logger, string githubToken = "")
    {
        // Validate the payload
        if (string.IsNullOrWhiteSpace(payload))
        {
            logger.LogError("Payload is null or empty.");
            return false;
        }

        // Validate the keyID
        if (string.IsNullOrWhiteSpace(keyID))
        {
            logger.LogError("Key ID is null or empty.");
            return false;
        }

        // Validate the signature
        if (string.IsNullOrWhiteSpace(signature))
        {
            logger.LogError("Signature is null or empty.");
            return false;
        }

        byte[] publicKeyDecodedKeyData = await GetPublicKey(GITHUB_COPILOT_KEYS_ENDPOINT, keyID, githubToken);
        byte[] decodedSignature = Convert.FromBase64String(signature);

        var signer = SignerUtilities.GetSigner("SHA256withECDSA");
        signer.Init(false, PublicKeyFactory.CreateKey(publicKeyDecodedKeyData));
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
        signer.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        var verificationResult = signer.VerifySignature(decodedSignature);

       logger.LogDebug(verificationResult ? "Signature verified" : "Signature verification failed");
       return verificationResult;
    }

    // Fetches the public key from the GitHub API
    static async Task<byte[]> GetPublicKey(string endpoint, string keyId, string githubToken)
    {
        if (_publicKeyCache.TryGetValue(keyId, out byte[]? cachedKey))
        {
            return cachedKey ?? throw new InvalidOperationException("Cached key is null");
        }
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add("User-Agent", ".NET App");
        // Add the GitHub token if provided to avoid strict rate limiting
        if (!string.IsNullOrEmpty(githubToken))
        {
            request.Headers.Add("Authorization", $"Bearer {githubToken}");
        }
        var _httpClient = new HttpClient();
        HttpResponseMessage response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("public_keys", out JsonElement publicKeysElement))
        {
            throw new InvalidOperationException("No public keys found");
        }
        string encodedKeyData = FindKey(publicKeysElement, keyId);
        byte[] decodedKeyData = Convert.FromBase64String(encodedKeyData);
        _publicKeyCache[keyId] = decodedKeyData;
        return decodedKeyData;
    }

    // Finds the key in the JSON element array by key identifier
    static string FindKey(JsonElement keyArray, string keyID)
    {
        foreach (JsonElement elem in keyArray.EnumerateArray())
        {
            if (elem.TryGetProperty("key_identifier", out JsonElement keyIdentifier) &&
                keyIdentifier.GetString() == keyID &&
                elem.TryGetProperty("key", out JsonElement key))
            {
                // Extract just the key value
                string keyValue = key.GetString() ?? string.Empty;
                return Regex.Replace(
                    Regex.Replace(
                        Regex.Replace(
                            Regex.Replace(
                                Regex.Replace(keyValue, "-*BEGIN.*KEY-*", ""),
                                "-*END.*KEY-*", ""),
                            "\n", ""),
                        "\r", ""),
                    "\\s", "");
            }
        }

        throw new InvalidOperationException($"Key {keyID} not found in public keys");
    }

    public static async Task<HttpResponseMessage> GHCopilotChatCompletion(string githubToken) {

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://api.githubcopilot.com/chat/completions");
        request.Headers.Add("User-Agent", ".NET App");
        // Add the GitHub token if provided to avoid strict rate limiting
        if (!string.IsNullOrEmpty(githubToken))
        {
            request.Headers.Add("Authorization", $"Bearer {githubToken}");
        }

        request.Content = new StringContent("{\"model\":\"gpt-4o\",\"stream\":true,\"messages\":[{\"role\":\"assistant\",\"content\":\"Hello, world!\"}]}");
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var _httpClient = new HttpClient();
        HttpResponseMessage response = await _httpClient.SendAsync(request);
        return response;
    }

    public static string SimpleResponseMessage(string message) {
        return "data: {\"choices\":[{\"finish_reason\":\"stop\",\"delta\":{\"role\":\"assistant\",\"content\":\"" + message + "\"}}]}\n\ndata: [DONE]";
    }
}


