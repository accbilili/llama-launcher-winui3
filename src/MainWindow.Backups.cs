using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;

namespace LlamaLauncher;

internal sealed partial class MainWindow
{
    private readonly StackPanel backupPane = new() { Spacing = 10 };
    private readonly ComboBox backupPicker = new() { Header = "WorkBuddy 配置备份", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock backupDetails = Text("", 12, true);
    private readonly Button restoreBackup = ActionButton("恢复选中备份", Symbol.Undo);
    private Border? backupCard;
    private FrameworkElement BuildBackupPane()
    {
        backupPane.Children.Add(Text("配置恢复", 17)); backupPane.Children.Add(backupPicker); backupPane.Children.Add(backupDetails);
        backupPicker.SelectionChanged += (_, _) =>
        {
            restoreBackup.IsEnabled = !busy && backupPicker.SelectedItem is ComboBoxItem;
            backupDetails.Text = backupPicker.SelectedItem is ComboBoxItem { Tag: string path } ? path + "\n恢复前会再次备份当前配置，可撤回恢复操作。" : "暂无备份。写入已有配置后会自动创建备份。";
        };
        restoreBackup.Click += async (_, _) =>
        {
            if (backupPicker.SelectedItem is not ComboBoxItem { Tag: string path }) return;
            await RunAction(async () =>
            {
                string saved = await Task.Run(() => ClientConnection.RestoreWorkBuddy(ClientConnection.WorkBuddyPath, path));
                RefreshBackups();
                ShowNotice("WorkBuddy 配置已恢复", "请在 WorkBuddy 重新选择模型。" + (saved.Length > 0 ? "\n恢复前的配置已备份：" + saved : ""), InfoBarSeverity.Success);
            });
        };
        var folder = ActionButton("打开备份位置", Symbol.Folder); folder.Click += (_, _) => OpenFolder(Path.GetDirectoryName(ClientConnection.WorkBuddyPath)!);
        var refresh = ActionButton("刷新备份", Symbol.Refresh); refresh.Click += (_, _) => RefreshBackups();
        backupPane.Children.Add(Fields(restoreBackup, folder, refresh));
        backupCard = Card(backupPane); return backupCard;
    }
    private void RefreshBackups()
    {
        if (backupCard is not null) backupCard.Visibility = CurrentClient == LocalClient.WorkBuddy ? Visibility.Visible : Visibility.Collapsed;
        if (CurrentClient != LocalClient.WorkBuddy) return;
        try
        {
            string? selectedPath = (backupPicker.SelectedItem as ComboBoxItem)?.Tag as string;
            backupPicker.Items.Clear();
            foreach (var file in ClientConnection.WorkBuddyBackups(ClientConnection.WorkBuddyPath))
            {
                string stamp = file.Name[(Path.GetFileName(ClientConnection.WorkBuddyPath).Length + ".backup-".Length)..];
                string label = stamp.Length >= 19 && DateTime.TryParseExact(stamp[..19], "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                    ? time.ToString("yyyy-MM-dd HH:mm:ss.fff") : file.Name;
                var item = new ComboBoxItem { Content = label + $" · {file.Length:N0} 字节", Tag = file.FullName }; backupPicker.Items.Add(item);
                if (file.FullName == selectedPath) backupPicker.SelectedItem = item;
            }
            if (backupPicker.SelectedItem is null && backupPicker.Items.Count > 0) backupPicker.SelectedIndex = 0;
            if (backupPicker.Items.Count == 0) backupDetails.Text = "暂无备份。写入已有配置后会自动创建备份。";
        }
        catch (Exception ex) { backupDetails.Text = "无法读取备份：" + Errors.Explain(ex); }
    }
}
