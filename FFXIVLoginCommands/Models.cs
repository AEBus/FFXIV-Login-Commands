using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXIVLoginCommands;

public enum CommandRunMode
{
    EveryLogin = 0,
    OncePerSession = 1
}

public enum CommandStatus
{
    Pending = 0,
    Sent = 1,
    Skipped = 2,
    Error = 3
}

[Serializable]
public sealed class CommandEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string CommandText { get; set; } = string.Empty;
    public int DelayMs { get; set; } = 0;
    public CommandRunMode RunMode { get; set; } = CommandRunMode.EveryLogin;
    public bool Enabled { get; set; } = true;
}

[Serializable]
public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "New Profile";
    public string CharacterName { get; set; } = string.Empty;
    public ushort WorldId { get; set; } = 0;
    public string WorldName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<CommandEntry> Commands { get; set; } = new();
}

[Serializable]
public sealed class LogEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string CharacterKey { get; set; } = string.Empty;
    public string CommandText { get; set; } = string.Empty;
    public CommandStatus Status { get; set; } = CommandStatus.Pending;
    public string Message { get; set; } = string.Empty;
}

[Serializable]
public sealed class SettingsExport
{
    public List<Profile> Profiles { get; set; } = new();
    public List<CommandEntry> GlobalCommands { get; set; } = new();
}

public static class SettingsSanitizer
{
    public const int MaxDelayMs = 10 * 60 * 1000;

    public static bool NormalizeConfiguration(Configuration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var changed = false;

        configuration.Profiles ??= new List<Profile>();
        configuration.GlobalCommands ??= new List<CommandEntry>();
        configuration.Logs ??= new List<LogEntry>();

        changed |= NormalizeCommands(configuration.GlobalCommands);
        changed |= NormalizeProfiles(configuration.Profiles);
        changed |= NormalizeLogs(configuration.Logs);

        return changed;
    }

    public static SettingsExport NormalizeImported(SettingsExport imported, out int profileCount, out int commandCount)
    {
        imported.Profiles ??= new List<Profile>();
        imported.GlobalCommands ??= new List<CommandEntry>();

        NormalizeProfiles(imported.Profiles);
        NormalizeCommands(imported.GlobalCommands);

        profileCount = imported.Profiles.Count;
        commandCount = imported.GlobalCommands.Count + imported.Profiles.Sum(profile => profile.Commands.Count);
        return imported;
    }

    private static bool NormalizeProfiles(List<Profile> profiles)
    {
        var changed = false;
        var profileIds = new HashSet<Guid>();

        for (var i = profiles.Count - 1; i >= 0; i--)
        {
            var profile = profiles[i];
            if (profile == null)
            {
                profiles.RemoveAt(i);
                changed = true;
                continue;
            }

            if (profile.Id == Guid.Empty || !profileIds.Add(profile.Id))
            {
                profile.Id = Guid.NewGuid();
                changed = true;
            }

            var normalizedLabel = (profile.Label ?? string.Empty).Trim();
            if (!string.Equals(profile.Label, normalizedLabel, StringComparison.Ordinal))
            {
                profile.Label = normalizedLabel;
                changed = true;
            }

            var normalizedName = (profile.CharacterName ?? string.Empty).Trim();
            if (!string.Equals(profile.CharacterName, normalizedName, StringComparison.Ordinal))
            {
                profile.CharacterName = normalizedName;
                changed = true;
            }

            var normalizedWorldName = (profile.WorldName ?? string.Empty).Trim();
            if (!string.Equals(profile.WorldName, normalizedWorldName, StringComparison.Ordinal))
            {
                profile.WorldName = normalizedWorldName;
                changed = true;
            }

            profile.Commands ??= new List<CommandEntry>();
            if (NormalizeCommands(profile.Commands))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeCommands(List<CommandEntry> commands)
    {
        var changed = false;
        var commandIds = new HashSet<Guid>();

        for (var i = commands.Count - 1; i >= 0; i--)
        {
            var command = commands[i];
            if (command == null)
            {
                commands.RemoveAt(i);
                changed = true;
                continue;
            }

            if (command.Id == Guid.Empty || !commandIds.Add(command.Id))
            {
                command.Id = Guid.NewGuid();
                changed = true;
            }

            var normalizedName = (command.Name ?? string.Empty).Trim();
            if (!string.Equals(command.Name, normalizedName, StringComparison.Ordinal))
            {
                command.Name = normalizedName;
                changed = true;
            }

            var normalizedCommandText = (command.CommandText ?? string.Empty).Trim();
            if (!string.Equals(command.CommandText, normalizedCommandText, StringComparison.Ordinal))
            {
                command.CommandText = normalizedCommandText;
                changed = true;
            }

            var clampedDelay = Math.Clamp(command.DelayMs, 0, MaxDelayMs);
            if (command.DelayMs != clampedDelay)
            {
                command.DelayMs = clampedDelay;
                changed = true;
            }

            if (!Enum.IsDefined(typeof(CommandRunMode), command.RunMode))
            {
                command.RunMode = CommandRunMode.EveryLogin;
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeLogs(List<LogEntry> logs)
    {
        var changed = false;
        for (var i = logs.Count - 1; i >= 0; i--)
        {
            var log = logs[i];
            if (log == null)
            {
                logs.RemoveAt(i);
                changed = true;
                continue;
            }

            log.CharacterKey ??= string.Empty;
            log.CommandText ??= string.Empty;
            log.Message ??= string.Empty;

            if (!Enum.IsDefined(typeof(CommandStatus), log.Status))
            {
                log.Status = CommandStatus.Error;
                changed = true;
            }
        }

        return changed;
    }
}
