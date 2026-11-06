using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;

namespace Kanabol.Tests.Shared.Builders;

public sealed class ProjectBuilder
{
    private static int _sequence;

    private readonly List<(Guid UserId, ProjectRole Role)> _members = [];
    private string _name = "Proiect de test";
    private string _key = NextKey();
    private string? _description;

    public ProjectBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public ProjectBuilder WithKey(string key)
    {
        _key = key;
        return this;
    }

    public ProjectBuilder WithDescription(string? description)
    {
        _description = description;
        return this;
    }

    public ProjectBuilder OwnedBy(User user)
    {
        _members.Add((user.Id, ProjectRole.Owner));
        return this;
    }

    public ProjectBuilder WithMember(User user)
    {
        _members.Add((user.Id, ProjectRole.Member));
        return this;
    }

    public Project Build()
    {
        var project = new Project(_name, _key, _description);
        project.AddColumn("De făcut", isDone: false);
        project.AddColumn("În lucru", isDone: false);
        project.AddColumn("Gata", isDone: true);
        _members.ForEach(member => project.AddMember(member.UserId, member.Role));
        return project;
    }

    /// <summary>Keys are unique, so each project gets its own: P and four letters, from PAAAA on.</summary>
    private static string NextKey()
    {
        var number = Interlocked.Increment(ref _sequence);
        var letters = new char[4];
        for (var index = letters.Length - 1; index >= 0; index--)
        {
            letters[index] = (char)('A' + (number % 26));
            number /= 26;
        }

        return $"P{new string(letters)}";
    }
}
