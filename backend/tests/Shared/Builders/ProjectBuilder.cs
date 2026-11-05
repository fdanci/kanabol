using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;

namespace Kanabol.Tests.Shared.Builders;

public sealed class ProjectBuilder
{
    private readonly List<(Guid UserId, ProjectRole Role)> _members = [];
    private string _name = "Proiect de test";

    public ProjectBuilder WithName(string name)
    {
        _name = name;
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
        var project = new Project(_name, description: null);
        project.AddColumn("De făcut", position: 0, isDone: false);
        project.AddColumn("În lucru", position: 1, isDone: false);
        project.AddColumn("Gata", position: 2, isDone: true);
        _members.ForEach(member => project.AddMember(member.UserId, member.Role));
        return project;
    }
}
