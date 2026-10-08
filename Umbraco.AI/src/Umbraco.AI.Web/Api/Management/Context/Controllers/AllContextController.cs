using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Umbraco.AI.Core.Contexts;
using Umbraco.AI.Web.Api.Management.Context.Models;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Core.Mapping;

namespace Umbraco.AI.Web.Api.Management.Context.Controllers;

/// <summary>
/// Controller to get all contexts.
/// </summary>
/// <remarks>
/// Does not require AI section access: the context picker is reused outside the AI section (Copilot
/// Workspace, the AI Context Picker property editor) by users without access to it. It only returns
/// name, alias and resource count; the full context stays behind AI section access.
/// </remarks>
[ApiVersion("1.0")]
public class AllContextController : ContextControllerBase
{
    private readonly IAIContextService _contextService;
    private readonly IUmbracoMapper _umbracoMapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="AllContextController"/> class.
    /// </summary>
    public AllContextController(IAIContextService contextService, IUmbracoMapper umbracoMapper)
    {
        _contextService = contextService;
        _umbracoMapper = umbracoMapper;
    }

    /// <summary>
    /// Get all contexts.
    /// </summary>
    /// <param name="filter">Optional filter to search by name or alias (case-insensitive contains).</param>
    /// <param name="skip">Number of items to skip for pagination.</param>
    /// <param name="take">Number of items to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of contexts.</returns>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(PagedViewModel<ContextItemResponseModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedViewModel<ContextItemResponseModel>>> GetAllContexts(
        string? filter = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var (contexts, total) = await _contextService.GetContextsPagedAsync(
            filter,
            skip,
            take,
            cancellationToken);

        var viewModel = new PagedViewModel<ContextItemResponseModel>
        {
            Total = total,
            Items = _umbracoMapper.MapEnumerable<AIContext, ContextItemResponseModel>(contexts)
        };

        return Ok(viewModel);
    }
}
