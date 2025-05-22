using Microsoft.SemanticKernel;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.OAuth;
using System.ComponentModel;

public class AzureDevOpsPlugin
{
    private readonly ILogger _logger;

    public AzureDevOpsPlugin(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get the count of open work items in the organization
    /// </summary>
    [KernelFunction, Description("Get the count of open work items in the Azure DevOps organization")]
    public async Task<string> GetOpenWorkItemsCountAsync(
        [Description("The Azure DevOps organization URL")] string organizationUrl,
        [Description("The OAuth bearer token for authentication")] string bearerToken)
    {
        try
        {
            _logger.LogInformation("Getting open work items count from: {OrganizationUrl}", organizationUrl);
            
            // Connect to Azure DevOps using OAuth bearer token
            VssConnection connection = new VssConnection(
                new Uri(organizationUrl),
                new VssOAuthAccessTokenCredential(bearerToken));

            // Create work item tracking client
            WorkItemTrackingHttpClient witClient = connection.GetClient<WorkItemTrackingHttpClient>();

            // Create WIQL query for open work items
            Wiql wiql = new Wiql()
            {
                Query = $"SELECT [System.Id] FROM WorkItems " +
                        $"WHERE [System.State] <> 'Closed' " +
                        $"AND [System.State] <> 'Done' " +
                        $"AND [System.State] <> 'Removed'"
            };

            // Execute the query and get the count
            WorkItemQueryResult result = await witClient.QueryByWiqlAsync(wiql);
            return $"There are {result.WorkItems.Count()} open work items in the organization.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting open work items count from {OrganizationUrl}", organizationUrl);
            return $"Error getting open work items count: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Search for work items based on a query
    /// </summary>
    [KernelFunction, Description("Search for work items based on a query")]
    public async Task<string> SearchWorkItemsAsync(
        [Description("The Azure DevOps organization URL")] string organizationUrl,
        [Description("The OAuth bearer token for authentication")] string bearerToken,
        [Description("The WIQL query to search for work items")] string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return "Please provide a valid WIQL query to search for work items.";
            }

            _logger.LogInformation("Searching work items with query: {Query}", query);
            
            // Connect to Azure DevOps using OAuth bearer token
            VssConnection connection = new VssConnection(
                new Uri(organizationUrl),
                new VssOAuthAccessTokenCredential(bearerToken));

            // Create work item tracking client
            WorkItemTrackingHttpClient witClient = connection.GetClient<WorkItemTrackingHttpClient>();

            // Create WIQL query
            Wiql wiql = new Wiql()
            {
                Query = query
            };

            // Execute the query
            WorkItemQueryResult result = await witClient.QueryByWiqlAsync(wiql);
            
            if (result.WorkItems.Count() == 0)
            {
                return "No work items found matching the search criteria.";
            }
            
            // Get the work item details
            var workItemIds = result.WorkItems.Select(wi => wi.Id).ToArray();
            
            // Limit the number of work items to fetch to avoid performance issues
            const int maxWorkItems = 25;
            if (workItemIds.Length > maxWorkItems)
            {
                _logger.LogWarning("Found {TotalCount} work items, limiting to {MaxWorkItems}", workItemIds.Length, maxWorkItems);
                workItemIds = workItemIds.Take(maxWorkItems).ToArray();
            }
            
            var workItems = await witClient.GetWorkItemsAsync(workItemIds, expand: WorkItemExpand.Fields);
            
            // Format the results
            var formattedResults = new System.Text.StringBuilder();
            formattedResults.AppendLine($"Found {result.WorkItems.Count()} work items" + (workItemIds.Length < result.WorkItems.Count() ? $" (showing first {maxWorkItems}):" : ":"));
            
            foreach (var workItem in workItems)
            {
                formattedResults.AppendLine($"- ID: {workItem.Id}, Title: {workItem.Fields["System.Title"]}, State: {workItem.Fields["System.State"]}");
            }
            
            return formattedResults.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching work items");
            return $"Error searching work items: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Get details of a specific work item
    /// </summary>
    [KernelFunction, Description("Get details of a specific work item by ID")]
    public async Task<string> GetWorkItemDetailsAsync(
        [Description("The Azure DevOps organization URL")] string organizationUrl,
        [Description("The OAuth bearer token for authentication")] string bearerToken,
        [Description("The ID of the work item to retrieve")] int workItemId)
    {
        try
        {
            if (workItemId <= 0)
            {
                return "Please provide a valid work item ID (a positive number).";
            }
            
            _logger.LogInformation("Getting details for work item {WorkItemId}", workItemId);
            
            // Connect to Azure DevOps using OAuth bearer token
            VssConnection connection = new VssConnection(
                new Uri(organizationUrl),
                new VssOAuthAccessTokenCredential(bearerToken));

            // Create work item tracking client
            WorkItemTrackingHttpClient witClient = connection.GetClient<WorkItemTrackingHttpClient>();

            // Get the work item details
            var workItem = await witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.Fields);
            
            if (workItem == null)
            {
                return $"Work item {workItemId} not found.";
            }
            
            // Format the results
            var formattedResult = new System.Text.StringBuilder();
            formattedResult.AppendLine($"Work Item {workItemId} Details:");
            formattedResult.AppendLine($"- Title: {workItem.Fields["System.Title"]}");
            formattedResult.AppendLine($"- State: {workItem.Fields["System.State"]}");
            
            if (workItem.Fields.ContainsKey("System.Description"))
            {
                var description = workItem.Fields["System.Description"]?.ToString() ?? "";
                // Trim description length if too long
                if (description.Length > 500)
                {
                    description = description.Substring(0, 500) + "... (description truncated)";
                }
                formattedResult.AppendLine($"- Description: {description}");
            }
            
            if (workItem.Fields.ContainsKey("System.AssignedTo"))
            {
                formattedResult.AppendLine($"- Assigned To: {workItem.Fields["System.AssignedTo"]}");
            }
            
            if (workItem.Fields.ContainsKey("System.WorkItemType"))
            {
                formattedResult.AppendLine($"- Type: {workItem.Fields["System.WorkItemType"]}");
            }
            
            return formattedResult.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting work item details for work item {WorkItemId}", workItemId);
            return $"Error getting work item details: {ex.Message}";
        }
    }
}