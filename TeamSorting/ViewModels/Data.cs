﻿using System.Collections.ObjectModel;
using System.Dynamic;
using System.Globalization;
using Avalonia.Controls.Notifications;
using CsvHelper;
using ReactiveUI;
using TeamSorting.Enums;
using TeamSorting.Lang;
using TeamSorting.Models;

namespace TeamSorting.ViewModels;

public class Data : ReactiveObject
{
    public ObservableCollection<DisciplineInfo> Disciplines { get; } = [];
    public ObservableCollection<Member> Members { get; } = [];
    public List<string> SortedMemberNames => Members.OrderBy(m => m.Name).Select(member => member.Name).ToList();

    private ObservableCollection<Team> _teams = [];

    public ObservableCollection<Team> Teams
    {
        get => _teams;
        set
        {
            _teams = value;
            foreach (var team in _teams)
            {
                team.WhenAnyValue(t => t.TotalScores)
                    .Subscribe(_ => this.RaisePropertyChanged(nameof(DisciplineDelta)));
            }
        }
    }

    public Dictionary<DisciplineInfo, double> DisciplineDelta
    {
        get
        {
            var dict = new Dictionary<DisciplineInfo, double>();
            foreach (var discipline in Disciplines)
            {
                var teamScores = Teams.Select(t => t.GetAverageValueByDiscipline(discipline)).ToList();
                double min = teamScores.Min();
                double max = teamScores.Max();
                double diff = double.Abs(min - max);
                dict.Add(discipline, Math.Round(diff, 2));
            }

            return dict;
        }
    }

    public string Seed { get; set; } = string.Empty;

    #region Discipline

    public bool AddDiscipline(DisciplineInfo discipline)
    {
        if (Disciplines.Any(i => i.Name == discipline.Name))
        {
            return false;
        }

        foreach (var member in Members)
        {
            AddDisciplineRecord(member, discipline, "");
        }

        Disciplines.Add(discipline);
        return true;
    }

    public bool RemoveDiscipline(DisciplineInfo discipline)
    {
        bool result = Disciplines.Remove(discipline);
        if (!result) return result;
        foreach (var member in Members)
        {
            member.RemoveDisciplineRecord(discipline.Id);
        }

        return result;
    }

    public DisciplineInfo? GetDisciplineByName(string name)
    {
        return Disciplines.FirstOrDefault(discipline => discipline.Name == name);
    }

    public DisciplineInfo? GetDisciplineById(Guid id)
    {
        return Disciplines.FirstOrDefault(discipline => discipline.Id == id);
    }

    public (double min, double max) GetDisciplineRange(DisciplineInfo discipline)
    {
        var records = GetDisciplineRecordsByDiscipline(discipline);
        var values = records.Select(record => record.DoubleValue).ToList();

        if (values.Count == 0)
        {
            return (0, 0);
        }

        return (values.Min(), values.Max());
    }

    #endregion

    #region Member

    public bool AddMember(Member member)
    {
        if (Members.Any(m => m.Name == member.Name))
        {
            return false;
        }

        foreach (var discipline in Disciplines)
        {
            AddDisciplineRecord(member, discipline, "");
        }

        Members.Add(member);
        this.RaisePropertyChanged(nameof(SortedMemberNames));
        return true;
    }

    public bool RemoveMember(Member member)
    {
        bool result = Members.Remove(member);
        if (result)
        {
            Teams.FirstOrDefault(team => team.Members.Contains(member))?.Members.Remove(member);
        }

        this.RaisePropertyChanged(nameof(SortedMemberNames));
        return result;
    }

    public Member? GetMemberByName(string name)
    {
        return Members.FirstOrDefault(member => member.Name == name);
    }

    public IEnumerable<Member> GetMembersByName(IEnumerable<string> names)
    {
        return Members.Where(member => names.Contains(member.Name));
    }

