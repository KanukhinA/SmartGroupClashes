using System;
using System.Windows;
using System.Windows.Controls;using System.Windows.Forms.Integration;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace SmartNavisTools
{
    /// <summary>
    /// Универсальная WPF-оболочка для dock pane с поддержкой WPF- и WinForms-контента.
    /// </summary>
    public sealed class PluginPaneShell : UserControl
    {
        private readonly TextBlock _titleTextBlock;
        private readonly TextBlock _descriptionTextBlock;
        private readonly ContentPresenter _contentHost;

        /// <summary>
        /// Резерв для локальных стилей панели; сейчас используются встроенные стили WPF.
        /// </summary>
        private void EnsureSmartPluginTheme()
        {
        }

        /// <summary>
        /// Создаёт WPF-оболочку и помещает внутрь WPF-контент.
        /// </summary>
        /// <param name="scrollContent">
        /// true — прокручивать всю карточку;
        /// false — растягивать контент по высоте панели, чтобы внутренние списки и кнопки оставались на месте.
        /// </param>
        public PluginPaneShell(string title, string description, UIElement content, bool scrollContent = true)
        {
            EnsureSmartPluginTheme();
            (_titleTextBlock, _descriptionTextBlock, _contentHost) = BuildShell(scrollContent);
            _titleTextBlock.Text = title ?? string.Empty;
            _descriptionTextBlock.Text = description ?? string.Empty;
            AttachContent(content);
        }

        /// <summary>
        /// Создаёт WPF-оболочку и встраивает legacy WinForms-контрол через WindowsFormsHost.
        /// </summary>
        public PluginPaneShell(string title, string description, Forms.Control legacyControl)
        {
            EnsureSmartPluginTheme();
            (_titleTextBlock, _descriptionTextBlock, _contentHost) = BuildShell(scrollContent: true);
            _titleTextBlock.Text = title ?? string.Empty;
            _descriptionTextBlock.Text = description ?? string.Empty;
            AttachLegacyControl(legacyControl);
        }

        /// <summary>
        /// Собирает WPF-оболочку программно, чтобы не зависеть от XAML-компиляции в старом проекте.
        /// </summary>
        private (TextBlock title, TextBlock description, ContentPresenter host) BuildShell(bool scrollContent)
        {
            var primaryBrush = CreateBrush("#FF005E96");
            var accentBrush = CreateBrush("#FF0DA9CA");
            var surfaceBrush = CreateBrush("#FFE5EAF1");
            var cardBrush = CreateBrush("#FFF7F9FC");
            var fieldBrush = Brushes.White;
            var borderBrush = CreateBrush("#FFC8D1DE");

            var title = new TextBlock
            {
                FontFamily = new FontFamily("Arial"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };

            var description = new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 12,
                Foreground = CreateBrush("#EAF7FCFF"),
                TextWrapping = TextWrapping.Wrap
            };

            var header = new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(16, 12, 16, 12),
                CornerRadius = new CornerRadius(0, 0, 10, 10),
                Background = new LinearGradientBrush(
                    ((SolidColorBrush)primaryBrush).Color,
                    ((SolidColorBrush)accentBrush).Color,
                    new Point(0, 0),
                    new Point(1, 0)),
                Child = new StackPanel
                {
                    Children =
                    {
                        title,
                        description
                    }
                }
            };

            var host = new ContentPresenter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var contentFrame = new Border
            {
                Background = fieldBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(1),
                Child = host
            };

            UIElement workCardChild = contentFrame;
            if (scrollContent)
            {
                workCardChild = new StackPanel { Children = { contentFrame } };
            }

            var workCard = new Border
            {
                Background = cardBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Child = workCardChild
            };

            var footer = new Border
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Background = CreateBrush("#66FFFFFF"),
                Padding = new Thickness(14, 6, 14, 6)
            };

            UIElement contentArea;
            if (scrollContent)
            {
                contentArea = CreateScrollViewer(workCard);
            }
            else
            {
                contentArea = workCard;
            }

            var layout = new Grid { Background = surfaceBrush };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(header, 0);
            Grid.SetRow(contentArea, 1);
            Grid.SetRow(footer, 2);
            layout.Children.Add(header);
            layout.Children.Add(contentArea);
            layout.Children.Add(footer);

            Content = layout;
            return (title, description, host);
        }

        /// <summary>
        /// Встраивает WPF-контент в оболочку, не допуская падения панели при ошибке.
        /// </summary>
        private void AttachContent(UIElement content)
        {
            try
            {
                if (content == null)
                {
                    return;
                }

                _contentHost.Content = content;
            }
            catch (Exception exception)
            {
                _contentHost.Content = new TextBlock
                {
                    Text = "Не удалось инициализировать интерфейс.\r\n" + exception.Message,
                    Padding = new Thickness(12),
                    TextWrapping = TextWrapping.Wrap
                };
            }
        }

        /// <summary>
        /// Встраивает WinForms-контрол через WindowsFormsHost для обратной совместимости.
        /// </summary>
        private void AttachLegacyControl(Forms.Control legacyControl)
        {
            try
            {
                if (legacyControl == null)
                {
                    return;
                }

                legacyControl.Dock = Forms.DockStyle.Fill;
                var host = new WindowsFormsHost { Child = legacyControl };
                _contentHost.Content = host;
            }
            catch (Exception exception)
            {
                _contentHost.Content = new TextBlock
                {
                    Text = "Не удалось инициализировать интерфейс.\r\n" + exception.Message,
                    Padding = new Thickness(12),
                    TextWrapping = TextWrapping.Wrap
                };
            }
        }

        /// <summary>
        /// Создаёт скроллируемую область контента с типовыми отступами оболочки.
        /// </summary>
        private static ScrollViewer CreateScrollViewer(UIElement content)
        {
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(14, 0, 14, 0),
                Content = new StackPanel
                {
                    Children =
                    {
                        content
                    }
                }
            };
        }

        /// <summary>
        /// Создаёт замороженную кисть по hex-цвету для повторного безопасного использования в WPF.
        /// </summary>
        private static Brush CreateBrush(string hexColor)
        {
            var converter = new BrushConverter();
            var brush = (Brush)converter.ConvertFromString(hexColor);
            brush.Freeze();
            return brush;
        }
    }
}
