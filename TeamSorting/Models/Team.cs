﻿using System.Collections.ObjectModel;

namespace TeamSorting.Models;

public class Team(string name)
{
    public string Name { get; set; } = name;
    public ObservableCollection<Member> Members { get; init; } = [];
    public bool IsValid => IsValidCheck(Members);

    public Dictionary<DisciplineInfo, double> TotalScores
    {
        get
        {
            return Members.SelectMany(member => member.Records.Values)
                .GroupBy(record => record.DisciplineInfo)
                .ToDictionary(g => g.Key, g => Math.Round(g.Sum(record => record.DoubleValue), 2));
        }
    }

    public double GetTotalValueByDiscipline(DisciplineInfo discipline)
    {
        var records = Members.Select(member => member.GetRecord(discipline));
        return records.Sum(record => record.DoubleValue);
    }

    public static bool IsValidCheck(IEnumerable<Member> members)
    {
        var invalidMembers = GetInvalidMembers(members);
        return invalidMembers.invalidWith.Count == 0 && invalidMembers.invalidNotWith.Count == 0;
    }

    public static (List<string> invalidWith, List<string> invalidNotWith) GetInvalidMembers(IEnumerable<Member> members)
    {
        var memberList = members.ToList();
        var memberNames = memberList.Select(member => member.Name).ToList();
        var with = memberList.SelectMany(member => member.With).ToList();
        var notWith = memberList.SelectMany(member => member.NotWith);

        return (with.Except(memberNames).ToList(), memberNames.Intersect(notWith).ToList());
    }
}