using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Account;
using Microsoft.VisualStudio.Services.Account.Client;
using Microsoft.VisualStudio.Services.OAuth;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class AzureDevOpsClient
{
    private readonly string _organizationUrl;
    private readonly string _bearerToken;

    public AzureDevOpsClient(string organizationUrl, string bearerToken)
    {
        _organizationUrl = organizationUrl ?? throw new ArgumentNullException(nameof(organizationUrl));
        _bearerToken = bearerToken ?? throw new ArgumentNullException(nameof(bearerToken));
    }

    public async Task<int> GetOpenWorkItemsCountAsync()
    {
        try
        {
            // Connect to Azure DevOps using OAuth bearer token
            VssConnection connection = new VssConnection(
                new Uri(_organizationUrl),
                new VssOAuthAccessTokenCredential(_bearerToken));

            // Create work item tracking client
            WorkItemTrackingHttpClient witClient = connection.GetClient<WorkItemTrackingHttpClient>();

            // Create WIQL query for open work items
            Wiql wiql = new Wiql()
            {
                Query = $"SELECT [System.Id] FROM WorkItems " +
                        //$"WHERE [System.TeamProject] = '{_project}' " +
                        $"WHERE [System.State] <> 'Closed' " +
                        $"AND [System.State] <> 'Done' " +
                        $"AND [System.State] <> 'Removed'"
            };

            // Execute the query and get the count
            WorkItemQueryResult result = await witClient.QueryByWiqlAsync(wiql);
            return result.WorkItems.Count();
        }
        catch (Exception ex)
        {
            // Log the exception or handle it as needed
            Console.WriteLine($"Error retrieving open work items: {ex.Message}");
            throw;
        }
    }
}