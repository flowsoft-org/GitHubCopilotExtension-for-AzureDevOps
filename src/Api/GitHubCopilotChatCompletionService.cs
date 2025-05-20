using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;

public class GitHubCopilotChatCompletionService : IChatCompletionService
{
    private readonly string _githubToken;
    private readonly ILogger _logger;

    public GitHubCopilotChatCompletionService(string githubToken, ILogger logger)
    {
        _githubToken = githubToken ?? throw new ArgumentNullException(nameof(githubToken));
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
            var response = await GitHubService.GHCopilotChatCompletion(_githubToken, jsonDocument, null);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GitHub Copilot API returned error: {StatusCode}", response.StatusCode);
                return new ChatMessageContent(AuthorRole.Assistant, $"I encountered an issue communicating with GitHub Copilot. Status code: {response.StatusCode}");
            }

            // Process the response
            _logger.LogDebug("Processing GitHub Copilot API response");
            var (role, content) = await GitHubService.ExtractLastMessageFromCompletionResponse(response);

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
    /// Get streaming chat content from GitHub Copilot
    /// </summary>
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // GitHub Copilot doesn't support streaming in our current implementation
            var result = await GetChatMessageContentAsync(chatHistory, executionSettings, cancellationToken);
            yield return new StreamingChatMessageContent(result.Role, result.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in streaming chat content");
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, $"I encountered an error: {ex.Message}");
        }
    }

    /// <summary>
    /// Convert AuthorRole to string format expected by GitHub Copilot
    /// </summary>
    private string ConvertAuthorRoleToString(AuthorRole role)
    {
        return role switch
        {
            AuthorRole.System => "system",
            AuthorRole.User => "user",
            AuthorRole.Assistant => "assistant",
            _ => "user"
        };
    }
}