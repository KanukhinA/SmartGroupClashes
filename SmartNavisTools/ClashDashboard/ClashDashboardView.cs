using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;


namespace SmartNavisTools
{
    /// <summary>
    /// WPF-представление для дашборда коллизий, построенное программно без XAML.
    /// </summary>
    internal sealed class ClashDashboardView : UserControl
    {
        private readonly ClashDashboardViewModel _viewModel;
        private ListBox _modelsListBox;

        /// <summary>
        /// Создаёт UI дашборда и привязывает его к ViewModel.
        /// </summary>
        public ClashDashboardView()
        {
            _viewModel = new ClashDashboardViewModel();
            DataContext = _viewModel;

            Content = BuildLayout();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Инициализирует ViewModel при загрузке View.
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.Initialize();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Освобождает ресурсы ViewModel при выгрузке View.
        /// </summary>
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.Dispose();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Строит полное визуальное дерево панели.
        /// </summary>
        private UIElement BuildLayout()
        {
            var root = new StackPanel { Margin = new Thickness(2) };

            root.Children.Add(CreateSectionLabel("Фильтр проверок:"));
            root.Children.Add(CreateFilterTextBox());

            var testsButtons = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var selectAllBtn = CreateStyledButton("Выбрать все");
            selectAllBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding("SelectAllTestsCommand"));
            testsButtons.Children.Add(selectAllBtn);

            var clearBtn = CreateStyledButton("Снять выбор");
            clearBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding("ClearTestsSelectionCommand"));
            testsButtons.Children.Add(clearBtn);

            root.Children.Add(testsButtons);

            root.Children.Add(CreateSectionLabel("Проверки Clash Detective (Shift/Ctrl — несколько):"));
            root.Children.Add(CreateTestsList());
            root.Children.Add(CreateSectionLabel("Статусы (Shift/Ctrl — несколько):"));
            root.Children.Add(CreateStatusesList());

            root.Children.Add(CreateSectionLabel("Этаж:"));
            root.Children.Add(CreateRadioButton("Этаж как в Clash Detective (сетка)", "FloorSourceIsGrid"));
            root.Children.Add(CreateRadioButton("Этаж по свойству", "FloorSourceIsProperty"));
            root.Children.Add(CreatePropertyComboBox("FloorPropertyName", "FloorPropertySuggestions", "FloorSourceIsProperty"));

            root.Children.Add(CreateSectionLabel("Раздел модели и смежный раздел:"));
            root.Children.Add(CreateRadioButton("Определять раздел по файлу модели", "DisciplineSourceIsModel"));
            root.Children.Add(CreateRadioButton("Определять раздел по свойству", "DisciplineSourceIsProperty"));
            root.Children.Add(CreatePropertyComboBox("DisciplinePropertyName", "DisciplinePropertySuggestions", "DisciplineSourceIsProperty"));

            root.Children.Add(CreateSectionLabel("Файлы моделей NWC (из NWD раскрываются, Shift/Ctrl — несколько):"));
            root.Children.Add(CreateModelsListBox());
            root.Children.Add(CreateGroupIntoSectionPanel());
            root.Children.Add(CreateSectionLabel("Разделы моделей (Key). Несколько файлов → один раздел:"));
            root.Children.Add(CreateMappingsList());
            root.Children.Add(CreateMappingButtons());
            root.Children.Add(new TextBlock
            {
                Text = "В отчёте: один график = раздел модели. Полоска = этаж. Сегмент = смежный раздел (второй участник).",
                FontSize = 11,
                Foreground = Brush("#FF64748B"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4)
            });

            root.Children.Add(CreateActionButtons());
            root.Children.Add(CreateSummaryLabel());

            return root;
        }

