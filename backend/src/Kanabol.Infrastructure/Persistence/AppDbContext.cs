using Kanabol.Application.Abstractions;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Common;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Search;
using Kanabol.Domain.Settings;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<BoardColumn> BoardColumns => Set<BoardColumn>();

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

    public DbSet<TaskAssignee> TaskAssignees => Set<TaskAssignee>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<TaskHistoryEntry> TaskHistoryEntries => Set<TaskHistoryEntry>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();

    public DbSet<TaskEmbedding> TaskEmbeddings => Set<TaskEmbedding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // pgvector, for the vectors of the search by meaning (SPEC section 19.1). The image has it.
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        var entityTypes = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => typeof(Entity).IsAssignableFrom(entityType.ClrType));
        foreach (var entityType in entityTypes)
        {
            var builder = modelBuilder.Entity(entityType.ClrType);

            // Entities create their own id, so EF must not read a set id as "already in the database".
            builder.Property(nameof(Entity.Id)).ValueGeneratedNever();
            builder.Ignore(nameof(Entity.DomainEvents));
        }
    }
}
