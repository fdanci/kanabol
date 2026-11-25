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

    /// <summary>Adding, renaming, moving and deleting a board's columns, for a real member of the project (group 23).</summary>
    public const string ColumnsManage = "columns.manage";

    /// <summary>
    /// The search by meaning and, from step 2, the chat (group 25, SPEC section 19.6). It reads over every project
    /// on its own, without projects.view_all, so it's for whoever may see everything.
    /// </summary>
    public const string ChatUse = "chat.use";

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
        ColumnsManage,
        ChatUse,
    ];
}
