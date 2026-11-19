using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
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
        var album = await _httpClient.GetFromJsonAsync<ImmichAlbum>("/api/albums/" + albumId, cancellationToken).ConfigureAwait(false);
        if (album == null)
            return null;

        var allAssets = new List<ImmichAsset>();
        int page = 1;

        while (true)
        {
            var payload = new
            {
                albumIds = new[] { albumId },
                page = page,
                size = 1000
            };

            var response = await _httpClient.PostAsJsonAsync("/api/search/metadata", payload, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var searchResult = await response.Content.ReadFromJsonAsync<ImmichSearchResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            var items = searchResult?.Assets?.Items;

            if (items != null && items.Count > 0)
            {
                allAssets.AddRange(items);
            }

            if (string.IsNullOrEmpty(searchResult?.Assets?.NextPage))
            {
                break;
            }

            page++;
        }

        album.Assets = allAssets;
        return album;
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

    private sealed class ImmichSearchResponse
    {
        [JsonPropertyName("assets")]
        public ImmichSearchAssetsSection? Assets { get; set; }
    }

    private sealed class ImmichSearchAssetsSection
    {
        [JsonPropertyName("items")]
        public List<ImmichAsset>? Items { get; set; }

        [JsonPropertyName("nextPage")]
        public string? NextPage { get; set; }
    }
}