    public double GetMemberDisciplineScore(Member member, DisciplineInfo discipline)
    {
        var range = GetDisciplineRange(discipline);
        int min = discipline.SortOrder == SortOrder.Asc ? 0 : 100;
        int max = discipline.SortOrder == SortOrder.Asc ? 100 : 0;
        double value = member.GetRecord(discipline).DoubleValue;
        return (((value - range.min) / (range.max - range.min)) * (max - min)) + min;
    }

    public IEnumerable<DisciplineRecord> GetSortedRecordsByDiscipline(DisciplineInfo discipline)
    {
        var records = GetDisciplineRecordsByDiscipline(discipline).ToList();
        if (discipline.SortOrder == SortOrder.Asc)
        {
            return records.OrderBy(record => record.DoubleValue);
        }

        return records.OrderByDescending(record => record.DoubleValue);
    }

    public Dictionary<DisciplineInfo, List<DisciplineRecord>> GetSortedDisciplines()
    {
        var sortedDisciplines = new Dictionary<DisciplineInfo, List<DisciplineRecord>>();
        foreach (var discipline in Disciplines)
        {
            var sortedRecords = GetSortedRecordsByDiscipline(discipline).ToList();
            sortedDisciplines.Add(discipline, sortedRecords);
        }

        return sortedDisciplines;
    }

    public List<Member> InvalidMembersCombination()
    {
        List<Member> invalidMembers = [];
        List<Member> membersToCheck = [..Members];
        while (membersToCheck.Count > 0)
        {
            List<Member> group = [membersToCheck.First()];
            membersToCheck.Remove(group.First());
            var i = 0;
            bool newMembersAdded;
            do
            {
                newMembersAdded = false;
                var newMembers = group[i..];
                var withMembers = newMembers.SelectMany(member => GetMembersByName(member.With)).Distinct();
                foreach (var withMember in withMembers)
                {
                    if (group.Contains(withMember)) continue;
                    newMembersAdded = true;
                    group.Add(withMember);
                    membersToCheck.Remove(withMember);
                    i++;
                }
            } while (newMembersAdded);

            var notWithMembers = group.SelectMany(member => GetMembersByName(member.NotWith)).Distinct().ToList();
            bool intersectExists = group.Intersect(notWithMembers).Any();
            if (intersectExists)
            {
                invalidMembers.AddRange(group);
            }
        }

        return invalidMembers;
    }

    public IEnumerable<Member> GetWithMembers(Member currentMember)
    {
        List<Member> group = [currentMember];
        var i = 0;
        bool newMembersAdded;
        do
        {
            newMembersAdded = false;
            var newMembers = group[i..];
            var withMembers = newMembers.SelectMany(member => GetMembersByName(member.With)).Distinct();
            foreach (var withMember in withMembers)
            {
                if (group.Contains(withMember)) continue;
                newMembersAdded = true;
                group.Add(withMember);
                i++;
            }
        } while (newMembersAdded);

        group.Remove(currentMember);
        return group;
    }

    public IEnumerable<Member> GetNotWithMembers(Member currentMember)
    {
        List<Member> group = [];
        var notWithMembers = GetMembersByName(currentMember.NotWith);
        foreach (var notWithMember in notWithMembers)
        {
            var allNotWithMembers = GetWithMembers(notWithMember).ToList();
            allNotWithMembers.Add(notWithMember);

            foreach (var member in allNotWithMembers.Where(member => !group.Contains(member)))
            {
                group.Add(member);
            }
        }

        return group;
    }

    #endregion

    #region DisciplineRecord

    public IEnumerable<DisciplineRecord> GetAllRecords()
    {
        return Members.SelectMany(member => member.Records.Values);
    }

    public bool AddDisciplineRecord(Member member, DisciplineInfo discipline, string value)
    {
        if (!Members.Contains(member) || !Disciplines.Contains(discipline)) return false;
        member.AddDisciplineRecord(discipline, value);
        return true;
    }


