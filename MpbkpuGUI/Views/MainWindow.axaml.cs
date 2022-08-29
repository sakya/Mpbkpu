using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Pages;

namespace MpbkpuGUI.Views
{
    public class MainWindow : BaseView
    {
        Grid? _container;
        Controls.TitleBar? _titleBar;
        List<BasePage> _pageHistory = new List<BasePage>();
        Dictionary<string, BasePage.PageState> _pageStates = new Dictionary<string, BasePage.PageState>();
        bool _changingPage;

        #region classes
        public class TransitionSettings
        {
            public enum EnterTransitions
            {
                None,
                SlideLeft,
                SlideUp,
                FadeIn
            }

            public TransitionSettings(EnterTransitions transition, TimeSpan duration, Avalonia.Animation.Easings.Easing? easing = null) {
                Type = transition;
                Duration = duration;
                Easing = easing ?? new Avalonia.Animation.Easings.LinearEasing();
            }

            public EnterTransitions Type { get; set; }
            public TimeSpan Duration { get; set; }
            public Avalonia.Animation.Easings.Easing Easing { get; set; }
        } // TransitionSettings
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            WindowTitle = Title;
            Transition = new TransitionSettings(TransitionSettings.EnterTransitions.SlideLeft, TimeSpan.FromMilliseconds(250));
            BackKey = Key.Escape;

            _container = this.FindControl<Grid>("Container");
            _titleBar = this.FindControl<Controls.TitleBar>("TitleBar");

            Closing += OnWindowClosing;
            KeyDown += OnKeyDown;
        }


        #region public properties
        public TransitionSettings? Transition { get; set; }
        public Key BackKey { get; set; }

        public string? WindowTitle { get; set; }

        public BasePage? CurrentPage
        {
            get
            {
                if (_container?.Children.Count > 1)
                    return _container.Children[1] as BasePage;
                return null;
            }
        }

        public bool CanNavigateBack => _pageHistory.Count > 0;

