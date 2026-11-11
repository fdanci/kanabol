namespace Kanabol.Domain.Authorization;

public static class Permissions
{
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string ProjectsCreate = "projects.create";
    public const string ProjectsViewAll = "projects.view_all";
    public const string ProjectsManage = "projects.manage";
    public const string TasksCreate = "tasks.create";
    public const string TasksArchive = "tasks.archive";
    public const string ReportsView = "reports.view";

    /// <summary>The general settings, the work schedule among them (group 22).</summary>
    public const string SettingsManage = "settings.manage";

    public static IReadOnlyList<string> All { get; } =
    [
        UsersManage,
        RolesManage,
        ProjectsCreate,
        ProjectsViewAll,
        ProjectsManage,
        TasksCreate,
        TasksArchive,
        ReportsView,
        SettingsManage,
    ];
}
