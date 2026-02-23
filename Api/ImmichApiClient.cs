using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ImmichAlbums.Api;

public class ImmichApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public ImmichApiClient(string baseUrl, string apiToken, ILogger logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiToken);
    }

    public async Task<List<ImmichAlbum>> GetAlbumsAsync(bool includeShared, CancellationToken cancellationToken)
    {
        var albums = new List<ImmichAlbum>();

        var owned = await _httpClient.GetFromJsonAsync<List<ImmichAlbum>>("/api/albums", cancellationToken).ConfigureAwait(false);
        if (owned != null)
            albums.AddRange(owned);

        if (includeShared)
        {
            var shared = await _httpClient.GetFromJsonAsync<List<ImmichAlbum>>("/api/albums?shared=true", cancellationToken).ConfigureAwait(false);
            if (shared != null)
            {
                foreach (var album in shared)
                {
                    if (!albums.Exists(a => a.Id == album.Id))
                        albums.Add(album);
                }
            }
        }

        _logger.LogInformation("Fetched {Count} albums from Immich", albums.Count);
        return albums;
    }

    public async Task<ImmichAlbum?> GetAlbumDetailAsync(string albumId, CancellationToken cancellationToken)
    {
        return await _httpClient.GetFromJsonAsync<ImmichAlbum>("/api/albums/" + albumId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/server/ping", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Immich API");
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
