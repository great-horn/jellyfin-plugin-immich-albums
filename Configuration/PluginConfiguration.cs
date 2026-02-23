using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ImmichAlbums.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string ImmichApiUrl { get; set; } = "http://localhost:2283";
    public string ImmichApiToken { get; set; } = string.Empty;
    public string SyncFolderPath { get; set; } = string.Empty;
    public int SyncIntervalHours { get; set; } = 6;
    public string ContainerUploadPath { get; set; } = string.Empty;
    public string HostUploadPath { get; set; } = string.Empty;
    public string ContainerExternalPath { get; set; } = string.Empty;
    public string HostExternalPath { get; set; } = string.Empty;
    public bool IncludeSharedAlbums { get; set; } = true;
}
