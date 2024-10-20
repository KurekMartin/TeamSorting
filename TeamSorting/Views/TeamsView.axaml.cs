﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TeamSorting.Controls;
using TeamSorting.Enums;
using TeamSorting.Models;
using TeamSorting.ViewModels;

namespace TeamSorting.Views;

public partial class TeamsView : UserControl
{
    public TeamsView()
    {
        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        if (DataContext is TeamsViewModel context)
        {
            context.NotificationManager = new WindowNotificationManager(TopLevel.GetTopLevel(this))
            {
                Position = NotificationPosition.BottomRight,
                Margin = new Thickness(0,0,0,35)
            };

            var nameItem = new ComboBoxSortCriteria(Lang.Resources.InputView_DataGrid_ColumnHeader_Name, null);

            List<ComboBoxSortCriteria> items = [nameItem];
            items.AddRange(context.Data.Disciplines.Select(discipline =>
                new ComboBoxSortCriteria(discipline.Name, discipline)));

            SortCriteriaComboBox.ItemsSource = items.OrderBy(criteria => criteria.DisplayText).ToList();
            SortCriteriaComboBox.SelectedValue = nameItem;
        }
    }

    private async void Back_OnClick(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this);
        if (window is MainWindow { DataContext: MainWindowViewModel mainWindowViewModel } mainWindow)
        {
            var dialog = new WarningDialog(Lang.Resources.TeamsView_Back_WarningDialog)
            {
                Position = mainWindow.Position //fix for WindowStartupLocation="CenterOwner" not working
            };
            var result = await dialog.ShowDialog<WarningDialogResult>(mainWindow);
            if (result == WarningDialogResult.Cancel)
            {
                return;
            }

            if (DataContext is TeamsViewModel teamsViewModel)
            {
                teamsViewModel.Data.Seed = string.Empty;
            }

            mainWindowViewModel.SwitchToInputView();
        }
    }

    private async void ExportTeamsToCsv_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TeamsViewModel context) return;
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            Title = Lang.Resources.TeamsView_ExportTeamsToCsv_FileDialogTitle,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("csv") { Patterns = ["*.csv"] }
            }
        });

        if (file is null)
        {
            return;
        }

        var fileSaved = false;
        try
        {
            context.Data.WriteTeamsToCsv(file.Path.LocalPath);
            fileSaved = true;
        }
        catch (Exception exception)
        {
            string message = string.Format(Lang.Resources.TeamsView_CsvExport_Error, exception.Message);
            context.NotificationManager?.Show(message, NotificationType.Error);
        }

        if (fileSaved)
        {
            context.NotificationManager?.Show(Lang.Resources.TeamsView_CsvExport_Success, NotificationType.Success);
        }
    }

    private void MemberTeamMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TeamsViewModel context) return;
        if (sender is not MenuItem menuItem) return;
        var memberCard = menuItem.FindLogicalAncestorOfType<MemberCard>();
        if (memberCard is { DataContext: Member member })
        {
            var team = context.Data.Teams.First(team =>
                string.Equals(team.Name, menuItem.Header as string, StringComparison.InvariantCultureIgnoreCase));
            member.MoveToTeam(team);
        }
    }

    private void ShowMemberDetailsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var cards = this.GetVisualDescendants().OfType<MemberCard>();
        foreach (var card in cards)
        {
            card.ShowDetail = true;
        }
    }

    private void HideMemberDetailsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var cards = this.GetVisualDescendants().OfType<MemberCard>();
        foreach (var card in cards)
        {
            card.ShowDetail = false;
        }
    }

    private void SortCriteriaComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is TeamsViewModel context && e.AddedItems.Count > 0 &&
            e.AddedItems[0] is ComboBoxSortCriteria item)
        {
            context.TeamsSortCriteria =
                new MemberSortCriteria((DisciplineInfo?)item.Value, context.TeamsSortCriteria.SortOrder);
        }
    }

    private void ToggleButton_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is IconRadioButton { IsChecked: true } && DataContext is TeamsViewModel context)
        {
            if (sender.Equals(SortAscRadioButton))
            {
                context.TeamsSortCriteria = new MemberSortCriteria(context.TeamsSortCriteria.Discipline, SortOrder.Asc);
            }
            else if (sender.Equals(SortDescRadioButton))
            {
                context.TeamsSortCriteria =
                    new MemberSortCriteria(context.TeamsSortCriteria.Discipline, SortOrder.Desc);
            }
        }
    }
}