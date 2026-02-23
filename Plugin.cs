using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.ImmichAlbums.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ImmichAlbums;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }

    public override string Name => "Immich Albums";

    public override Guid Id => new Guid("f3a2b1c0-d4e5-6f78-9a0b-c1d2e3f4a5b6");

    public override string Description => "Sync Immich photo albums into Jellyfin as photo libraries via symlinks";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        if (string.IsNullOrEmpty(Configuration.SyncFolderPath))
        {
            Configuration.SyncFolderPath = Path.Combine(applicationPaths.DataPath, "immich-albums");
        }
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        };
    }
}
