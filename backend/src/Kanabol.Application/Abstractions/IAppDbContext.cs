using Kanabol.Domain.Authorization;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Settings;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<Role> Roles { get; }

    DbSet<Permission> Permissions { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<Project> Projects { get; }

    DbSet<ProjectMember> ProjectMembers { get; }

    DbSet<BoardColumn> BoardColumns { get; }

    DbSet<TaskItem> TaskItems { get; }

    DbSet<TaskAssignee> TaskAssignees { get; }

    DbSet<Comment> Comments { get; }

    DbSet<Attachment> Attachments { get; }

    DbSet<TaskHistoryEntry> TaskHistoryEntries { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<WorkSchedule> WorkSchedules { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