        /// <summary>
        /// Создаёт заголовок секции.
        /// </summary>
        private static TextBlock CreateSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = Brush("#FF1D2733"),
                Margin = new Thickness(0, 8, 0, 4)
            };
        }

        /// <summary>
        /// Создаёт текстовое поле фильтра тестов.
        /// </summary>
        private static TextBox CreateFilterTextBox()
        {
            var tb = CreateStyledTextBox();
            tb.SetBinding(TextBox.TextProperty, new Binding("TestsFilter")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return tb;
        }

        /// <summary>
        /// Создаёт список тестов с multi-select через Shift/Ctrl.
        /// </summary>
        private static UIElement CreateTestsList()
        {
            var listBox = CreateMultiSelectListBox();
            listBox.MaxHeight = 160;
            listBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Tests"));

            var style = CreateSelectedItemStyle("IsChecked");
            style.Setters.Add(new Setter(
                UIElement.VisibilityProperty,
                new Binding("IsVisible") { Converter = new BooleanToVisibilityConverter() }));
            listBox.ItemContainerStyle = style;
            listBox.ItemTemplate = CreateTextItemTemplate("DisplayName");
            return WrapInRoundedBorder(listBox);
        }

        /// <summary>
        /// Создаёт шаблон элемента теста.
        /// </summary>
        private static DataTemplate CreateTextItemTemplate(string textPath)
        {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty, new Binding(textPath));
            factory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2));
            factory.SetValue(Control.FontSizeProperty, 12.0);
            factory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            template.VisualTree = factory;
            return template;
        }

        /// <summary>
        /// Создаёт стиль ListBoxItem с двусторонней привязкой IsSelected.
        /// </summary>
        private static Style CreateSelectedItemStyle(string selectedPath)
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(
                ListBoxItem.IsSelectedProperty,
                new Binding(selectedPath) { Mode = BindingMode.TwoWay }));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            return style;
        }

        /// <summary>
        /// Создаёт ListBox с режимом Extended (Shift — диапазон, Ctrl — точечный выбор).
        /// </summary>
        private static ListBox CreateMultiSelectListBox()
        {
            return new ListBox
            {
                SelectionMode = SelectionMode.Extended,
                Margin = new Thickness(0, 2, 0, 2),
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
        }

        /// <summary>
        /// Оборачивает список в скруглённую рамку.
        /// </summary>
        private static Border WrapInRoundedBorder(UIElement child)
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Brush("#FFC8D1DE"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2),
                Child = child
            };
        }

        /// <summary>
        /// Создаёт список статусов с multi-select через Shift/Ctrl.
        /// </summary>
        private static UIElement CreateStatusesList()
        {
            var listBox = CreateMultiSelectListBox();
            listBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Statuses"));
            listBox.ItemContainerStyle = CreateSelectedItemStyle("IsChecked");
            listBox.ItemTemplate = CreateTextItemTemplate("Name");
            return WrapInRoundedBorder(listBox);
        }

        /// <summary>
        /// Создаёт RadioButton, привязанный к булевому свойству ViewModel.
        /// </summary>
        private static RadioButton CreateRadioButton(string text, string bindingPath)
        {
            var rb = new RadioButton
            {
                Content = text,
                Margin = new Thickness(0, 2, 0, 2),
                FontSize = 12,
                GroupName = bindingPath.Contains("Floor") ? "FloorGroup" : "DisciplineGroup"
            };
            rb.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Binding(bindingPath) { Mode = BindingMode.TwoWay });
            return rb;
        }

        /// <summary>
        /// Создаёт ComboBox с подсказками свойств, привязанный к ViewModel.
        /// </summary>
        private static ComboBox CreatePropertyComboBox(string textPath, string itemsSourcePath, string enabledPath)
        {
            var cb = new ComboBox
            {
                IsEditable = true,
                Margin = new Thickness(0, 2, 0, 4),
                FontSize = 12,
                Height = 26
            };

            ApplyRoundedComboBoxStyle(cb);

            cb.SetBinding(ComboBox.TextProperty, new Binding(textPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            });
            cb.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(itemsSourcePath));
            cb.SetBinding(UIElement.IsEnabledProperty, new Binding(enabledPath));
            return cb;
        }

        /// <summary>
        /// Создаёт список моделей документа с multi-select (Shift/Ctrl).
        /// </summary>
        private UIElement CreateModelsListBox()
        {
            _modelsListBox = new ListBox
            {
                SelectionMode = SelectionMode.Extended,
                Height = 160,
                Margin = new Thickness(0, 2, 0, 4),
                FontSize = 12,
                BorderBrush = Brush("#FFC8D1DE"),
                BorderThickness = new Thickness(1),
                Background = Brushes.White
            };
            _modelsListBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("ModelFileSuggestions"));
            _modelsListBox.SetBinding(UIElement.IsEnabledProperty, new Binding("DisciplineSourceIsModel"));

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Brush("#FFC8D1DE"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2),
                Child = _modelsListBox
            };
        }

        /// <summary>
        /// Создаёт поле имени раздела и кнопку группировки выбранных моделей.
        /// </summary>
        private UIElement CreateGroupIntoSectionPanel()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 6)
            };

            var nameLabel = new TextBlock
            {
                Text = "Имя раздела:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12,
                Foreground = Brush("#FF1D2733")
            };
            panel.Children.Add(nameLabel);

            var nameBox = CreateStyledTextBox();
            nameBox.Width = 180;
            nameBox.Margin = new Thickness(0, 0, 6, 0);
            nameBox.ToolTip = "Например: ОВиК, АР, КР";
            nameBox.SetBinding(TextBox.TextProperty, new Binding("NewSectionName")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            nameBox.SetBinding(UIElement.IsEnabledProperty, new Binding("DisciplineSourceIsModel"));
            panel.Children.Add(nameBox);

            var groupBtn = CreateStyledButton("Сгруппировать в раздел");
            groupBtn.SetBinding(UIElement.IsEnabledProperty, new Binding("DisciplineSourceIsModel"));
            groupBtn.Click += OnGroupIntoSectionClick;
            panel.Children.Add(groupBtn);

            return panel;
        }

        /// <summary>
        /// Группирует выбранные в списке модели в раздел.
        /// </summary>
        private void OnGroupIntoSectionClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_modelsListBox == null)
                {
                    return;
                }

                var selected = _modelsListBox.SelectedItems
                    .Cast<object>()
                    .Select(item => item?.ToString())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();

                _viewModel.GroupSelectedModels(selected);
            }
            catch (Exception ex)
            {
                _viewModel.SummaryText = "Ошибка группировки: " + ex.Message;
            }
        }

        /// <summary>
        /// Создаёт список уже созданных разделов с multi-select через Shift/Ctrl.
        /// </summary>
        private static UIElement CreateMappingsList()
        {
            var listBox = CreateMultiSelectListBox();
            listBox.MinHeight = 60;
            listBox.MaxHeight = 140;
            listBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Mappings"));
            listBox.ItemContainerStyle = CreateSelectedItemStyle("IsSelected");
            listBox.ItemTemplate = CreateTextItemTemplate("DisplayText");
            return WrapInRoundedBorder(listBox);
        }

        /// <summary>
        /// Создаёт кнопку удаления выбранных разделов.
        /// </summary>
        private static UIElement CreateMappingButtons()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 4)
            };

            var removeBtn = CreateStyledButton("Удалить выбранные разделы");
            removeBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding("RemoveMappingCommand"));
            panel.Children.Add(removeBtn);

            return panel;
        }

        /// <summary>
        /// Создаёт панель основных кнопок действий.
        /// </summary>
        private static UIElement CreateActionButtons()
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };

            var refresh = CreateStyledButton("Обновить данные");
            refresh.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding("RefreshCommand"));
            panel.Children.Add(refresh);

            var save = CreateStyledButton("Сохранить настройки");
            save.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding("SaveSettingsCommand"));
            panel.Children.Add(save);

            var build = CreateStyledButton("Сформировать отчёт");
            build.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding("BuildReportCommand"));
            build.Background = Brush("#FF005E96");
            build.Foreground = Brushes.White;
            build.FontWeight = FontWeights.SemiBold;
            panel.Children.Add(build);

            return panel;
        }

        /// <summary>
        /// Создаёт метку итогового статуса.
        /// </summary>
        private static UIElement CreateSummaryLabel()
        {
            var tb = new TextBlock
            {
                FontSize = 12,
                Foreground = Brush("#FF5C6875"),
                Margin = new Thickness(0, 4, 0, 0)
            };
            tb.SetBinding(TextBlock.TextProperty, new Binding("SummaryText"));
            return tb;
        }

        /// <summary>
        /// Создаёт стилизованную кнопку с закруглёнными углами.
        /// </summary>
        private static Button CreateStyledButton(string text)
        {
            return new Button
            {
                Content = text,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(12, 6, 12, 6),
                Background = Brushes.White,
                Foreground = Brush("#FF1D2733"),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        /// <summary>
        /// Создаёт стилизованное текстовое поле.
        /// </summary>
        private static TextBox CreateStyledTextBox()
        {
            return new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 2, 0, 4),
                BorderThickness = new Thickness(1),
                BorderBrush = Brush("#FFC8D1DE"),
                Background = Brushes.White
            };
        }

        /// <summary>
        /// Применяет стиль закруглённых углов к FrameworkElementFactory текстового поля.
        /// </summary>
        private static void ApplyRoundedTextBoxStyle(FrameworkElementFactory factory)
        {
            factory.SetValue(Control.PaddingProperty, new Thickness(4, 2, 4, 2));
            factory.SetValue(Control.BorderThicknessProperty, new Thickness(1));
            factory.SetValue(Control.BorderBrushProperty, Brush("#FFC8D1DE"));
        }

        /// <summary>
        /// Применяет стиль закруглённых углов к ComboBox.
        /// </summary>
        private static void ApplyRoundedComboBoxStyle(ComboBox cb)
        {
            cb.Padding = new Thickness(4, 2, 4, 2);
            cb.BorderThickness = new Thickness(1);
            cb.BorderBrush = Brush("#FFC8D1DE");
        }

        /// <summary>
        /// Создаёт замороженную кисть по hex-цвету.
        /// </summary>
        private static SolidColorBrush Brush(string hex)
        {
            var converter = new BrushConverter();
            var brush = (SolidColorBrush)converter.ConvertFromString(hex);
            brush.Freeze();
            return brush;
        }
    }
}
