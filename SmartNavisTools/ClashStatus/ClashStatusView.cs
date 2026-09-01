using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartNavisTools
{
    /// <summary>
    /// WPF-представление панели обновления статусов коллизий (без XAML).
    /// </summary>
    internal sealed class ClashStatusView : UserControl
    {
        /// <summary>Создаёт визуальное дерево панели ClashStatus.</summary>
        public ClashStatusView()
        {
            var viewModel = new ClashStatusViewModel();
            DataContext = viewModel;

            var previewTextBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(ColorFromHex("#FFF7F9FC")),
                BorderBrush = new SolidColorBrush(ColorFromHex("#FFC8D1DE")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                FontFamily = new FontFamily("Consolas")
            };
            ApplyRoundedBorder(previewTextBox, 6);
            previewTextBox.SetBinding(TextBox.TextProperty, new Binding("PreviewText") { Mode = BindingMode.OneWay });

            var importButton = CreateStyledButton("Импорт отчёта (XML)");
            importButton.SetBinding(Button.CommandProperty, new Binding("ImportCommand"));

            var updateButton = CreateStyledButton("Обновить статусы");
            updateButton.SetBinding(Button.CommandProperty, new Binding("UpdateCommand"));

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            };
            buttonPanel.Children.Add(importButton);
            buttonPanel.Children.Add(updateButton);

            var root = new DockPanel { Margin = new Thickness(10) };
            DockPanel.SetDock(buttonPanel, Dock.Bottom);
            root.Children.Add(buttonPanel);
            root.Children.Add(previewTextBox);

            Content = root;
        }

        /// <summary>Создаёт стилизованную кнопку с закруглёнными углами.</summary>
        private static Button CreateStyledButton(string text)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 150,
                MinHeight = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 4, 12, 4),
                Background = new SolidColorBrush(ColorFromHex("#FF005E96")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(ColorFromHex("#FF005E96")),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ApplyRoundedBorder(button, 6);
            return button;
        }

        /// <summary>Применяет скруглённую рамку к элементу через шаблон.</summary>
        private static void ApplyRoundedBorder(Control control, double radius)
        {
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
            factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
            factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));

            if (control is TextBox)
            {
                var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
                scrollViewer.SetValue(ScrollViewer.NameProperty, "PART_ContentHost");
                factory.AppendChild(scrollViewer);
            }
            else
            {
                var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                factory.AppendChild(presenter);
            }

            var template = new ControlTemplate(control.GetType()) { VisualTree = factory };
            control.Template = template;
        }

        /// <summary>Парсит hex-цвет.</summary>
        private static Color ColorFromHex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
    }
}
