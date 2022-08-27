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
            base.InitializeComponent();
        }
    }
}
