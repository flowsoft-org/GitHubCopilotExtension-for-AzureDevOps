using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Azure;

public class GitHubCopilotChatCompletionService : IChatCompletionService
{
    private readonly ILogger _logger;
    
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public GitHubCopilotChatCompletionService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get chat message content using GitHub Copilot
    /// </summary>
    public async Task<ChatMessageContent> GetChatMessageContentAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting chat completion from GitHub Copilot");

            if (chatHistory == null || chatHistory.Count == 0)
            {
                _logger.LogWarning("Chat history is empty");
                return new ChatMessageContent(AuthorRole.Assistant, "I don't have any context to work with. Could you please provide more information?");
            }

            // Get the github token from executionSettings.ExtensionData
            string githubToken = executionSettings?.ExtensionData != null && executionSettings.ExtensionData.TryGetValue("githubToken", out var tokenObj) && tokenObj is string tokenStr ? tokenStr : string.Empty;
            if (string.IsNullOrEmpty(githubToken))
            {
                _logger.LogError("GitHub token is missing in PromptExecutionSettings.ExtensionData");
                return new ChatMessageContent(AuthorRole.Assistant, "GitHub token is missing. Please authenticate.");
            }

            // Convert chat history to the format expected by GitHub Copilot
            var messages = chatHistory.Select(message => new
            {
                role = ConvertAuthorRoleToString(message.Role),
                content = message.Content ?? string.Empty
            }).ToList();

            // Create request payload
            var requestPayload = new
            {
                model = executionSettings?.ModelId ?? "gpt-4o",
                stream = false,
                messages = messages
            };

            // Serialize the payload to JSON
            var jsonDocument = JsonSerializer.SerializeToDocument(requestPayload);

            // Call GitHub Copilot
            _logger.LogDebug("Sending request to GitHub Copilot API");
            _logger.LogDebug($"Prompt: {messages}");
            _logger.LogDebug($"JsonDocument: {jsonDocument}");
            var response = await GitHubService.GHCopilotChatCompletion(githubToken, jsonDocument, null);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GitHub Copilot API returned error: {StatusCode}", response.StatusCode);
                return new ChatMessageContent(AuthorRole.Assistant, $"I encountered an issue communicating with GitHub Copilot. Status code: {response.StatusCode}");
            }

            // Process the response
            _logger.LogDebug("Processing GitHub Copilot API response");
            var (role, content) = await GitHubService.ExtractLastMessageFromCompletionResponse(response);
            _logger.LogDebug($"Role: {role} Content: {content}");
            if (string.IsNullOrEmpty(content))
            {
                _logger.LogWarning("Empty content received from GitHub Copilot");
                return new ChatMessageContent(AuthorRole.Assistant, "I received an empty response from GitHub Copilot. Please try again.");
            }

            return new ChatMessageContent(AuthorRole.Assistant, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chat completion from GitHub Copilot");
            return new ChatMessageContent(AuthorRole.Assistant, $"I encountered an error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get chat message content async overload that includes the Kernel parameter
    /// </summary>
    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        var content = await GetChatMessageContentAsync(chatHistory, executionSettings, cancellationToken);
        return new List<ChatMessageContent> { content }.AsReadOnly();
    }

    /// <summary>
    /// Get streaming chat content from GitHub Copilot
    /// </summary>
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatMessageContent result;
        
        try
        {
            result = await GetChatMessageContentAsync(chatHistory, executionSettings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in streaming chat content");
            result = new ChatMessageContent(AuthorRole.Assistant, $"I encountered an error: {ex.Message}");
        }
        
        yield return new StreamingChatMessageContent(result.Role, result.Content);
    }

    /// <summary>
    /// Get streaming chat content from GitHub Copilot overload that includes the Kernel parameter
    /// </summary>
    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        return GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, cancellationToken);
    }

    /// <summary>
    /// Convert AuthorRole to string format expected by GitHub Copilot
    /// </summary>
    private string ConvertAuthorRoleToString(AuthorRole role)
    {
        if (role == AuthorRole.System) return "system";
        if (role == AuthorRole.User) return "user";
        if (role == AuthorRole.Assistant) return "assistant";
        if (role == AuthorRole.Tool) return "tool";
        return "user"; // Default fallback
    }
}