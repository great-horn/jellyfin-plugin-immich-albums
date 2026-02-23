using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ImmichAlbums.Configuration;
using MediaBrowser.Common.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ImmichAlbums.Api;

[ApiController]
[Route("ImmichAlbums")]
[Authorize]
public class ImmichController : ControllerBase
{
    private readonly ILogger<ImmichController> _logger;

    public ImmichController(ILogger<ImmichController> logger)
    {
        _logger = logger;
    }

    [HttpGet("TestConnection")]
    [ProducesResponseType(200)]
    [ProducesResponseType(500)]
    [Produces("application/json")]
    public async Task<ActionResult<object>> TestConnection(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null || string.IsNullOrEmpty(config.ImmichApiToken))
        {
            return StatusCode(500, new { success = false, message = "Token API non configuré" });
        }

        using var client = new ImmichApiClient(config.ImmichApiUrl, config.ImmichApiToken, _logger);
        if (await client.TestConnectionAsync(cancellationToken).ConfigureAwait(false))
        {
            var albums = await client.GetAlbumsAsync(includeShared: false, cancellationToken).ConfigureAwait(false);
            return Ok(new { success = true, message = $"Connecté — {albums.Count} albums trouvés" });
        }

        return StatusCode(500, new { success = false, message = "Impossible de joindre Immich" });
    }
}
