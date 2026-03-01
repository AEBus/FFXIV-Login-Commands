using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVLoginCommands.Windows;

namespace FFXIVLoginCommands;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/ffxivlogincommands";
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LoginRetryInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LoginRetryTimeout = TimeSpan.FromSeconds(20);

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("FFXIVLoginCommands");
    private MainWindow MainWindow { get; init; }

    private readonly List<ExecutionEntry> executionPlan = new();
    private readonly List<ExecutionEntry> pendingQueue = new();
    private readonly HashSet<Guid> sessionExecutedCommands = new();
    private bool configSavePending;
    private DateTime saveNotBeforeUtc;
    private bool loginPlanPending;
    private DateTime loginRetryDeadlineUtc;
    private DateTime nextLoginRetryUtc;

    public string ActiveCharacterDisplay { get; private set; } = "Not logged in";

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (SettingsSanitizer.NormalizeConfiguration(Configuration))
        {
            QueueConfigurationSave(immediate: true);
            SaveConfigurationNow();
        }

        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open FFXIV Login Commands"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;
        Framework.Update += OnFrameworkUpdate;

        if (ClientState.IsLoggedIn)
        {
            ScheduleLoginPlanBuild();
        }

        Log.Information($"===FFXIV Login Commands loaded ({PluginInterface.Manifest.Name})===");
    }

    public void Dispose()
    {
        SaveConfigurationNow();

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        ClientState.Login -= OnLogin;
        ClientState.Logout -= OnLogout;
        Framework.Update -= OnFrameworkUpdate;

        WindowSystem.RemoveAllWindows();

        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    public IReadOnlyList<ExecutionEntry> ExecutionPlan => executionPlan;
    public IReadOnlyList<ExecutionEntry> PendingQueue => pendingQueue;

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    public void ToggleConfigUi() => MainWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();

    private void OnLogin()
    {
        ScheduleLoginPlanBuild();
    }

    private void OnLogout(int type, int code)
    {
        ActiveCharacterDisplay = "Not logged in";
        executionPlan.Clear();
        pendingQueue.Clear();
        sessionExecutedCommands.Clear();
        loginPlanPending = false;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var utcNow = DateTime.UtcNow;
        if (loginPlanPending && utcNow >= nextLoginRetryUtc)
        {
            var completed = TryHandleLogin(logIfNotReady: utcNow >= loginRetryDeadlineUtc);
            if (!completed)
            {
                nextLoginRetryUtc = utcNow.Add(LoginRetryInterval);
                if (utcNow >= loginRetryDeadlineUtc)
                {
                    loginPlanPending = false;
                }
            }
        }

        if (configSavePending && utcNow >= saveNotBeforeUtc)
        {
            SaveConfigurationNow();
        }

        if (pendingQueue.Count == 0)
        {
            return;
        }

        var next = pendingQueue[0];
        if (utcNow < next.ScheduledUtc)
        {
            return;
        }

        pendingQueue.RemoveAt(0);
        ExecuteEntry(next);
    }

    private void ScheduleLoginPlanBuild()
    {
        loginPlanPending = true;
        nextLoginRetryUtc = DateTime.UtcNow;
        loginRetryDeadlineUtc = nextLoginRetryUtc.Add(LoginRetryTimeout);
    }

    private bool TryHandleLogin(bool logIfNotReady)
    {
        var characterInfo = GetCurrentCharacterInfo();
        if (characterInfo == null)
        {
            if (logIfNotReady)
            {
                Log.Warning("Login detected but character info is not ready.");
            }

            return false;
        }

        ActiveCharacterDisplay = $"{characterInfo.Value.Name} @ {characterInfo.Value.WorldName}";
        BuildExecutionPlan(characterInfo.Value);
        loginPlanPending = false;
        return true;
    }

    private (string Name, ushort WorldId, string WorldName)? GetCurrentCharacterInfo()
    {
        if (!PlayerState.IsLoaded)
        {
            return null;
        }

        var name = PlayerState.CharacterName;
        var worldId = (ushort)PlayerState.HomeWorld.RowId;
        var worldName = PlayerState.HomeWorld.Value.Name.ToString();
        return (name, worldId, worldName);
    }

    public bool TryGetCurrentCharacterInfo(out (string Name, ushort WorldId, string WorldName) info)
    {
        var result = GetCurrentCharacterInfo();
        if (result == null)
        {
            info = default;
            return false;
        }

        info = result.Value;
        return true;
    }

    private void BuildExecutionPlan((string Name, ushort WorldId, string WorldName) characterInfo)
    {
        executionPlan.Clear();
        pendingQueue.Clear();

        var profile = FindProfile(characterInfo.Name, characterInfo.WorldId, characterInfo.WorldName);
        if (profile != null && profile.WorldId != characterInfo.WorldId)
        {
            profile.WorldId = characterInfo.WorldId;
            QueueConfigurationSave();
        }
        var worldDisplay = string.IsNullOrWhiteSpace(characterInfo.WorldName)
            ? $"World {characterInfo.WorldId}"
            : characterInfo.WorldName;
        var characterKey = $"{characterInfo.Name}@{worldDisplay}";

        var entries = new List<CommandEntry>();
        entries.AddRange(Configuration.GlobalCommands);
        if (profile?.Enabled == true)
        {
            entries.AddRange(profile.Commands);
        }

        var scheduledTime = DateTime.UtcNow;
        var sequence = 0;

        foreach (var command in entries)
        {
            var entry = new ExecutionEntry
            {
                SequenceIndex = sequence++,
                CharacterKey = characterKey,
                Command = command,
                ScheduledUtc = scheduledTime
            };

            if (!command.Enabled)
            {
                entry.Status = CommandStatus.Skipped;
                entry.Message = "Disabled";
                executionPlan.Add(entry);
                continue;
            }

            if (string.IsNullOrWhiteSpace(command.CommandText))
            {
                entry.Status = CommandStatus.Skipped;
                entry.Message = "Empty command";
                executionPlan.Add(entry);
                continue;
            }

            if (command.RunMode == CommandRunMode.OncePerSession && sessionExecutedCommands.Contains(command.Id))
            {
                entry.Status = CommandStatus.Skipped;
                entry.Message = "Already sent this session";
                WriteExecutionLog(entry);
                executionPlan.Add(entry);
                continue;
            }

            scheduledTime = scheduledTime.AddMilliseconds(Math.Clamp(command.DelayMs, 0, SettingsSanitizer.MaxDelayMs));
            entry.ScheduledUtc = scheduledTime;
            entry.Status = CommandStatus.Pending;
            executionPlan.Add(entry);
            pendingQueue.Add(entry);
        }
    }

    private Profile? FindProfile(string name, ushort worldId, string worldName)
    {
        return Configuration.Profiles.FirstOrDefault(profile =>
            profile.Enabled &&
            string.Equals(profile.CharacterName, name, StringComparison.OrdinalIgnoreCase) &&
            (!string.IsNullOrWhiteSpace(profile.WorldName)
                ? string.Equals(profile.WorldName, worldName, StringComparison.OrdinalIgnoreCase)
                : profile.WorldId == worldId));
    }

    private void ExecuteEntry(ExecutionEntry entry)
    {
        try
        {
            CommandManager.ProcessCommand(entry.Command.CommandText);
            entry.Status = CommandStatus.Sent;
            entry.Message = "Sent";

            if (entry.Command.RunMode == CommandRunMode.OncePerSession)
            {
                sessionExecutedCommands.Add(entry.Command.Id);
            }
        }
        catch (Exception ex)
        {
            entry.Status = CommandStatus.Error;
            entry.Message = ex.Message;
        }

        WriteExecutionLog(entry);
    }

    public void RunEntryNow(ExecutionEntry entry)
    {
        if (entry.Status != CommandStatus.Pending)
        {
            return;
        }

        pendingQueue.Remove(entry);
        entry.ScheduledUtc = DateTime.UtcNow;
        ExecuteEntry(entry);
    }

    public void SkipEntry(ExecutionEntry entry, string reason)
    {
        if (entry.Status != CommandStatus.Pending)
        {
            return;
        }

        pendingQueue.Remove(entry);
        entry.Status = CommandStatus.Skipped;
        entry.Message = reason;
        WriteExecutionLog(entry);
    }

    public void ClearPendingQueue()
    {
        foreach (var entry in pendingQueue)
        {
            entry.Status = CommandStatus.Skipped;
            entry.Message = "Cleared";
            WriteExecutionLog(entry);
        }

        pendingQueue.Clear();
    }

    private void WriteExecutionLog(ExecutionEntry entry)
    {
        if (!Configuration.EnableXlLogOutput)
        {
            return;
        }

        var text = $"[{entry.CharacterKey}] #{entry.SequenceIndex} '{entry.Command.Name}': {entry.Command.CommandText} -> {entry.Status} ({entry.Message})";
        switch (entry.Status)
        {
            case CommandStatus.Error:
                Log.Error(text);
                break;
            case CommandStatus.Skipped:
                Log.Warning(text);
                break;
            default:
                Log.Information(text);
                break;
        }
    }

    public void QueueConfigurationSave(bool immediate = false)
    {
        configSavePending = true;
        saveNotBeforeUtc = immediate ? DateTime.UtcNow : DateTime.UtcNow.Add(SaveDebounce);
    }

    public void SaveConfigurationNow()
    {
        if (!configSavePending)
        {
            return;
        }

        Configuration.Save();
        configSavePending = false;
    }

    public sealed class ExecutionEntry
    {
        public int SequenceIndex { get; set; }
        public string CharacterKey { get; set; } = string.Empty;
        public CommandEntry Command { get; set; } = new();
        public DateTime ScheduledUtc { get; set; } = DateTime.UtcNow;
        public CommandStatus Status { get; set; } = CommandStatus.Pending;
        public string Message { get; set; } = string.Empty;
    }
}