    public IEnumerable<DisciplineRecord> GetDisciplineRecordsByDiscipline(DisciplineInfo discipline)
    {
        return GetAllRecords().Where(record => record.DisciplineInfo == discipline);
    }

    #endregion

    #region Team

    public bool AddTeam(Team team)
    {
        if (Teams.Any(t => t.Name == team.Name))
        {
            return false;
        }

        Teams.Add(team);
        return true;
    }

    public bool RemoveTeam(Team team)
    {
        return Teams.Remove(team);
    }

    public bool AddMemberToTeam(Member member, Team team)
    {
        if (!Members.Contains(member))
        {
            return false;
        }

        team.Members.Add(member);
        return true;
    }

    public bool RemoveMemberFromTeam(Member member, Team team)
    {
        return team.Members.Remove(member);
    }

    public Dictionary<Team, double> GetSortedTeamsByValueByDiscipline(DisciplineInfo discipline)
    {
        var dict = new Dictionary<Team, double>();
        foreach (var team in Teams)
        {
            double value = team.GetAverageValueByDiscipline(discipline);
            dict.Add(team, value);
        }

        return dict.OrderBy(pair => pair.Value).ToDictionary();
    }

    public ObservableCollection<Team> CreateTeams(int count)
    {
        Teams.Clear();
        for (var i = 0; i < count; i++)
        {
            AddTeam(new Team(string.Format(Resources.Data_TeamName_Template, i + 1)));
        }

        return Teams;
    }

    public void SortTeamsByCriteria(DisciplineInfo? disciplineInfo, SortOrder sortOrder)
    {
        foreach (var team in Teams)
        {
            team.SortCriteria = new MemberSortCriteria(disciplineInfo, sortOrder);
        }
    }

    #endregion

    #region CSV

    public async Task<ReturnMessage> LoadFromFile(StreamReader inputFile)
    {
        var csv = new CsvReader(inputFile, CultureInfo.InvariantCulture);

        await csv.ReadAsync(); //Read first line
        csv.ReadHeader(); //load header
        var returnMessage = ValidateCsvHeader(csv);
        if (returnMessage is not null)
        {
            return returnMessage;
        }

        await csv.ReadAsync(); //read next line

        returnMessage = LoadDisciplinesInfo(csv);
        if (returnMessage?.NotificationType == NotificationType.Error)
        {
            ClearData();
            returnMessage.Message = $"{Resources.Data_LoadFromFile_Error}\n{returnMessage.Message}";
            return returnMessage;
        }

        try
        {
            LoadMembersData(csv);
        }
        catch (Exception e)
        {
            ClearData();
            return new ReturnMessage(NotificationType.Error,
                $"{Resources.Data_LoadFromFile_Error}\n{e.Message}");
        }

        return returnMessage ?? new ReturnMessage(NotificationType.Success, Resources.Data_LoadFromFile_Success);
    }

    private static ReturnMessage? ValidateCsvHeader(CsvReader csv)
    {
        List<string> missingFields = [];
        if (!csv.HeaderRecord.Contains(nameof(Member.Name)))
        {
            missingFields.Add(nameof(Member.Name));
        }

        if (!csv.HeaderRecord.Contains(nameof(Member.With)))
        {
            missingFields.Add(nameof(Member.With));
        }

        if (!csv.HeaderRecord.Contains(nameof(Member.NotWith)))
        {
            missingFields.Add(nameof(Member.NotWith));
        }

        if (missingFields.Count > 0)
        {
            return new ReturnMessage(NotificationType.Error,
                Resources.Data_ValidateCsvHeader_MissingColumns_Error + string.Join('\n', missingFields));
        }

        var duplicateColumns = csv.HeaderRecord.GroupBy(x => x)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateColumns.Count > 0)
        {
            return new ReturnMessage(NotificationType.Error,
                Resources.Data_ValidateCsvHeader_DuplicateColumns_Error + string.Join('\n', duplicateColumns));
        }

