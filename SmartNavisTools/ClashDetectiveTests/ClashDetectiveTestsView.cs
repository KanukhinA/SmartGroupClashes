using System;
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
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;

            try
            {
                BuildLayout();
            }
            catch (Exception exception)
            {
                Content = new TextBlock
                {
                    Text = "Не удалось создать интерфейс проверок.\r\n" + exception.Message,
                    Padding = new Thickness(12),
                    TextWrapping = TextWrapping.Wrap
                };
            }
        }

        /// <summary>
        /// Собирает макет: фильтр сверху, список по центру, панель кнопок всегда снизу.
        /// </summary>
        private void BuildLayout()
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
                Background = new SolidColorBrush(ColorFromHex("#FFF7F9FC")),
                MinHeight = 80
            };
            ScrollViewer.SetVerticalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);
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
                Margin = new Thickness(0, 0, 0, 6)
            };
            selectionLabel.SetBinding(TextBlock.TextProperty, new Binding("SelectionSummary"));

            var projectLabel = new TextBlock
            {
                Text = "Проект",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var projectCombo = new ComboBox
            {
                MinHeight = 26,
                MinWidth = 180,
                DisplayMemberPath = "Name",
                BorderBrush = new SolidColorBrush(ColorFromHex("#FFC8D1DE")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            projectCombo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Projects"));
            projectCombo.SetBinding(ComboBox.SelectedItemProperty, new Binding("SelectedProject")
            {
                Mode = BindingMode.TwoWay
            });

            var refreshProjectsButton = CreateStyledButton("Загрузить проекты", 140);
            refreshProjectsButton.Margin = new Thickness(0, 0, 0, 0);
            refreshProjectsButton.SetBinding(Button.CommandProperty, new Binding("RefreshProjectsCommand"));

            var projectRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(refreshProjectsButton, Dock.Right);
            DockPanel.SetDock(projectLabel, Dock.Left);
            projectRow.Children.Add(refreshProjectsButton);
            projectRow.Children.Add(projectLabel);
            projectRow.Children.Add(projectCombo);

            var projectHintLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(ColorFromHex("#FF5C6875")),
                Margin = new Thickness(0, 0, 0, 6),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            projectHintLabel.SetBinding(TextBlock.TextProperty, new Binding("ProjectHint"));

            var statusesLabel = new TextBlock
            {
                Text = "Статусы для отправки в БД:",
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 12
            };

            var statusesPanel = new WrapPanel();
            statusesPanel.Children.Add(CreateStatusCheckBox("Новые", "SendNewStatus"));
            statusesPanel.Children.Add(CreateStatusCheckBox("Активные", "SendActiveStatus"));
            statusesPanel.Children.Add(CreateStatusCheckBox("Проверенные", "SendReviewedStatus"));
            statusesPanel.Children.Add(CreateStatusCheckBox("Утверждённые", "SendApprovedStatus"));
            statusesPanel.Children.Add(CreateStatusCheckBox("Исправленные", "SendResolvedStatus"));

            var imagesLabel = new TextBlock
            {
                Text = "Изображения:",
                Margin = new Thickness(0, 2, 0, 4),
                FontSize = 12
            };

            var imagesPanel = new WrapPanel();
            CheckBox imagesForNewCheckBox = CreateStatusCheckBox("Картинки только без превью в БД", "SendImagesForNewOnly");
            imagesForNewCheckBox.SetBinding(UIElement.IsEnabledProperty, new Binding("CanSendImagesForNewOnly"));
            imagesPanel.Children.Add(imagesForNewCheckBox);
            imagesPanel.Children.Add(CreateStatusCheckBox("Без изображений", "SendWithoutImages"));

            CheckBox nativeImagesCheckBox = CreateStatusCheckBox(
                "Создание картинок через печать отчёта",
                "UseNativeReportImages");
            nativeImagesCheckBox.SetBinding(UIElement.IsEnabledProperty, new Binding("CanUseNativeReportImages"));
            imagesPanel.Children.Add(nativeImagesCheckBox);

            var refreshButton = CreateStyledButton("Обновить список", 120);
            refreshButton.SetBinding(Button.CommandProperty, new Binding("RefreshCommand"));

            var updateButton = CreateStyledButton("Обновить проверки", 140);
            updateButton.SetBinding(Button.CommandProperty, new Binding("UpdateTestsCommand"));

            var exportXmlButton = CreateStyledButton("Экспорт XML", 100);
            exportXmlButton.SetBinding(Button.CommandProperty, new Binding("ExportXmlCommand"));

            var exportHtmlButton = CreateStyledButton("Экспорт HTML", 110);
            exportHtmlButton.SetBinding(Button.CommandProperty, new Binding("ExportHtmlCommand"));

            var sendReportButton = CreateStyledButton("Отправить отчёты в БД", 170);
            sendReportButton.SetBinding(Button.CommandProperty, new Binding("SendReportCommand"));

            var buttonsPanel = new WrapPanel();
            buttonsPanel.Children.Add(refreshButton);
            buttonsPanel.Children.Add(updateButton);
            buttonsPanel.Children.Add(exportXmlButton);
            buttonsPanel.Children.Add(exportHtmlButton);
            buttonsPanel.Children.Add(sendReportButton);

            var bottomPanel = new StackPanel();
            bottomPanel.Children.Add(projectRow);
            bottomPanel.Children.Add(projectHintLabel);
            bottomPanel.Children.Add(statusesLabel);
            bottomPanel.Children.Add(statusesPanel);
            bottomPanel.Children.Add(imagesLabel);
            bottomPanel.Children.Add(imagesPanel);
            bottomPanel.Children.Add(selectionLabel);
            bottomPanel.Children.Add(buttonsPanel);

            // Нижняя панель с кнопками закреплена в последней строке Grid и не уезжает со списком.
            var footer = new Border
            {
                BorderBrush = new SolidColorBrush(ColorFromHex("#FFC8D1DE")),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = bottomPanel
            };

            var root = new Grid { Margin = new Thickness(10) };
            root.SetBinding(UIElement.IsEnabledProperty, new Binding("IsIdle"));
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(filterRow, 0);
            Grid.SetRow(filterSummaryLabel, 1);
            Grid.SetRow(listBox, 2);
            Grid.SetRow(footer, 3);
            root.Children.Add(filterRow);
            root.Children.Add(filterSummaryLabel);
            root.Children.Add(listBox);
            root.Children.Add(footer);

            var host = new Grid();
            host.Children.Add(root);
            host.Children.Add(CreateBusyOverlay());
            Content = host;
        }

        /// <summary>Затемнение с ProgressBar на время отправки в БД.</summary>
        private static UIElement CreateBusyOverlay()
        {
            var statusText = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ColorFromHex("#FF005E96")),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            statusText.SetBinding(TextBlock.TextProperty, new Binding("BusyStatusText"));

            var progress = new ProgressBar
            {
                IsIndeterminate = true,
                Height = 8,
                Minimum = 0,
                Maximum = 100,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(ColorFromHex("#FF005E96")),
                Background = new SolidColorBrush(ColorFromHex("#FFE5EAF1"))
            };

            var cardContent = new StackPanel();
            cardContent.Children.Add(statusText);
            cardContent.Children.Add(progress);

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(ColorFromHex("#FFC8D1DE")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(20, 16, 20, 16),
                MinWidth = 260,
                MaxWidth = 360,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = cardContent
            };

            var overlay = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 247, 249, 252)),
                IsHitTestVisible = true
            };
            overlay.Children.Add(card);
            overlay.SetBinding(
                UIElement.VisibilityProperty,
                new Binding("IsSending") { Converter = new BooleanToVisibilityConverter() });
            Panel.SetZIndex(overlay, 10);
            return overlay;
        }

        /// <summary>Создаёт флажок статуса для отправки в БД.</summary>
        private static CheckBox CreateStatusCheckBox(string text, string bindingPath)
        {
            var checkBox = new CheckBox
            {
                Content = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 6)
            };
            checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(bindingPath)
            {
                Mode = BindingMode.TwoWay
            });
            return checkBox;
        }

        /// <summary>Создаёт стилизованную кнопку.</summary>
        private static Button CreateStyledButton(string text, double minWidth)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = minWidth,
                MinHeight = 26,
                Margin = new Thickness(0, 0, 6, 6),
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
