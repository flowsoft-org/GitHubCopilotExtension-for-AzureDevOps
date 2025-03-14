using GitHub;
using GitHub.Octokit.Client;
using GitHub.Octokit.Client.Authentication;
using Microsoft.Kiota.Serialization;

public class GitHubService
{
    private readonly GitHubClient _client;
    private readonly ILogger _logger;

    public GitHubService(string githubToken, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(githubToken))
        {
            throw new ArgumentException("GITHUB_TOKEN cannot be null or empty.", nameof(githubToken));
        }
        var tokenProvider = new TokenProvider(githubToken);
        var adapter = RequestAdapter.Create(new TokenAuthProvider(tokenProvider));
        _client = new GitHubClient(adapter);
        _logger = logger;
    }

    private async Task<GitHub.User.UserRequestBuilder.UserGetResponse?> GetCurrentUserAsync()
    {
        var userGetResponse = await _client.User.GetAsync();
        var currentUserJson = await userGetResponse!.PublicUser!.SerializeAsJsonStringAsync();
        _logger.LogDebug("Current user: {CurrentUser}", currentUserJson);
        return userGetResponse;
    }

    public async Task<long?> GetUserIdAsync()
    {
        var currentUser = await this.GetCurrentUserAsync();
        if (currentUser!.PublicUser == null)
        {
            return currentUser!.PrivateUser!.Id;
        } else {
            return currentUser!.PublicUser!.Id;
        }
        
    }
}