        #endregion

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            await Task.Delay(500);
            await NavigateTo(new JobListPage());
            SetWaitAnimation(false);
        }

        protected sealed override void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            Transition = new TransitionSettings(TransitionSettings.EnterTransitions.SlideLeft, TimeSpan.FromMilliseconds(250));
            BackKey = Key.Escape;

            _container = this.FindControl<Grid>("Container");
            _titleBar = this.FindControl<Controls.TitleBar>("TitleBar");

            Closing += OnWindowClosing;
            KeyDown += OnKeyDown;

            base.InitializeComponent();
        }

       #region public operations
        /// <summary>
        /// Navigate to a new page
        /// </summary>
        /// <param name="page"></param>
        /// <returns>True on success</returns>
        public async Task<bool> NavigateTo(BasePage page)
        {
            if (_changingPage)
                return false;

            var exitingPage = CurrentPage;
            if (exitingPage != null) {
                if (!exitingPage.OnNavigatingFrom(BasePage.NavigationDirection.Forward))
                    return false;
                _pageHistory.Add(exitingPage);
            }

            await ChangePage(exitingPage, page, false);
            page.OnNavigatedTo(BasePage.NavigationDirection.Forward);

            return true;
        } // NavigateTo

        /// <summary>
        /// Navigate to the previous page
        /// </summary>
        /// <returns>True on success</returns>
        public async Task<bool> NavigateBack()
        {
            if (_changingPage)
                return false;

            if (_pageHistory.Count > 0) {
                var exitingPage = CurrentPage;
                if (exitingPage?.OnNavigatingFrom(BasePage.NavigationDirection.Backward) == false)
                    return false;

                var enteringPage = _pageHistory.Last();
                _pageHistory.Remove(enteringPage);

                await ChangePage(exitingPage, enteringPage, true);
                exitingPage?.Dispose();
                RemovePageState(exitingPage);

                enteringPage.OnNavigatedTo(BasePage.NavigationDirection.Backward);
                return true;
            }
            return false;
        } // NavigateBack

        public void SavePageState(BasePage.PageState state) {
            _pageStates[state.PageId] = state;
        } // SavePageState

        public BasePage.PageState? LoadPageState<T>(BasePage page) where T : BasePage.PageState
        {
            BasePage.PageState? res;
            if (_pageStates.TryGetValue(page.Id, out res))
                return res as T;
            return null;
        } // LoadPageState

        public void RemovePageState(BasePage? page) {
            if (page != null)
                _pageStates.Remove(page.Id);
        } // RemovePageState
        #endregion

        private async Task ChangePage(BasePage? exiting, BasePage? entering, bool back)
        {
            if (entering == null)
                throw new ArgumentNullException(nameof(entering));

            _changingPage = true;
            entering.Opacity = 0;
            _container!.Children.Add(entering);
            if (exiting == null) {
                entering.Opacity = 1;
                _changingPage = false;
                return;
            }

            if (Transition == null || Transition.Type == TransitionSettings.EnterTransitions.None) {
                _container.Children.Remove(exiting);
                entering.Opacity = 1;
                _changingPage = false;
                return;
            }

            exiting.IsHitTestVisible = false;
            entering.IsHitTestVisible = false;

            AvaloniaProperty? property = null;
            double from = 0.0;
            double to = 0.0;
            switch (Transition.Type) {
                case TransitionSettings.EnterTransitions.SlideLeft:
                    property = TranslateTransform.XProperty;
                    from = 0.0;
                    to = this.Bounds.Size.Width;

                    exiting.RenderTransform = new TranslateTransform();
                    entering.RenderTransform = new TranslateTransform() {
                        X = to
                    };
                    break;
                case TransitionSettings.EnterTransitions.SlideUp:
                    property = TranslateTransform.YProperty;
                    from = 0.0;
                    to = this.Bounds.Size.Height;

                    exiting.RenderTransform = new TranslateTransform();
                    entering.RenderTransform = new TranslateTransform() {
                        Y = to
                    };
                    break;
                case TransitionSettings.EnterTransitions.FadeIn:
                    property = UserControl.OpacityProperty;
                    from = 1.0;
                    to = 0.0;
                    break;
            }

            // Exiting
            Animation exitAnim = new Animation()
            {
                Duration = Transition.Duration,
                Easing = Transition.Easing
            };

            var kf = new KeyFrame()
            {
                Cue = new Cue(0.0)
            };
            kf.Setters.Add(new Setter()
            {
                Property = property,
                Value = from
            });
            exitAnim.Children.Add(kf);

            kf = new KeyFrame()
            {
                Cue = new Cue(1.0)
            };
            kf.Setters.Add(new Setter()
            {
                Property = property,
                Value = back ? to : -to
            });
            exitAnim.Children.Add(kf);

            // Entering
            Animation enterAnim = new Animation()
            {
                Duration = Transition.Duration,
                Easing = Transition.Easing
            };

            kf = new KeyFrame()
            {
                Cue = new Cue(0.0)
            };
            kf.Setters.Add(new Setter()
            {
                Property = property,
                Value = back ? -to : to
            });
            enterAnim.Children.Add(kf);

            kf = new KeyFrame()
            {
                Cue = new Cue(1.0)
            };
            kf.Setters.Add(new Setter()
            {
                Property = property,
                Value = from
            });
            enterAnim.Children.Add(kf);

            if (Transition.Type == TransitionSettings.EnterTransitions.FadeIn) {
                await exitAnim.RunAsync(exiting, null);
                exiting.Opacity = 0;
                await enterAnim.RunAsync(entering, null);
                entering.Opacity = 1.0;
            } else {
                List<Task> tasks = new List<Task>();
                tasks.Add(exitAnim.RunAsync(exiting, null));
                tasks.Add(enterAnim.RunAsync(entering, null));
                entering.Opacity = 1;
                await Task.WhenAll(tasks);
            }

            entering.RenderTransform = null;
            exiting.RenderTransform = null;

            entering.IsHitTestVisible = true;
            _container.Children.Remove(exiting);

            _titleBar!.CanGoBack = CanNavigateBack;
            _changingPage = false;
        } // ChangePage

        private async void OnWindowClosing(object? sender, CancelEventArgs args)
        {
            if (_changingPage) {
                args.Cancel = true;
                return;
            }

            if (CurrentPage?.NavigateBackOnWindowClose == true && CanNavigateBack) {
                args.Cancel = true;
                await NavigateBack();
            }
        } // OnWindowClosing

        private async void OnKeyDown(object? sender, KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (_changingPage)
                return;

            if (CurrentPage != null && CurrentPage.NavigateBackWithKeyboard && e.KeyModifiers == KeyModifiers.None && e.Key == BackKey) {
                e.Handled = true;
                await NavigateBack();
            }
        } // OnKeyDown

        private void SetWaitAnimation(bool visible)
        {
            var wait = this.FindControl<Grid>("Spinner");
            wait.IsVisible = visible;
            var stack = this.FindControl<StackPanel>("WaitSpinner");
            if (visible)
                stack.Classes.Add("spinner");
            else
                stack.Classes.Remove("spinner");
        } // SetWaitAnimation

    }
}
