using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ImmichAlbums.Api;

public class ImmichAlbum
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("albumName")]
    public string AlbumName { get; set; } = string.Empty;

    [JsonPropertyName("assetCount")]
    public int AssetCount { get; set; }

    [JsonPropertyName("assets")]
    public List<ImmichAsset> Assets { get; set; } = new();

    [JsonPropertyName("shared")]
    public bool Shared { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class ImmichAsset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("originalPath")]
    public string OriginalPath { get; set; } = string.Empty;

    [JsonPropertyName("originalFileName")]
    public string OriginalFileName { get; set; } = string.Empty;

    [JsonPropertyName("fileCreatedAt")]
    public DateTime FileCreatedAt { get; set; }
}
