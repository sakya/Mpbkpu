using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;

namespace MpbkpuGUI.Controls
{
    public partial class ItemsList : UserControl
    {
        private IEnumerable<object>? _items;
        private bool _selectable;
        private ContentControl? _selectedControl;
        private object? _selectedItem;

        private IDataTemplate? _itemTemplate;
        private ItemsRepeater? _itemsRepeater;

        public static readonly DirectProperty<ItemsList, IEnumerable<object>?> ItemsProperty =
                AvaloniaProperty.RegisterDirect<ItemsList, IEnumerable<object>?>(
                    nameof(Items),
                    o => o.Items,
                    (o, v) => o.Items = v);

        public static readonly DirectProperty<ItemsList, bool> SelectableProperty =
                AvaloniaProperty.RegisterDirect<ItemsList, bool>(
                    nameof(Selectable),
                    o => o.Selectable,
                    (o, v) => o.Selectable = v);

        public static readonly DirectProperty<ItemsList, IDataTemplate?> ItemTemplateProperty =
                AvaloniaProperty.RegisterDirect<ItemsList, IDataTemplate?>(
                    nameof(ItemTemplate),
                    o => o.ItemTemplate,
                    (o, v) => o.ItemTemplate = v);

        public static readonly DirectProperty<ItemsList, object?> SelectedItemProperty =
                AvaloniaProperty.RegisterDirect<ItemsList, object?>(
                    nameof(SelectedItem),
                    o => o.SelectedItem,
                    (o, v) => o.SelectedItem = v);

        public ItemsList()
        {
            InitializeComponent();

            this.FindControl<ScrollViewer>("ScrollViewer").DataContext = this;
            this.Focusable = true;
            this.KeyDown += OnKeyDown;

            this.AttachedToVisualTree += (s, e) =>
            {
                if (Selectable && _items?.Count() > 0) {
                    _selectedItem = _items.ElementAt(0);
                }
            };
        }

        public IEnumerable<object>? Items
        {
            get { return _items; }
            set
            {
                if (SetAndRaise(ItemsProperty, ref _items, value)) {
                    _itemsRepeater!.ItemsSource = _items;
                    if (Selectable && _items?.Count() > 0)
                        SelectedItem = _items.ElementAt(0);
                }
            }
        }

        public IDataTemplate? ItemTemplate
        {
            get { return _itemTemplate; }
            set
            {
                if (SetAndRaise(ItemTemplateProperty, ref _itemTemplate, value)) {
                }
            }
        }

        public bool Selectable
        {
            get { return _selectable; }
            set
            {
                if (SetAndRaise(SelectableProperty, ref _selectable, value)) {
                    if (!Selectable)
                        SelectedItem = null;
                }
            }
        }

        public object? SelectedItem
        {
            get { return _selectedItem; }
            set
            {
                if (SetAndRaise(SelectedItemProperty, ref _selectedItem, value)) {
                    SetSelectedItemStyle();
                    if (_selectedItem != null && _items != null)
                        BringItemIntoView(_items.ToList().IndexOf(_selectedItem));
                }
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            var selectedIndex = _selectedItem != null ? _items?.ToList().IndexOf(_selectedItem) : null;
            if (selectedIndex == null)
                return;

            var count = _items!.Count();
            switch (e.Key) {
                case Key.Down:
                case Key.FnDownArrow:
                    if (selectedIndex + 1 < count)
                        SelectedItem = _items!.ElementAt(selectedIndex.Value + 1);
                    break;
                case Key.PageDown:
                    selectedIndex += 10;
                    if (selectedIndex >= count)
                        selectedIndex = count - 1;
                    SelectedItem = _items!.ElementAt(selectedIndex.Value);
                    break;
                case Key.Up:
                case Key.FnUpArrow:
                    if (selectedIndex > 0)
                        SelectedItem = _items!.ElementAt(selectedIndex.Value - 1);
                    break;
                case Key.PageUp:
                    selectedIndex -= 10;
                    if (selectedIndex < 0)
                        selectedIndex = 0;
                    SelectedItem = _items!.ElementAt(selectedIndex.Value);
                    break;
                case Key.End:
                    selectedIndex = count - 1;
                    SelectedItem = _items!.ElementAt(selectedIndex.Value);
                    break;
                case Key.Home:
                    selectedIndex = 0;
                    SelectedItem = _items!.ElementAt(selectedIndex.Value);
                    break;
            }
        } // OnKeyDown

        private Control GetItemControl(int index)
        {
            var ctrl = _itemsRepeater!.TryGetElement(index);
            if (ctrl == null)
                ctrl = _itemsRepeater.GetOrCreateElement(index);
            if (TopLevel.GetTopLevel(this) is { } top)
                top.UpdateLayout();
            return ctrl;
        } // GetItemControl

        private void BringItemIntoView(int index)
        {
            var ctrl = GetItemControl(index);
            ctrl.BringIntoView();
        } // BringIntoView

        private void OnElementPrepared(object sender, ItemsRepeaterElementPreparedEventArgs args)
        {
            if (args.Element is ContentControl ctrl)
                SetItemBackground(ctrl);
        }

        private void OnMouseEnter(object sender, PointerEventArgs args)
        {
            if (sender is ContentControl ctrl && ctrl.DataContext != SelectedItem)
                ctrl.Background = this.FindResource("SystemControlHighlightListLowBrush") as SolidColorBrush;
        }

        private void OnMouseLeave(object sender, PointerEventArgs args)
        {
            if (sender is ContentControl ctrl && ctrl.DataContext != SelectedItem)
                ctrl.Background = new SolidColorBrush(Colors.Transparent);
        }

        private void OnItemTapped(object sender, RoutedEventArgs e)
        {
            if (!Selectable)
                return;

            var ctrl = sender as ContentControl;
            SelectedItem = ctrl?.DataContext;
        }

        private void SetSelectedItemStyle()
        {
            var selectedIndex = _selectedItem != null ? _items?.ToList().IndexOf(_selectedItem) : null;
            if (selectedIndex == null)
                return;

            var ctrl = GetItemControl(selectedIndex.Value) as ContentControl;
            if (_selectedControl != null)
                SetItemBackground(_selectedControl);
            _selectedControl = ctrl;
            if (ctrl != null)
                SetItemBackground(ctrl);
        }

        private void SetItemBackground(ContentControl ctrl)
        {
            if (ctrl.DataContext == SelectedItem) {
                ctrl.Background = this.FindResource("SystemControlHighlightListAccentLowBrush") as SolidColorBrush;
                _selectedControl = ctrl;
            } else {
                ctrl.Background = new SolidColorBrush(Colors.Transparent);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            _itemsRepeater = this.FindControl<ItemsRepeater>("ItemsRepeater");
        }
    }
}
