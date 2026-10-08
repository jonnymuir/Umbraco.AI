using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.AI.Agent.Conversations.Web.Api.Management.Common.Controllers;
using Umbraco.AI.Agent.Copilot.Workspace.Core;

namespace Umbraco.AI.Agent.Copilot.Workspace.Web.Configuration;

/// <summary>
/// Adds the backoffice security requirement to the Conversations/Projects operations in the Copilot
/// Workspace OpenAPI document.
/// </summary>
/// <remarks>
/// The shared backoffice security filter only covers actions with a compile-time
/// <c>[MapToApi]</c> attribute (it checks the method by reflection). The Conversations/Projects
/// controllers are bound to this document at runtime by <see cref="CopilotWorkspaceConversationsApiConvention"/>,
/// so that filter skips them. Without a security requirement, the generated client never attaches the
/// bearer token, and every call fails with 401.
/// </remarks>
internal sealed class CopilotWorkspaceConversationsSecurityRequirementsOperationFilter : IOperationFilter
{
    // The backoffice security scheme the CMS registers on every Management API document. Its own constant
    // (ManagementApiConfiguration.ApiSecurityName) is internal, so the value is repeated here.
    private const string BackOfficeSecuritySchemeName = "Backoffice-User";

    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var controllerType = context.MethodInfo.DeclaringType;
        if (context.DocumentName != CopilotWorkspaceConstants.ManagementApi.ApiName
            || controllerType is null
            || !typeof(ConversationsManagementControllerBase).IsAssignableFrom(controllerType)
            || operation.Security is { Count: > 0 })
        {
            return;
        }

        if (context.MethodInfo.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any()
            || controllerType.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any())
        {
            return;
        }

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd(
            StatusCodes.Status401Unauthorized.ToString(),
            new OpenApiResponse { Description = "The resource is protected and requires an authentication token" });

        // The convention also requires Copilot Workspace section access, so an authenticated user can still be refused.
        operation.Responses.TryAdd(
            StatusCodes.Status403Forbidden.ToString(),
            new OpenApiResponse { Description = "The authenticated user does not have access to this resource" });

        var schemeReference = new OpenApiSecuritySchemeReference(BackOfficeSecuritySchemeName, context.Document);
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement { [schemeReference] = [] });
    }
}
