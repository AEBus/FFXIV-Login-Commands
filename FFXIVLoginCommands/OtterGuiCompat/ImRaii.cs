using Dalamud.Bindings.ImGui;

namespace OtterGui.Raii;

// Lightweight compatibility layer so this plugin can use OtterGui-style RAII calls
// without requiring the full OtterGui project in CI builds.
public static class ImRaii
{
    public static Dalamud.Interface.Utility.Raii.ImRaii.IEndObject TabBar(string label)
        => Dalamud.Interface.Utility.Raii.ImRaii.TabBar(label);

    public static Dalamud.Interface.Utility.Raii.ImRaii.IEndObject TabBar(string label, ImGuiTabBarFlags flags)
        => Dalamud.Interface.Utility.Raii.ImRaii.TabBar(label, flags);

    public static Dalamud.Interface.Utility.Raii.ImRaii.IEndObject TabItem(string label)
        => Dalamud.Interface.Utility.Raii.ImRaii.TabItem(label);

    public static Dalamud.Interface.Utility.Raii.ImRaii.IEndObject Table(string table, int numColumns, ImGuiTableFlags flags)
        => Dalamud.Interface.Utility.Raii.ImRaii.Table(table, numColumns, flags);

    public static Dalamud.Interface.Utility.Raii.ImRaii.Id PushId(int id, bool enabled = true)
        => Dalamud.Interface.Utility.Raii.ImRaii.PushId(id, enabled);
}
