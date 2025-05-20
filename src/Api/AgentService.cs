using Microsoft.SemanticKernel;
using System.Text.Json;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

public class AgentService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly ILogger<AgentService> _logger;
    private readonly IConfiguration _configuration;

    public AgentService(Kernel kernel, IChatCompletionService chatCompletionService, ILogger<AgentService> logger, IConfiguration configuration)
    {
        _kernel = kernel;
        _chatCompletionService = chatCompletionService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Process a chat message using the agent
    /// </summary>
    public async Task<string> ProcessChatMessageAsync(string userMessage, string githubToken, string azureDevOpsToken, string azureDevOpsOrganizationUrl)
    {
        try
        {
            _logger.LogInformation("Processing chat message: {Message}", userMessage);

            // Create chat history
            var chatHistory = new ChatHistory();
            
            // Add system message with instructions
            chatHistory.AddSystemMessage(GetSystemPrompt(azureDevOpsOrganizationUrl));
            
            // Add user message
            chatHistory.AddUserMessage(userMessage);

            // Get completion from the chat service
            var result = await _chatCompletionService.GetChatMessageContentAsync(chatHistory);
            
            return result.Content ?? "I couldn't generate a response. Please try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat message");
            return $"Sorry, I encountered an error: {ex.Message}";
        }
    }

    /// <summary>
    /// Process the GitHub Copilot request and return a response
    /// </summary>
    public async Task<string> ProcessGitHubCopilotRequestAsync(JsonDocument requestBody, string githubToken, string azureDevOpsToken, string azureDevOpsOrganizationUrl)
    {
        try
        {
            // Extract messages from the request
            var messages = new List<ChatMessageContent>();
            
            if (requestBody.RootElement.TryGetProperty("messages", out var messagesElement) && 
                messagesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messagesElement.EnumerateArray())
                {
                    try
                    {
                        var role = message.GetProperty("role").GetString();
                        var content = message.GetProperty("content").GetString();
                        
                        if (role == "system")
                        {
                            messages.Add(new ChatMessageContent(AuthorRole.System, content));
                        }
                        else if (role == "user")
                        {
                            messages.Add(new ChatMessageContent(AuthorRole.User, content));
                        }
                        else if (role == "assistant")
                        {
                            messages.Add(new ChatMessageContent(AuthorRole.Assistant, content));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error processing message in request");
                        // Continue processing other messages
                    }
                }
            }
            
            if (messages.Count == 0)
            {
                // If no valid messages found, extract content directly or use a default
                string userMessage = "Hello";
                try
                {
                    if (requestBody.RootElement.TryGetProperty("content", out var contentElement))
                    {
                        userMessage = contentElement.GetString() ?? "Hello";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not extract content from request");
                }
                
                return await ProcessChatMessageAsync(userMessage, githubToken, azureDevOpsToken, azureDevOpsOrganizationUrl);
            }
            
            // Create chat history
            var chatHistory = new ChatHistory();
            
            // Add system message with instructions
            chatHistory.AddSystemMessage(GetSystemPrompt(azureDevOpsOrganizationUrl));
            
            // Add messages from request
            foreach (var message in messages)
            {
                if (message.Role == AuthorRole.System)
                {
                    chatHistory.AddSystemMessage(message.Content ?? string.Empty);
                }
                else if (message.Role == AuthorRole.User)
                {
                    chatHistory.AddUserMessage(message.Content ?? string.Empty);
                }
                else if (message.Role == AuthorRole.Assistant)
                {
                    chatHistory.AddAssistantMessage(message.Content ?? string.Empty);
                }
            }
            
            try
            {
                // Get completion from the chat service
                var result = await _chatCompletionService.GetChatMessageContentAsync(chatHistory);
                
                // Format response for GitHub Copilot Extension
                return GitHubService.SimpleResponseMessage(result.Content ?? "I couldn't generate a response. Please try again.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chat completion");
                return GitHubService.SimpleResponseMessage($"Sorry, I encountered an error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GitHub Copilot request");
            return GitHubService.SimpleResponseMessage($"Sorry, I encountered an error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the system prompt with instructions for the agent
    /// </summary>
    private string GetSystemPrompt(string azureDevOpsOrganizationUrl)
    {
        return $@"You are an AI assistant that helps users with Azure DevOps. 
You have access to the Azure DevOps organization at {azureDevOpsOrganizationUrl}.
Your goal is to provide helpful, accurate information about work items, repositories, builds, releases, and other Azure DevOps resources.

Here are some examples of tasks you can help with:
- Retrieving information about work items
- Searching for work items based on criteria
- Checking the status of builds and releases
- Providing information about repositories
- Helping with Azure DevOps queries and best practices

When providing information, be concise and specific.
If you don't know the answer or don't have access to certain information, please say so clearly.";
    }
}