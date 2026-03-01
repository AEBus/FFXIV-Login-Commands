using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using OtterGui.Raii;

namespace FFXIVLoginCommands.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly JsonSerializerOptions jsonOptions;

    private int selectedProfileIndex = -1;
    private int selectedTargetIndex = 0;
    private string importExportText = string.Empty;
    private string importExportStatus = string.Empty;
    private static float UiScale(float value) => value * ImGuiHelpers.GlobalScale;

    public MainWindow(Plugin plugin)
        : base("FFXIV Login Commands##MainWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(900f, 620f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text($"Active character: {plugin.ActiveCharacterDisplay}");
        ImGui.Separator();

        using var tabBar = ImRaii.TabBar("FFXIVLoginCommandsTabs");
        if (!tabBar)
        {
            return;
        }

        using (var overviewTab = ImRaii.TabItem("Overview"))
        {
            if (overviewTab)
            {
                DrawOverviewTab();
            }
        }

        using (var profilesTab = ImRaii.TabItem("Profiles"))
        {
            if (profilesTab)
            {
                DrawProfilesTab();
            }
        }

        using (var commandsTab = ImRaii.TabItem("Commands"))
        {
            if (commandsTab)
            {
                DrawCommandsTab();
            }
        }

        using (var executionTab = ImRaii.TabItem("Execution"))
        {
            if (executionTab)
            {
                DrawExecutionTab();
            }
        }

        using (var importExportTab = ImRaii.TabItem("Import/Export"))
        {
            if (importExportTab)
            {
                DrawImportExportTab();
            }
        }

        using (var devTab = ImRaii.TabItem("Dev"))
        {
            if (devTab)
            {
                DrawDevTab();
            }
        }

        using (var aboutTab = ImRaii.TabItem("About"))
        {
            if (aboutTab)
            {
                DrawAboutTab();
            }
        }
    }

    private void DrawOverviewTab()
    {
        var configuration = plugin.Configuration;

        ImGui.Text("Quick setup");
        ImGui.BulletText("Create one profile per character.");
        ImGui.BulletText("Add global or profile commands in order.");
        ImGui.BulletText("Use Execution tab to run/skip pending actions.");
        ImGui.Separator();

        ImGui.Text($"Profiles: {configuration.Profiles.Count}");
        ImGui.Text($"Global commands: {configuration.GlobalCommands.Count}");
        ImGui.Text($"Pending commands right now: {plugin.PendingQueue.Count}");
    }

    private void DrawProfilesTab()
    {
        var configuration = plugin.Configuration;
        ImGui.Text("Profiles for specific characters.");
        ImGui.Separator();

        if (ImGui.Button("Add Profile"))
        {
            var profile = new Profile();
            if (plugin.TryGetCurrentCharacterInfo(out var newProfileInfo))
            {
                profile.CharacterName = newProfileInfo.Name;
                profile.WorldId = newProfileInfo.WorldId;
                profile.WorldName = newProfileInfo.WorldName;
                profile.Label = $"{newProfileInfo.Name} ({newProfileInfo.WorldName})";
            }

            configuration.Profiles.Add(profile);
            selectedProfileIndex = configuration.Profiles.Count - 1;
            plugin.QueueConfigurationSave();
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete Profile") && selectedProfileIndex >= 0 && selectedProfileIndex < configuration.Profiles.Count)
        {
            configuration.Profiles.RemoveAt(selectedProfileIndex);
            selectedProfileIndex = Math.Min(selectedProfileIndex, configuration.Profiles.Count - 1);
            plugin.QueueConfigurationSave();
        }

        ImGui.Spacing();

        for (var i = 0; i < configuration.Profiles.Count; i++)
        {
            var profile = configuration.Profiles[i];
            var label = string.IsNullOrWhiteSpace(profile.Label) ? $"Profile {i + 1}" : profile.Label;
            var suffix = string.IsNullOrWhiteSpace(profile.CharacterName) ? string.Empty : $" - {profile.CharacterName}";
            if (ImGui.Selectable($"{label}{suffix}", selectedProfileIndex == i))
            {
                selectedProfileIndex = i;
            }
        }

        ImGui.Separator();

        if (selectedProfileIndex < 0 || selectedProfileIndex >= configuration.Profiles.Count)
        {
            ImGui.Text("Select a profile to edit.");
            return;
        }

        var selected = configuration.Profiles[selectedProfileIndex];
        var enabled = selected.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            selected.Enabled = enabled;
            plugin.QueueConfigurationSave();
        }

        var labelText = selected.Label;
        if (ImGui.InputText("Label", ref labelText, 120))
        {
            selected.Label = labelText;
            plugin.QueueConfigurationSave();
        }

        var nameText = selected.CharacterName;
        if (ImGui.InputText("Character Name", ref nameText, 120))
        {
            selected.CharacterName = nameText;
            plugin.QueueConfigurationSave();
        }

        var worldName = selected.WorldName;
        if (ImGui.InputText("World Name", ref worldName, 120))
        {
            selected.WorldName = worldName;
            selected.WorldId = 0;
            plugin.QueueConfigurationSave();
        }

        if (ImGui.Button("Use Current Character") && plugin.TryGetCurrentCharacterInfo(out var currentInfo))
        {
            selected.CharacterName = currentInfo.Name;
            selected.WorldId = currentInfo.WorldId;
            selected.WorldName = currentInfo.WorldName;
            if (string.IsNullOrWhiteSpace(selected.Label))
            {
                selected.Label = $"{currentInfo.Name} ({currentInfo.WorldName})";
            }

            plugin.QueueConfigurationSave();
        }
    }

    private void DrawCommandsTab()
    {
        var configuration = plugin.Configuration;
        var targetLabels = new List<string> { "Global Commands" };
        foreach (var profile in configuration.Profiles)
        {
            targetLabels.Add(string.IsNullOrWhiteSpace(profile.Label) ? "Unnamed Profile" : profile.Label);
        }

        selectedTargetIndex = Math.Clamp(selectedTargetIndex, 0, targetLabels.Count - 1);

        ImGui.Text("Configure command order, delay, and execution mode.");
        ImGui.Separator();

        ImGui.Combo("Target", ref selectedTargetIndex, targetLabels.ToArray(), targetLabels.Count);

        List<CommandEntry> commandList;
        if (selectedTargetIndex == 0)
        {
            commandList = configuration.GlobalCommands;
            ImGui.Text("Editing global commands.");
        }
        else
        {
            var profileIndex = selectedTargetIndex - 1;
            if (profileIndex < 0 || profileIndex >= configuration.Profiles.Count)
            {
                ImGui.Text("No profile selected.");
                return;
            }

            var profile = configuration.Profiles[profileIndex];
            commandList = profile.Commands;
            ImGui.Text($"Editing commands for: {profile.Label}");
        }

        if (ImGui.Button("Add Command"))
        {
            commandList.Add(new CommandEntry
            {
                Name = "New Command",
                CommandText = string.Empty,
                DelayMs = 0,
                RunMode = CommandRunMode.EveryLogin,
                Enabled = true
            });
            plugin.QueueConfigurationSave();
        }

        ImGui.Spacing();

        using (var commandsTable = ImRaii.Table("CommandsTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            if (!commandsTable)
            {
                return;
            }

            ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, UiScale(70f));
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, UiScale(170f));
            ImGui.TableSetupColumn("Command", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Delay (ms)", ImGuiTableColumnFlags.WidthFixed, UiScale(90f));
            ImGui.TableSetupColumn("Run Mode", ImGuiTableColumnFlags.WidthFixed, UiScale(120f));
            ImGui.TableSetupColumn("Order", ImGuiTableColumnFlags.WidthFixed, UiScale(80f));
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, UiScale(80f));
            ImGui.TableHeadersRow();

            for (var i = 0; i < commandList.Count; i++)
            {
                var command = commandList[i];
                using var rowId = ImRaii.PushId(i);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                var enabled = command.Enabled;
                if (ImGui.Checkbox("##enabled", ref enabled))
                {
                    command.Enabled = enabled;
                    plugin.QueueConfigurationSave();
                }

                ImGui.TableNextColumn();
                var name = command.Name;
                if (ImGui.InputText("##name", ref name, 120))
                {
                    command.Name = name;
                    plugin.QueueConfigurationSave();
                }

                ImGui.TableNextColumn();
                var commandText = command.CommandText;
                if (ImGui.InputText("##command", ref commandText, 512))
                {
                    command.CommandText = commandText;
                    plugin.QueueConfigurationSave();
                }

                ImGui.TableNextColumn();
                var delayMs = command.DelayMs;
                if (ImGui.InputInt("##delay", ref delayMs))
                {
                    command.DelayMs = Math.Clamp(delayMs, 0, SettingsSanitizer.MaxDelayMs);
                    plugin.QueueConfigurationSave();
                }

                ImGui.TableNextColumn();
                var runModeIndex = command.RunMode == CommandRunMode.EveryLogin ? 0 : 1;
                var runModeOptions = new[] { "Every Login", "Once Per Session" };
                if (ImGui.Combo("##runmode", ref runModeIndex, runModeOptions, runModeOptions.Length))
                {
                    command.RunMode = runModeIndex == 0 ? CommandRunMode.EveryLogin : CommandRunMode.OncePerSession;
                    plugin.QueueConfigurationSave();
                }

                ImGui.TableNextColumn();
                if (ImGui.Button("Up") && i > 0)
                {
                    (commandList[i - 1], commandList[i]) = (commandList[i], commandList[i - 1]);
                    plugin.QueueConfigurationSave();
                }
                ImGui.SameLine();
                if (ImGui.Button("Down") && i < commandList.Count - 1)
                {
                    (commandList[i + 1], commandList[i]) = (commandList[i], commandList[i + 1]);
                    plugin.QueueConfigurationSave();
                }

                ImGui.TableNextColumn();
                if (ImGui.Button("Delete"))
                {
                    commandList.RemoveAt(i);
                    plugin.QueueConfigurationSave();
                    break;
                }
            }
        }
    }

    private void DrawExecutionTab()
    {
        var pendingCount = plugin.PendingQueue.Count;
        ImGui.Text($"Pending commands: {pendingCount}");

        if (ImGui.Button("Run Next Now") && pendingCount > 0)
        {
            plugin.RunEntryNow(plugin.PendingQueue[0]);
        }
        ImGui.SameLine();
        if (ImGui.Button("Skip Next") && pendingCount > 0)
        {
            plugin.SkipEntry(plugin.PendingQueue[0], "Skipped by user");
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear Pending"))
        {
            plugin.ClearPendingQueue();
        }

        ImGui.Spacing();

        using (var executionTable = ImRaii.Table("ExecutionTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            if (!executionTable)
            {
                return;
            }

            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, UiScale(40f));
            ImGui.TableSetupColumn("Command", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Delay (ms)", ImGuiTableColumnFlags.WidthFixed, UiScale(90f));
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, UiScale(90f));
            ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, UiScale(100f));
            ImGui.TableHeadersRow();

            foreach (var entry in new List<Plugin.ExecutionEntry>(plugin.ExecutionPlan))
            {
                using var rowId = ImRaii.PushId(entry.SequenceIndex);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(entry.SequenceIndex.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(entry.Command.CommandText);
                ImGui.TableNextColumn();
                ImGui.Text(entry.Command.DelayMs.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(entry.Status.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(entry.Message);
                ImGui.TableNextColumn();

                if (entry.Status == CommandStatus.Pending)
                {
                    if (ImGui.Button("Run"))
                    {
                        plugin.RunEntryNow(entry);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Skip"))
                    {
                        plugin.SkipEntry(entry, "Skipped by user");
                    }
                }
                else
                {
                    ImGui.Text("-");
                }
            }
        }
    }

    private void DrawImportExportTab()
    {
        var configuration = plugin.Configuration;
        ImGui.Text("Import or export profiles and global commands as JSON.");
        ImGui.Text("Import only trusted JSON. Imported commands are sanitized before saving.");
        ImGui.Separator();

        if (ImGui.Button("Export Settings"))
        {
            var export = new SettingsExport
            {
                Profiles = configuration.Profiles,
                GlobalCommands = configuration.GlobalCommands
            };

            importExportText = JsonSerializer.Serialize(export, jsonOptions);
            importExportStatus = "Exported settings.";
        }

        ImGui.SameLine();
        if (ImGui.Button("Import Settings"))
        {
            try
            {
                var imported = JsonSerializer.Deserialize<SettingsExport>(importExportText, jsonOptions);
                if (imported == null)
                {
                    importExportStatus = "Import failed: invalid JSON.";
                }
                else
                {
                    var normalized = SettingsSanitizer.NormalizeImported(imported, out var importedProfiles, out var importedCommands);
                    configuration.Profiles = normalized.Profiles;
                    configuration.GlobalCommands = normalized.GlobalCommands;
                    plugin.QueueConfigurationSave(immediate: true);
                    importExportStatus = $"Import complete. Profiles: {importedProfiles}, Commands: {importedCommands}.";
                }
            }
            catch (Exception ex)
            {
                importExportStatus = $"Import failed: {ex.Message}";
            }
        }

        ImGui.Text(importExportStatus);
        ImGui.InputTextMultiline("##importexport", ref importExportText, 20000, new Vector2(-1f, UiScale(360f)));
    }

    private void DrawAboutTab()
    {
        ImGui.Text("FFXIV Login Commands");
        ImGui.Text($"Version: {Plugin.PluginInterface.Manifest.AssemblyVersion}");
        ImGui.Text($"Author: {Plugin.PluginInterface.Manifest.Author}");
        ImGui.Separator();
        ImGui.Text("Repository: https://github.com/AEBus/FFXIV-Login-Commands");
        ImGui.Text("Documentation: https://github.com/AEBus/FFXIV-Login-Commands");
    }

    private void DrawDevTab()
    {
        var configuration = plugin.Configuration;

        ImGui.Text("Developer options");
        ImGui.Separator();

        var xlLogOutput = configuration.EnableXlLogOutput;
        if (ImGui.Checkbox("Write execution logs to XL log", ref xlLogOutput))
        {
            configuration.EnableXlLogOutput = xlLogOutput;
            plugin.QueueConfigurationSave();
        }

        ImGui.TextDisabled("When disabled, command execution is not logged in XL log.");
    }
}
