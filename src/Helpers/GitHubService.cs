using Octokit;
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
        return new GitHubClient(new ProductHeaderValue("GHCPAzureDevOpsExtension"))
        {
            Credentials = new Credentials(_githubToken),
        };
    }

    public async Task<long> GetUserIdAsync()
    {
        var client = CreateGitHubClient();
        var currentUser = await client.User.Current();
        return currentUser.Id;
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

    public static async Task<HttpResponseMessage> GHCopilotChatCompletion(string githubToken, JsonDocument history, object content) {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://api.githubcopilot.com/chat/completions");
        request.Headers.Add("User-Agent", ".NET App");
        
        if (!string.IsNullOrEmpty(githubToken))
        {
            request.Headers.Add("Authorization", $"Bearer {githubToken}");
        }

        // Extract messages from the history
        var messages = new List<object>();
        
        // Try to find messages array in the history
        if (history.RootElement.TryGetProperty("messages", out var messagesElement) && 
            messagesElement.ValueKind == JsonValueKind.Array)
        {
            // Convert existing messages to list
            foreach (var message in messagesElement.EnumerateArray())
            {
                messages.Add(new {
                    role = message.GetProperty("role").GetString(),
                    content = message.GetProperty("content").GetString()
                });
            }
        }
        
        // Add a new message
        messages.Add(content);
        
        // Create the request payload
        var requestPayload = new {
            model = "gpt-4o",
            stream = true,
            messages = messages
        };
        
        // Serialize the payload to JSON
        string jsonPayload = JsonSerializer.Serialize(requestPayload);
        
        request.Content = new StringContent(jsonPayload);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        
        var _httpClient = new HttpClient();
        HttpResponseMessage response = await _httpClient.SendAsync(request);
        return response;
    }

    public static string SimpleResponseMessage(string message) {
        return "data: {\"choices\":[{\"finish_reason\":\"stop\",\"delta\":{\"role\":\"assistant\",\"content\":\"" + message + "\"}}]}\n\ndata: [DONE]";
    }

    public static async Task<(string role, string content)> ExtractLastMessageFromCompletionResponse(HttpResponseMessage response)
    {
        if (response == null || !response.IsSuccessStatusCode)
        {
            throw new ArgumentException("Invalid response or response not successful", nameof(response));
        }

        // Read the response content
        string responseContent = await response.Content.ReadAsStringAsync();
        
        // Check if the response is in Server-Sent Events format (data: prefix)
        if (responseContent.StartsWith("data:"))
        {
            // Handle SSE format
            // Split by newlines to get individual events
            var events = responseContent.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            
            string content = string.Empty;
            string role = string.Empty;

            // Loop through all events
            for (int i = 0; i < events.Length; i++)
            {
                if (!events[i].Contains("[DONE]"))
                {
                    // Extract the JSON part after "data: "
                    string jsonPart = events[i].Substring(events[i].IndexOf("data: ") + 6);
                    using var jsonDoc = JsonDocument.Parse(jsonPart);
                    
                    // Navigate to choices[0].delta or choices[0].message
                    if (jsonDoc.RootElement.TryGetProperty("choices", out var choices) && 
                        choices.GetArrayLength() > 0)
                    {
                        var choice = choices[0];
                        
                        // Try delta (for streaming) then message (for non-streaming)
                        JsonElement contentElement;
                        JsonElement roleElement;
                        
                        if (choice.TryGetProperty("delta", out var delta))
                        {
                            delta.TryGetProperty("content", out contentElement);
                            delta.TryGetProperty("role", out roleElement);
                        }
                        else if (choice.TryGetProperty("message", out var message))
                        {
                            message.TryGetProperty("content", out contentElement);
                            message.TryGetProperty("role", out roleElement);
                        }
                        else
                        {
                            continue; // Skip if neither delta nor message exists
                        }
                        
                        content += contentElement.ValueKind != JsonValueKind.Undefined ? 
                            contentElement.GetString() ?? string.Empty : string.Empty;
                        
                        role = roleElement.ValueKind != JsonValueKind.Undefined ? 
                            roleElement.GetString() ?? "assistant" : "assistant";
                        
                    }
                }
            }

            return (role, content);
        }
        else
        {
            // Handle regular JSON response format
            try
            {
                using var jsonDoc = JsonDocument.Parse(responseContent);
                
                if (jsonDoc.RootElement.TryGetProperty("choices", out var choices) && 
                    choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    
                    if (choice.TryGetProperty("message", out var message))
                    {
                        string content = message.TryGetProperty("content", out var contentElement) ? 
                            contentElement.GetString() ?? string.Empty : string.Empty;
                        
                        string role = message.TryGetProperty("role", out var roleElement) ? 
                            roleElement.GetString() ?? "assistant" : "assistant";
                        
                        return (role, content);
                    }
                }
            }
            catch (JsonException)
            {
                // If JSON parsing fails, return the raw content
                return ("assistant", responseContent);
            }
        }
        
        // Default return if no message found
        return ("assistant", string.Empty);
    }
}


