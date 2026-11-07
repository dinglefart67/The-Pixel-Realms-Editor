using System.Windows;
using ThePixelRealmsEditor.Services;

namespace ThePixelRealmsEditor
{
    public partial class App : Application
    {
        protected override void OnStartup(
            StartupEventArgs e)
        {
            EditorStorage.EnsureDirectories();

            CategoryDatabase.Load();
            AssetDatabase.Load();

            base.OnStartup(e);
        }
    }
}