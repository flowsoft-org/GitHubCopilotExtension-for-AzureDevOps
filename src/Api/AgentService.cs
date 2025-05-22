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
    public async Task<string> ProcessChatMessageAsync(ChatHistory chatHistory, string githubToken, string azureDevOpsToken, string azureDevOpsOrganizationUrl)
    {
        try
        {
            var arguments = new KernelArguments();
            arguments["organizationUrl"] = azureDevOpsOrganizationUrl;
            arguments["bearerToken"] = azureDevOpsToken;

            var chat = _kernel.GetRequiredService<IChatCompletionService>();
            chatHistory.AddSystemMessage(GetSystemPrompt(azureDevOpsOrganizationUrl));

            // You can optionally pass functions available for calling
            var promptExecutionSettings = new PromptExecutionSettings
            {
                ModelId = "gpt-4o",
                ExtensionData = new Dictionary<string, object>
                {
                    { "githubToken", githubToken },
                    { "bearerToken", azureDevOpsToken },
                    { "organizationUrl", azureDevOpsOrganizationUrl }
                }
            };
            var response = await chat.GetChatMessageContentAsync(chatHistory, promptExecutionSettings);

            return response.Content ?? "I couldn't generate a response. Please try again.";
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
            var chatHistory = new ChatHistory();
            
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
                            chatHistory.Add(new ChatMessageContent(AuthorRole.System, content));
                        }
                        else if (role == "user")
                        {
                            chatHistory.Add(new ChatMessageContent(AuthorRole.User, content));
                        }
                        else if (role == "assistant")
                        {
                            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, content));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error processing message in request");
                        // Continue processing other messages
                    }
                }
            }


            _logger.LogInformation("Processing chat message: {Message}", chatHistory.Last().Content);

            return await ProcessChatMessageAsync(chatHistory, githubToken, azureDevOpsToken, azureDevOpsOrganizationUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GitHub Copilot request");
            return $"Sorry, I encountered an error: {ex.Message}";
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