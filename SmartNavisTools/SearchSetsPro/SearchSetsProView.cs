using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace SmartNavisTools
{
    /// <summary>
    /// WPF-представление панели «Поисковые наборы PRO» (без XAML).
    /// </summary>
    internal sealed class SearchSetsProView : UserControl
    {
        /// <summary>Создаёт визуальное дерево панели SearchSetsPro.</summary>
        public SearchSetsProView()
        {
            var viewModel = new SearchSetsProViewModel();
            DataContext = viewModel;

            var root = new Grid
            {
                Margin = new Thickness(10)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var filterTextBox = CreateTextBox();
            filterTextBox.SetBinding(TextBox.TextProperty, new Binding("FilterText")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            Grid.SetRow(filterTextBox, 0);
            root.Children.Add(filterTextBox);

            var contentGrid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.42, GridUnitType.Star) });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.58, GridUnitType.Star) });
            Grid.SetRow(contentGrid, 1);
            root.Children.Add(contentGrid);

            var treeSection = BuildTreeSection();
            Grid.SetRow(treeSection, 0);
            contentGrid.Children.Add(treeSection);

            var tabs = BuildTabs();
            Grid.SetRow(tabs, 1);
            contentGrid.Children.Add(tabs);

            var limitations = new TextBlock
            {
                Foreground = new SolidColorBrush(ColorFromHex("#FF5C6875")),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            limitations.SetBinding(TextBlock.TextProperty, new Binding("LimitationsText"));
            Grid.SetRow(limitations, 2);
            root.Children.Add(limitations);

            Content = root;
        }

        /// <summary>Строит секцию дерева поисковых наборов.</summary>
        private FrameworkElement BuildTreeSection()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var treeView = new TreeView
            {
                Background = new SolidColorBrush(ColorFromHex("#FFF7F9FC")),
                BorderBrush = new SolidColorBrush(ColorFromHex("#FFC8D1DE")),
                BorderThickness = new Thickness(1),
                ItemTemplate = CreateTreeTemplate()
            };
            treeView.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("TreeRootNodes"));
            Grid.SetRow(treeView, 0);
            panel.Children.Add(treeView);

            var actions = new WrapPanel
            {
                Margin = new Thickness(0, 8, 0, 0)
            };

            var selectedLabel = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 0)
            };
            selectedLabel.SetBinding(TextBlock.TextProperty, new Binding("SelectedCount")
            {
                StringFormat = "Выбрано наборов: {0}"
            });
            actions.Children.Add(selectedLabel);

            var refreshButton = CreateButton("Обновить список", 120);
            refreshButton.SetBinding(Button.CommandProperty, new Binding("RefreshTreeCommand"));
            actions.Children.Add(refreshButton);

            var selectAllButton = CreateButton("Выбрать все", 100);
            selectAllButton.SetBinding(Button.CommandProperty, new Binding("SelectAllCommand"));
            actions.Children.Add(selectAllButton);

            var clearButton = CreateButton("Снять выбор", 100);
            clearButton.SetBinding(Button.CommandProperty, new Binding("ClearSelectionCommand"));
            actions.Children.Add(clearButton);

            Grid.SetRow(actions, 1);
            panel.Children.Add(actions);

            return panel;
        }

        /// <summary>Создаёт шаблон дерева с чекбоксами.</summary>
        private static HierarchicalDataTemplate CreateTreeTemplate()
        {
            var checkBoxFactory = new FrameworkElementFactory(typeof(CheckBox));
            checkBoxFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsChecked")
            {
                Mode = BindingMode.TwoWay
            });
            checkBoxFactory.SetValue(MarginProperty, new Thickness(2, 1, 2, 1));
            checkBoxFactory.SetValue(CheckBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            checkBoxFactory.SetBinding(ContentControl.ContentProperty, new Binding("DisplayName"));

            var template = new HierarchicalDataTemplate(typeof(TreeNodeViewModel))
            {
                ItemsSource = new Binding("Children"),
                VisualTree = checkBoxFactory
            };
            return template;
        }

        /// <summary>Строит вкладки операций.</summary>
        private TabControl BuildTabs()
        {
            var tabs = new TabControl
            {
                Margin = new Thickness(0, 12, 0, 0)
            };
            tabs.SetBinding(TabControl.SelectedIndexProperty, new Binding("ActiveTabIndex") { Mode = BindingMode.TwoWay });

            tabs.Items.Add(new TabItem
            {
                Header = "Добавить",
                Content = BuildAddTab()
            });

            tabs.Items.Add(new TabItem
            {
                Header = "Заменить",
                Content = BuildReplaceTab()
            });

            tabs.Items.Add(new TabItem
            {
                Header = "Удалить",
                Content = BuildDeleteTab()
            });

            return tabs;
        }

        /// <summary>Строит вкладку добавления.</summary>
        private FrameworkElement BuildAddTab()
        {
            var panel = CreateTabRoot();
            panel.Children.Add(CreatePropertyEditor("Новая категория", "TargetCategory"));
            panel.Children.Add(CreatePropertyEditor("Новое свойство", "TargetProperty"));
            panel.Children.Add(CreateValueEditor());
            panel.Children.Add(CreateFooter());
            return panel;
        }

        /// <summary>Строит вкладку замены.</summary>
        private FrameworkElement BuildReplaceTab()
        {
            var panel = CreateTabRoot();
            panel.Children.Add(CreatePropertyEditor("Исходная категория", "SourceCategory"));
            panel.Children.Add(CreatePropertyEditor("Исходное свойство", "SourceProperty"));
            panel.Children.Add(CreatePropertyEditor("Новая категория", "TargetCategory"));
            panel.Children.Add(CreatePropertyEditor("Новое свойство", "TargetProperty"));
            panel.Children.Add(CreateValueEditor());
            panel.Children.Add(CreateFooter());
            return panel;
        }

        /// <summary>Строит вкладку удаления.</summary>
        private FrameworkElement BuildDeleteTab()
        {
            var panel = CreateTabRoot();

            var deleteModePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var fromListRadio = new RadioButton
            {
                Content = "Выбрать из условий выбранных наборов",
                Margin = new Thickness(0, 0, 0, 4)
            };
            fromListRadio.SetBinding(ToggleButton.IsCheckedProperty, new Binding("DeleteFromList") { Mode = BindingMode.TwoWay });
            deleteModePanel.Children.Add(fromListRadio);

            var byPropertyRadio = new RadioButton
            {
                Content = "По категории и свойству (все совпадения)",
                Margin = new Thickness(0, 0, 0, 8)
            };
            byPropertyRadio.SetBinding(ToggleButton.IsCheckedProperty, new Binding("DeleteByProperty") { Mode = BindingMode.TwoWay });
            deleteModePanel.Children.Add(byPropertyRadio);

            panel.Children.Add(deleteModePanel);
            panel.Children.Add(CreatePropertyEditor("Категория", "SourceCategory"));
            panel.Children.Add(CreatePropertyEditor("Свойство", "SourceProperty"));

            var conditionsSummary = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 6),
                Foreground = new SolidColorBrush(ColorFromHex("#FF5C6875"))
            };
            conditionsSummary.SetBinding(TextBlock.TextProperty, new Binding("ConditionsSummary"));
            panel.Children.Add(conditionsSummary);

            var conditionsList = new ListBox
            {
                SelectionMode = SelectionMode.Extended,
                MinHeight = 120,
                Background = new SolidColorBrush(ColorFromHex("#FFF7F9FC")),
                BorderBrush = new SolidColorBrush(ColorFromHex("#FFC8D1DE")),
                BorderThickness = new Thickness(1)
            };
            conditionsList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Conditions"));

            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(
                ListBoxItem.IsSelectedProperty,
                new Binding("IsChecked") { Mode = BindingMode.TwoWay }));
            conditionsList.ItemContainerStyle = itemStyle;

            var textFactory = new FrameworkElementFactory(typeof(TextBlock));
            textFactory.SetBinding(TextBlock.TextProperty, new Binding("DisplayText"));
            textFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2));
            textFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            conditionsList.ItemTemplate = new DataTemplate { VisualTree = textFactory };
            panel.Children.Add(conditionsList);

            var conditionButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var refreshConditions = CreateButton("Обновить список условий", 160);
            refreshConditions.SetBinding(Button.CommandProperty, new Binding("RefreshConditionsCommand"));
            conditionButtons.Children.Add(refreshConditions);
            var selectAllConditions = CreateButton("Отметить все", 100);
            selectAllConditions.SetBinding(Button.CommandProperty, new Binding("SelectAllConditionsCommand"));
            conditionButtons.Children.Add(selectAllConditions);
            var clearConditions = CreateButton("Снять все", 100);
            clearConditions.SetBinding(Button.CommandProperty, new Binding("ClearConditionsCommand"));
            conditionButtons.Children.Add(clearConditions);
            panel.Children.Add(conditionButtons);

            panel.Children.Add(CreateFooter());
            return panel;
        }

        /// <summary>Создаёт контейнер вкладки.</summary>
        private static StackPanel CreateTabRoot()
        {
            return new StackPanel
            {
                Margin = new Thickness(10)
            };
        }

        /// <summary>Создаёт редактор категории или свойства.</summary>
        private FrameworkElement CreatePropertyEditor(string labelText, string bindingPath)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(new TextBlock
            {
                Text = labelText,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = new SolidColorBrush(ColorFromHex("#FF1D2733"))
            });

            var combo = new ComboBox
            {
                IsEditable = true,
                MinWidth = 220,
                Background = new SolidColorBrush(ColorFromHex("#FFF7F9FC")),
                BorderBrush = new SolidColorBrush(ColorFromHex("#FFC8D1DE")),
                BorderThickness = new Thickness(1)
            };
            combo.SetBinding(ComboBox.TextProperty, new Binding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            if (bindingPath.IndexOf("Category", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("CategorySuggestions"));
            }
            else
            {
                combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("PropertySuggestions"));
            }

            panel.Children.Add(combo);
            return panel;
        }

        /// <summary>Создаёт редактор оператора и значения.</summary>
        private FrameworkElement CreateValueEditor()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            panel.Children.Add(new TextBlock
            {
                Text = "Оператор",
                Margin = new Thickness(0, 0, 0, 4)
            });

            var comparison = new ComboBox
            {
                MinWidth = 220,
                Background = new SolidColorBrush(ColorFromHex("#FFF7F9FC")),
                BorderBrush = new SolidColorBrush(ColorFromHex("#FFC8D1DE")),
                BorderThickness = new Thickness(1)
            };
            comparison.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("ComparisonOptions"));
            comparison.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedComparison") { Mode = BindingMode.TwoWay });
            panel.Children.Add(comparison);

            panel.Children.Add(new TextBlock
            {
                Text = "Значение",
                Margin = new Thickness(0, 8, 0, 4)
            });

            var valueTextBox = CreateTextBox();
            valueTextBox.SetBinding(TextBox.TextProperty, new Binding("ValueText")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            panel.Children.Add(valueTextBox);

            var options = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var ignoreCase = new CheckBox { Content = "Без учёта регистра", Margin = new Thickness(0, 0, 12, 0) };
            ignoreCase.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IgnoreCase") { Mode = BindingMode.TwoWay });
            options.Children.Add(ignoreCase);
            var ignoreAccents = new CheckBox { Content = "Без учёта акцентов" };
            ignoreAccents.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IgnoreAccents") { Mode = BindingMode.TwoWay });
            options.Children.Add(ignoreAccents);
            panel.Children.Add(options);

            return panel;
        }

        /// <summary>Создаёт нижний блок команд и режимов применения.</summary>
        private FrameworkElement CreateFooter()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

            var modePanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            var updateExisting = new RadioButton
            {
                Content = "Обновить существующие",
                Margin = new Thickness(0, 0, 12, 0)
            };
            updateExisting.SetBinding(ToggleButton.IsCheckedProperty, new Binding("UpdateExisting") { Mode = BindingMode.TwoWay });
            modePanel.Children.Add(updateExisting);

            var createCopies = new RadioButton
            {
                Content = "Создать копии",
                Margin = new Thickness(0, 0, 12, 0)
            };
            createCopies.SetBinding(ToggleButton.IsCheckedProperty, new Binding("CreateCopies") { Mode = BindingMode.TwoWay });
            modePanel.Children.Add(createCopies);

            modePanel.Children.Add(new TextBlock
            {
                Text = "Суффикс:",
                Margin = new Thickness(0, 4, 6, 0)
            });

            var suffix = CreateTextBox();
            suffix.MinWidth = 140;
            suffix.SetBinding(TextBox.TextProperty, new Binding("CopySuffix")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            modePanel.Children.Add(suffix);
            panel.Children.Add(modePanel);

            var buttons = new WrapPanel();
            var refreshCatalog = CreateButton("Обновить каталог из наборов", 190);
            refreshCatalog.SetBinding(Button.CommandProperty, new Binding("RefreshCatalogCommand"));
            buttons.Children.Add(refreshCatalog);

            var apply = CreateButton(null, 120);
            apply.SetBinding(ContentControl.ContentProperty, new Binding("ApplyButtonText"));
            apply.SetBinding(Button.CommandProperty, new Binding("ApplyCommand"));
            buttons.Children.Add(apply);
            panel.Children.Add(buttons);

            return panel;
        }

        /// <summary>Создаёт стилизованную кнопку.</summary>
        private static Button CreateButton(string text, double minWidth)
        {
            return new Button
            {
                Content = text,
                MinWidth = minWidth,
                MinHeight = 28,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(ColorFromHex("#FF005E96")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(ColorFromHex("#FF005E96")),
                BorderThickness = new Thickness(1)
            };
        }

        /// <summary>Создаёт стилизованный текстовый ввод.</summary>
        private static TextBox CreateTextBox()
        {
            return new TextBox
            {
                Background = new SolidColorBrush(ColorFromHex("#FFF7F9FC")),
                BorderBrush = new SolidColorBrush(ColorFromHex("#FFC8D1DE")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                MinHeight = 28
            };
        }

        /// <summary>Парсит hex-цвет.</summary>
        private static Color ColorFromHex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

    }
}
