﻿using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Projektanker.Icons.Avalonia;
using TeamSorting.Enums;
using TeamSorting.Models;
using TeamSorting.ViewModels;
using MenuItem = Avalonia.Controls.MenuItem;

namespace TeamSorting.Views;

public partial class InputView : UserControl
{
    private WindowNotificationManager? _notificationManager;

    public InputView()
    {
        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        if (DataContext is InputViewModel vm)
        {
            AddDisciplinesToDataGrid();
            vm.Data.Disciplines.CollectionChanged += DisciplinesOnCollectionChanged;
        }

        _notificationManager = new WindowNotificationManager(TopLevel.GetTopLevel(this))
        {
            Position = NotificationPosition.BottomRight,
            Margin = new Thickness(0, 0, 0, 20),
            MaxItems = 3
        };

        base.OnInitialized();
    }

    private void DisciplinesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            if (e.OldItems is null || e.OldItems.Count <= 0) return;
            foreach (DisciplineInfo discipline in e.OldItems)
            {
                var column =
                    MemberGrid.Columns.First(column => column.Tag is Guid columnId && columnId == discipline.Id);
                MemberGrid.Columns.Remove(column);
            }
        }
    }

    [Localizable(false)]
    private async void LoadData_OnClick(object? sender, RoutedEventArgs e)
    {
        var context = (InputViewModel)DataContext!;
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            return;
        }

        var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Lang.Resources.InputView_LoadData_FileDialog,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("csv") { Patterns = ["*.csv"] }
            }
        });

        if (file.Count <= 0) return;
        await using var stream = await file[0].OpenReadAsync();
        using var streamReader = new StreamReader(stream);
        var loadDataErrors = context.Data.LoadFromFile(streamReader);
        if (loadDataErrors.Count != 0)
        {
            var window = TopLevel.GetTopLevel(this);
            if (window is MainWindow { DataContext: MainWindowViewModel } mainWindow)
            {
                var dialog = new CsvErrorDialog { DataContext = new CsvErrorViewModel(loadDataErrors) };
                await dialog.ShowDialog(mainWindow);
            }
        }

        AddDisciplinesToDataGrid();
    }

    [Localizable(false)]
    private void AddDisciplinesToDataGrid()
    {
        var context = (InputViewModel)DataContext!;

        foreach (var discipline in context.Data.Disciplines)
        {
            if (DataGridContainsDisciplineColumn(MemberGrid, discipline))
            {
                continue;
            }

            var column = new DataGridTextColumn
            {
                Tag = discipline.Id,
                Header = CreateDisciplineColumnHeader(discipline),
                Binding = new Binding($"{nameof(Member.Records)}[{discipline.Id}].{nameof(DisciplineRecord.RawValue)}"),
                IsReadOnly = false
            };
            MemberGrid.Columns.Add(column);
        }
    }

    private bool DataGridContainsDisciplineColumn(DataGrid dataGrid, DisciplineInfo discipline)
    {
        var column = dataGrid.Columns.FirstOrDefault(column => column.Tag is Guid id && id == discipline.Id);
        return column is not null;
    }

    private static DockPanel CreateDisciplineColumnHeader(DisciplineInfo discipline)
    {
        var panel = new DockPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Tag = discipline.Id
        };

        var removeButton = new Button()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            [DockPanel.DockProperty] = Dock.Left,
            [Attached.IconProperty] = "mdi-close",
            Margin = new Thickness(0, 0, 5, 0),
            [ToolTip.TipProperty] = Lang.Resources.InputView_RemoveDiscipline_Button
        };
        removeButton.Click += RemoveButtonOnClick;
        panel.Children.Add(removeButton);

        var iconSort = new Icon
        {
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Right,
            [DockPanel.DockProperty] = Dock.Right
        };
        var iconSortBinding = new Binding
        {
            Source = discipline,
            Path = nameof(DisciplineInfo.SortOrder),
            Converter = new Converters.DisciplineSortToIconConverter()
        };
        iconSort.Bind(Icon.ValueProperty, iconSortBinding);
        panel.Children.Add(iconSort);

        var iconType = new Icon
        {
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Right,
            [DockPanel.DockProperty] = Dock.Right
        };
        var iconTypeBinding = new Binding
        {
            Source = discipline,
            Path = nameof(DisciplineInfo.DataType),
            Converter = new Converters.DisciplineTypeToIconConverter()
        };
        iconType.Bind(Icon.ValueProperty, iconTypeBinding);
        panel.Children.Add(iconType);

        var text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 0, 10, 0),
            [DockPanel.DockProperty] = Dock.Left
        };
        var textBinding = new Binding
        {
            Source = discipline,
            Path = nameof(DisciplineInfo.Name)
        };
        text.Bind(TextBlock.TextProperty, textBinding);
        panel.Children.Add(text);

        return panel;
    }

    private static void RemoveButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            var panel = button.FindLogicalAncestorOfType<DockPanel>();
            if (panel is { DataContext: InputViewModel context, Tag: Guid disciplineId })
            {
                var discipline = context.Data.GetDisciplineById(disciplineId);
                if (discipline is not null)
                {
                    context.Data.RemoveDiscipline(discipline);
                }
            }
        }
    }

    private void SortToTeams_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InputViewModel context || sender is not Button button) return;

        button.IsEnabled = false;
        Cursor = new Cursor(StandardCursorType.Wait);

        var numberOfTeams = (int)(NumberOfTeams.Value ?? 1);
        var sortResult = context.Sorter.Sort(context.Data.Members.ToList(), numberOfTeams, context.Data.Seed);
        context.Data.Teams = new ObservableCollection<Team>(sortResult.teams);
        context.Data.Seed = sortResult.seed ?? string.Empty;

        var window = TopLevel.GetTopLevel(this);
        if (window is MainWindow { DataContext: MainWindowViewModel mainWindowViewModel })
        {
            mainWindowViewModel.SwitchToTeamsView();
        }

        button.IsEnabled = true;
        Cursor = Cursor.Default;
    }

    private void NewMemberTextBox_OnClick(object? sender, RoutedEventArgs e)
    {
        AddMember();
    }

    private void NewMemberTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddMember();
        }
    }

    private void AddMember()
    {
        var context = (InputViewModel)DataContext!;
        var member = new Member(context.NewMemberName);
        context.Data.AddMember(member);
        context.NewMemberName = string.Empty;
        MemberGrid.ScrollIntoView(member, MemberGrid.Columns.First());
    }

    private void RemoveMemberButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            var gridRow = button.FindLogicalAncestorOfType<DataGridRow>();
            if (gridRow?.DataContext is Member member)
            {
                var context = (InputViewModel)DataContext!;
                context.Data.RemoveMember(member);
            }
        }
    }

    private void AddDisciplineTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddDiscipline();
        }
    }

    private void AddDisciplineButton_OnClick(object? sender, RoutedEventArgs e)
    {
        AddDiscipline();
    }

    private void AddDiscipline()
    {
        var context = (InputViewModel)DataContext!;
        var disciplineType = (DisciplineDataType)(DisciplineTypeComboBox.SelectedItem ?? DisciplineDataType.Number);
        var disciplineSortOrder = (SortOrder)(DisciplineSortOrderComboBox.SelectionBoxItem ?? SortOrder.Asc);
        var discipline = new DisciplineInfo(context.NewDisciplineName)
        {
            DataType = disciplineType,
            SortOrder = disciplineSortOrder
        };
        context.Data.AddDiscipline(discipline);
        //TODO error handling when discipline with same name exists 
        context.NewDisciplineName = string.Empty;
        AddDisciplinesToDataGrid();
        if (context.Data.Members.Count > 0)
        {
            //TODO scroll even if members are empty
            MemberGrid.ScrollIntoView(null, MemberGrid.Columns.Last());
        }
    }

    private void ComboBox_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private void RemoveWithMemberButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Member removeName } button)
        {
            var parent = button.FindLogicalAncestorOfType<ItemsControl>();
            if (parent?.DataContext is Member member)
            {
                member.RemoveWithMember(removeName);
            }
        }
    }

    private void RemoveNotWithMemberButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Member removeName } button)
        {
            var parent = button.FindLogicalAncestorOfType<ItemsControl>();
            if (parent?.DataContext is Member member)
            {
                member.RemoveNotWithMember(removeName);
            }
        }
    }

    private void AddNotWithMemberMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: Member notWithMember } menuItem)
        {
            var row = menuItem.FindLogicalAncestorOfType<DataGridRow>();
            if (row?.DataContext is Member member)
            {
                member.AddNotWithMember(notWithMember);
            }
        }
    }

    private void AddWithMemberMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: Member withMember } menuItem)
        {
            var row = menuItem.FindLogicalAncestorOfType<DataGridRow>();
            if (row?.DataContext is Member member)
            {
                member.AddWithMember(withMember);
            }
        }
    }
}