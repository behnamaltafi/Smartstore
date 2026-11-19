using Smartstore.Collections;
using Smartstore.Core.Content.Menus;
using Smartstore.Web.Rendering.Builders;

namespace Smartstore.MultiSms;

/// <summary>
/// Exposes the gateway under both Configuration / Settings and installed modules.
/// </summary>
public sealed class AdminMenu : IMenuProvider
{
    public string MenuName => "admin";
    public int Ordinal => 0;

    public void BuildMenu(TreeNode<MenuItem> rootNode)
    {
        var settingsNode = rootNode.SelectNodeById("settings");
        var modulesNode = rootNode.SelectNodeById("modules");

        settingsNode?.Append(BuildItem());
        settingsNode?.Append(BuildHistoryItem());
        modulesNode?.Append(BuildItem());
    }

    private static MenuItem BuildHistoryItem()
        => new MenuItem().ToBuilder()
            .Text("SMS History")
            .ResKey("Plugins.Sms.MultiSms.HistoryTitle")
            .Icon("list")
            .PermissionNames("configuration.setting")
            .Action("History", "MultiSms", new { area = "Admin" })
            .AsItem();

    private static MenuItem BuildItem()
        => new MenuItem().ToBuilder()
            .Text("SMS Gateway")
            .ResKey("Plugins.FriendlyName.Smartstore.MultiSms")
            .Icon("message-square")
            .PermissionNames("configuration.setting")
            .Action("Configure", "MultiSms", new { area = "Admin" })
            .AsItem();
}
