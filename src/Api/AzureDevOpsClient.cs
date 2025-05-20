using Microsoft.TeamFoundation.Build.WebApi;
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

    /// <summary>
    /// Gets all pipelines from Azure DevOps
    /// </summary>
    /// <param name="project">The project name. If null, pipelines from all projects will be returned</param>
    /// <returns>A list of build definitions (pipelines)</returns>
    public async Task<List<BuildDefinition>> GetPipelinesAsync(string project = null)
    {
        try
        {
            // Connect to Azure DevOps using OAuth bearer token
            VssConnection connection = new VssConnection(
                new Uri(_organizationUrl),
                new VssOAuthAccessTokenCredential(_bearerToken));

            // Create build client
            BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();

            // Get all build definitions (pipelines)
            List<BuildDefinition> pipelines = await buildClient.GetDefinitionsAsync(
                project: project);

            return pipelines;
        }
        catch (Exception ex)
        {
            // Log the exception or handle it as needed
            Console.WriteLine($"Error retrieving pipelines: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gets details about pipeline runs
    /// </summary>
    /// <param name="pipelineId">Optional. The ID of the pipeline to get runs for. If null, runs for all pipelines will be returned</param>
    /// <param name="project">Optional. The project name. If null, runs from all projects will be returned</param>
    /// <param name="top">Optional. The number of runs to return. Default is 100</param>
    /// <returns>A list of build runs</returns>
    public async Task<List<Build>> GetPipelineRunsAsync(int? pipelineId = null, string project = null, int top = 100)
    {
        try
        {
            // Connect to Azure DevOps using OAuth bearer token
            VssConnection connection = new VssConnection(
                new Uri(_organizationUrl),
                new VssOAuthAccessTokenCredential(_bearerToken));

            // Create build client
            BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();

            // Get builds (pipeline runs)
            List<Build> builds = await buildClient.GetBuildsAsync(
                project: project, 
                definitions: pipelineId.HasValue ? new List<int> { pipelineId.Value } : null,
                top: top);

            return builds;
        }
        catch (Exception ex)
        {
            // Log the exception or handle it as needed
            Console.WriteLine($"Error retrieving pipeline runs: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Triggers a pipeline run
    /// </summary>
    /// <param name="pipelineId">The ID of the pipeline to trigger</param>
    /// <param name="project">The project name</param>
    /// <param name="branch">Optional. The branch to build. If null, the default branch will be used</param>
    /// <param name="parameters">Optional. Build parameters as a dictionary</param>
    /// <returns>The created build</returns>
    public async Task<Build> TriggerPipelineRunAsync(int pipelineId, string project, string branch = null, Dictionary<string, string> parameters = null)
    {
        try
        {
            // Connect to Azure DevOps using OAuth bearer token
            VssConnection connection = new VssConnection(
                new Uri(_organizationUrl),
                new VssOAuthAccessTokenCredential(_bearerToken));

            // Create build client
            BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();

            // Create build to queue
            Build build = new Build
            {
                Definition = new DefinitionReference
                {
                    Id = pipelineId
                },
                Project = new TeamProjectReference
                {
                    Name = project
                },
                SourceBranch = branch,
                Parameters = parameters != null ? System.Text.Json.JsonSerializer.Serialize(parameters) : null
            };

            // Queue the build
            Build createdBuild = await buildClient.QueueBuildAsync(build, project);

            return createdBuild;
        }
        catch (Exception ex)
        {
            // Log the exception or handle it as needed
            Console.WriteLine($"Error triggering pipeline run: {ex.Message}");
            throw;
        }
    }
}