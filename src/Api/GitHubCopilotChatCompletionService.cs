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

            // Convert chat history to the format expected by GitHub Copilot
            var messages = chatHistory.Select(message => new
            {
                role = ConvertAuthorRoleToString(message.Role),
                content = message.Content
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
            var response = await GitHubService.GHCopilotChatCompletion(_githubToken, jsonDocument, null);

            // Process the response
            var (role, content) = await GitHubService.ExtractLastMessageFromCompletionResponse(response);

            return new ChatMessageContent(AuthorRole.Assistant, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chat completion from GitHub Copilot");
            throw;
        }
    }

    /// <summary>
    /// Get streaming chat content from GitHub Copilot
    /// </summary>
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, CancellationToken cancellationToken = default)
    {
        // GitHub Copilot doesn't support streaming in our current implementation
        var result = await GetChatMessageContentAsync(chatHistory, executionSettings, cancellationToken);
        yield return new StreamingChatMessageContent(result.Role, result.Content);
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