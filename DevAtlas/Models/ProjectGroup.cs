using DevAtlas.Services;
using System.Collections.ObjectModel;

namespace DevAtlas.Models
{
    public class ProjectGroup
    {
        public string GroupName { get; set; } = "";
        public string Icon { get; set; } = "";
        public string AccentColor { get; set; } = "#6B7280";
        public string BadgeBackground { get; set; } = "#F3F4F6";
        public string BadgeForeground { get; set; } = "#6B7280";
        public ObservableCollection<ProjectInfo> Projects { get; set; } = new();
        public int Count => Projects.Count;

        public static List<ProjectGroup> GroupForExplorer(IEnumerable<ProjectInfo> projects)
        {
            var projectList = projects.ToList();
            var wslProjects = projectList
                .Where(project => project.IsWslProject)
                .OrderByDescending(project => project.LastModified)
                .ToList();
            var localProjects = projectList
                .Where(project => !project.IsWslProject)
                .ToList();

            var groups = new List<ProjectGroup>();
            if (wslProjects.Count > 0)
            {
                groups.Add(CreateGroup(
                    LanguageManager.Instance["MessageWslProjects"],
                    "WSL",
                    "#0F172A",
                    "#E0F2FE",
                    "#075985",
                    wslProjects));
            }

            groups.AddRange(GroupByLastModified(localProjects));
            return groups;
        }

        public static ProjectGroup CreateSingleGroup(string groupName, string icon, IEnumerable<ProjectInfo> projects)
        {
            return CreateGroup(
                groupName,
                icon,
                "#0F172A",
                "#E0F2FE",
                "#075985",
                projects.OrderByDescending(project => project.LastModified));
        }

        public static List<ProjectGroup> GroupByLastModified(IEnumerable<ProjectInfo> projects)
        {
            var lm = LanguageManager.Instance;
            var now = DateTime.Now;
            var today = now.Date;
            var yesterday = today.AddDays(-1);
            var thisWeekStart = today.AddDays(-7);
            var thisMonthStart = today.AddDays(-30);
            var threeMonthsAgo = today.AddMonths(-3);

            var groups = new Dictionary<string, (string icon, string accent, string badgeBg, string badgeFg, List<ProjectInfo> items)>
            {
                [lm["TimeGroupToday"]] = ("🔥", "#F59E0B", "#FEF3C7", "#92400E", new()),
                [lm["TimeGroupYesterday"]] = ("📅", "#3B82F6", "#DBEAFE", "#1D4ED8", new()),
                [lm["TimeGroupThisWeek"]] = ("📆", "#8B5CF6", "#EDE9FE", "#6D28D9", new()),
                [lm["TimeGroupThisMonth"]] = ("🗓️", "#10B981", "#D1FAE5", "#065F46", new()),
                [lm["TimeGroup1to3MonthsAgo"]] = ("📦", "#F97316", "#FFEDD5", "#9A3412", new()),
                [lm["TimeGroupOlder"]] = ("🗃️", "#6B7280", "#F3F4F6", "#374151", new()),
            };

            foreach (var project in projects)
            {
                var lastMod = project.LastModified.Date;

                if (lastMod >= today)
                    groups[lm["TimeGroupToday"]].items.Add(project);
                else if (lastMod >= yesterday)
                    groups[lm["TimeGroupYesterday"]].items.Add(project);
                else if (lastMod >= thisWeekStart)
                    groups[lm["TimeGroupThisWeek"]].items.Add(project);
                else if (lastMod >= thisMonthStart)
                    groups[lm["TimeGroupThisMonth"]].items.Add(project);
                else if (lastMod >= threeMonthsAgo)
                    groups[lm["TimeGroup1to3MonthsAgo"]].items.Add(project);
                else
                    groups[lm["TimeGroupOlder"]].items.Add(project);
            }

            var result = new List<ProjectGroup>();
            foreach (var kvp in groups)
            {
                if (kvp.Value.items.Count > 0)
                {
                    var group = new ProjectGroup
                    {
                        GroupName = kvp.Key,
                        Icon = kvp.Value.icon,
                        AccentColor = kvp.Value.accent,
                        BadgeBackground = kvp.Value.badgeBg,
                        BadgeForeground = kvp.Value.badgeFg,
                    };
                    // Sort by LastModified descending within each group
                    foreach (var p in kvp.Value.items.OrderByDescending(p => p.LastModified))
                    {
                        group.Projects.Add(p);
                    }
                    result.Add(group);
                }
            }

            return result;
        }

        private static ProjectGroup CreateGroup(
            string groupName,
            string icon,
            string accentColor,
            string badgeBackground,
            string badgeForeground,
            IEnumerable<ProjectInfo> projects)
        {
            var group = new ProjectGroup
            {
                GroupName = groupName,
                Icon = icon,
                AccentColor = accentColor,
                BadgeBackground = badgeBackground,
                BadgeForeground = badgeForeground
            };

            foreach (var project in projects)
            {
                group.Projects.Add(project);
            }

            return group;
        }
    }
}