        return null;
    }

    private void ClearData()
    {
        Members.Clear();
        Disciplines.Clear();
        Teams.Clear();
    }

    private ReturnMessage? LoadDisciplinesInfo(CsvReader csv)
    {
        var disciplines = csv.HeaderRecord.Except([
            nameof(Member.Name),
            nameof(Member.With),
            nameof(Member.NotWith)
        ]).Select(d => new DisciplineInfo(d)).ToList();

        var returnMessage = ReadDisciplineDataTypes(disciplines, csv);
        if (returnMessage?.NotificationType == NotificationType.Error)
        {
            return returnMessage;
        }

        csv.Read();
        returnMessage = ReadDisciplineSortTypes(disciplines, csv);
        if (returnMessage?.NotificationType == NotificationType.Error)
        {
            return returnMessage;
        }

        foreach (var discipline in disciplines)
        {
            AddDiscipline(discipline);
        }

        return null;
    }

    private static ReturnMessage? ReadDisciplineDataTypes(IEnumerable<DisciplineInfo> disciplines, CsvReader csv)
    {
        foreach (var discipline in disciplines)
        {
            string? value = csv[discipline.Name];
            try
            {
                discipline.DataType = Enum.Parse<DisciplineDataType>(value);
            }
            catch (ArgumentException)
            {
                return new ReturnMessage(NotificationType.Error,
                    string.Format(Resources.Data_ReadDisciplineDataTypes_WrongDisciplineDataTypes_Error, value,
                        discipline.Name, string.Join(", ", Enum.GetValues<DisciplineDataType>())));
            }
            catch (Exception ex)
            {
                return new ReturnMessage(NotificationType.Error,
                    string.Format(Resources.Data_ReadDisciplineDataTypes_ReadingError, discipline.Name, ex.Message));
            }
        }

        return null;
    }

    private static ReturnMessage? ReadDisciplineSortTypes(IEnumerable<DisciplineInfo> disciplines, CsvReader csv)
    {
        foreach (var discipline in disciplines)
        {
            string value = csv[discipline.Name];
            try
            {
                discipline.SortOrder = Enum.Parse<SortOrder>(csv[discipline.Name]);
            }
            catch (ArgumentException)
            {
                return new ReturnMessage(NotificationType.Error,
                    string.Format(Resources.Data_ReadDisciplineSortTypes_WrongDisciplineSortOrder_Error, value,
                        discipline.Name, string.Join(", ", Enum.GetValues<SortOrder>())));
            }
            catch (Exception ex)
            {
                return new ReturnMessage(NotificationType.Error,
                    string.Format(Resources.Data_ReadDisciplineDataTypes_ReadingError, discipline.Name, ex.Message));
            }
        }

        return null;
    }

    private void LoadMembersData(CsvReader csv)
    {
        while (csv.Read())
        {
            var withMembers = csv[nameof(Member.With)].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            var notWithMembers = csv[nameof(Member.NotWith)].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            var member = new Member(name: csv[nameof(Member.Name)]);
            member.AddWithMembers(withMembers);
            member.AddNotWithMembers(notWithMembers);
            AddMember(member);

            foreach (var disciplineInfo in Disciplines)
            {
                AddDisciplineRecord(member, disciplineInfo, csv[disciplineInfo.Name]);
            }
        }
    }

    public void WriteTeamsToCsv(string path)
    {
        var records = new List<dynamic>();

        int maxMembers = Teams.MaxBy(team => team.Members.Count)!.Members.Count;
        for (var i = 0; i < maxMembers; i++)
        {
            IDictionary<string, object> record = new ExpandoObject()!;
            foreach (var team in Teams)
            {
                if (i >= team.Members.Count)
                {
                    record.Add(team.Name, string.Empty);
                    continue;
                }

                var member = team.Members[i];
                record.Add(team.Name, member.Name);
            }

            records.Add(record);
        }

        using var writer = new StreamWriter(path);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(records);
    }

    #endregion
}