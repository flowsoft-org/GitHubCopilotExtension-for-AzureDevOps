public class AccountMapping
{
    public static async Task<(bool, long)> CheckAccountMapping(IHeaderDictionary headers, ILogger logger)
    {
        var azureDevOpsToken = headers["x-azure-devops-token"];
        var githubToken = headers["x-github-token"];

        if (string.IsNullOrWhiteSpace(azureDevOpsToken))
        {
            if (string.IsNullOrWhiteSpace(githubToken))
            {
                throw new ArgumentException("x-github-token cannot be null or empty.");
            }
            
            var gitHubService = new GitHubService(githubToken!, logger);
            var userId = await gitHubService.GetUserIdAsync();

            return (false, userId);
        }
        else
        {
            logger.LogWarning("Azure DevOps Token provided, but handling is not implemented.");
            return (true, 0);
        }
    }
}