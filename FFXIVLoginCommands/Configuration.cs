using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace FFXIVLoginCommands;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool EnableXlLogOutput { get; set; } = false;
    public List<Profile> Profiles { get; set; } = new();
    public List<CommandEntry> GlobalCommands { get; set; } = new();
    public List<LogEntry> Logs { get; set; } = new();

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
