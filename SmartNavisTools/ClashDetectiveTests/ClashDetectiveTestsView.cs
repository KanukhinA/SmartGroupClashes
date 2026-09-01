using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartNavisTools
{
    /// <summary>
    /// WPF-представление панели проверок Clash Detective (без XAML).
    /// </summary>
    internal sealed class ClashDetectiveTestsView : UserControl
    {
        /// <summary>Создаёт визуальное дерево панели ClashDetectiveTests.</summary>
        public ClashDetectiveTestsView()
        {
            var viewModel = new ClashDetectiveTestsViewModel();
            DataContext = viewModel;

            var filterTextBox = new TextBox
            {
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(4),
                BorderBrush = new SolidColorBrush(ColorFromHex("#FFC8D1DE")),
                BorderThickness = new Thickness(1)
            };
            ApplyRoundedBorder(filterTextBox, 6);
            filterTextBox.SetBinding(TextBox.TextProperty, new Binding("FilterText")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            var useRegexCheckBox = new CheckBox
            {
                Content = "Regex",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            useRegexCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding("UseRegex"));

            var clearButton = CreateStyledButton("Очистить", 70);
            clearButton.SetBinding(Button.CommandProperty, new Binding("ClearFilterCommand"));

            var filterRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(useRegexCheckBox, Dock.Right);
            DockPanel.SetDock(clearButton, Dock.Right);
            filterRow.Children.Add(clearButton);
            filterRow.Children.Add(useRegexCheckBox);
            filterRow.Children.Add(filterTextBox);

            var filterSummaryLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(ColorFromHex("#FF5C6875")),
                Margin = new Thickness(0, 2, 0, 6),
                FontSize = 11
            };
            filterSummaryLabel.SetBinding(TextBlock.TextProperty, new Binding("FilterSummary"));

            var listBox = new ListBox
            {
                SelectionMode = SelectionMode.Extended,
                BorderBrush = new SolidColorBrush(ColorFromHex("#FFC8D1DE")),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(ColorFromHex("#FFF7F9FC"))
            };
            listBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Tests"));

            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(
                ListBoxItem.IsSelectedProperty,
                new Binding("IsSelected") { Mode = BindingMode.TwoWay }));
            listBox.ItemContainerStyle = itemStyle;

            var textFactory = new FrameworkElementFactory(typeof(TextBlock));
            textFactory.SetBinding(TextBlock.TextProperty, new Binding("DisplayName"));
            textFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2));
            textFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            listBox.ItemTemplate = new DataTemplate { VisualTree = textFactory };

            var selectionLabel = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            selectionLabel.SetBinding(TextBlock.TextProperty, new Binding("SelectionSummary"));

            var refreshButton = CreateStyledButton("Обновить список", 120);
            refreshButton.SetBinding(Button.CommandProperty, new Binding("RefreshCommand"));

            var updateButton = CreateStyledButton("Обновить проверки", 140);
            updateButton.SetBinding(Button.CommandProperty, new Binding("UpdateTestsCommand"));

            var exportXmlButton = CreateStyledButton("Экспорт XML", 100);
            exportXmlButton.SetBinding(Button.CommandProperty, new Binding("ExportXmlCommand"));

            var exportHtmlButton = CreateStyledButton("Экспорт HTML", 110);
            exportHtmlButton.SetBinding(Button.CommandProperty, new Binding("ExportHtmlCommand"));

            var bottomPanel = new WrapPanel
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            bottomPanel.Children.Add(selectionLabel);
            bottomPanel.Children.Add(refreshButton);
            bottomPanel.Children.Add(updateButton);
            bottomPanel.Children.Add(exportXmlButton);
            bottomPanel.Children.Add(exportHtmlButton);

            var root = new DockPanel { Margin = new Thickness(10) };
            DockPanel.SetDock(filterRow, Dock.Top);
            DockPanel.SetDock(filterSummaryLabel, Dock.Top);
            DockPanel.SetDock(bottomPanel, Dock.Bottom);
            root.Children.Add(filterRow);
            root.Children.Add(filterSummaryLabel);
            root.Children.Add(bottomPanel);
            root.Children.Add(listBox);

            Content = root;
        }

        /// <summary>Создаёт стилизованную кнопку.</summary>
        private static Button CreateStyledButton(string text, double minWidth)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = minWidth,
                MinHeight = 26,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 3, 8, 3),
                Background = new SolidColorBrush(ColorFromHex("#FF005E96")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(ColorFromHex("#FF005E96")),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ApplyRoundedButtonBorder(button, 6);
            return button;
        }

        /// <summary>Применяет скруглённую рамку к кнопке.</summary>
        private static void ApplyRoundedButtonBorder(Button button, double radius)
        {
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
            factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
            factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.AppendChild(presenter);

            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = factory };
        }

        /// <summary>Применяет скруглённую рамку к TextBox.</summary>
        private static void ApplyRoundedBorder(TextBox textBox, double radius)
        {
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
            factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
            factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));

            var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollViewer.SetValue(ScrollViewer.NameProperty, "PART_ContentHost");
            factory.AppendChild(scrollViewer);

            textBox.Template = new ControlTemplate(typeof(TextBox)) { VisualTree = factory };
        }

        /// <summary>Парсит hex-цвет.</summary>
        private static Color ColorFromHex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
    }
}
