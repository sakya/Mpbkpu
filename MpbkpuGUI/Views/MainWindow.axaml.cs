using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MpbkpuGUI.Views
{
    public class MainWindow : BaseView
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected sealed override void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            var list = this.FindControl<ListBox>("JobList");
            list.Items = App.Settings!.Jobs.OrderBy(i => i.Name);

            base.InitializeComponent();
        }
    }
}
