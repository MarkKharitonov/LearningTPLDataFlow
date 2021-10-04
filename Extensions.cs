using Microsoft.Build.Construction;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Project = Microsoft.Build.Evaluation.Project;

namespace TPLDataFlow
{
    public static class Extensions
    {
        public static void ForEach<T>(this IEnumerable<T> items, Action<T> action)
        {
            foreach (var item in items)
            {
                action(item);
            }
        }

        public static IEnumerable<ProjectEx> YieldAllProjects(this string slnListFilePath, bool includeAll = false)
        {
            var visitedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return slnListFilePath.YieldSolutionFiles().SelectMany(slnFilePath => slnFilePath.YieldSolutionProjects(includeAll));
        }

        public static IEnumerable<string> YieldSolutionFiles(this string slnListFilePath) => File
            .ReadAllLines(slnListFilePath)
            .Where(line => line.StartsWith("      - name: "))
            .Select(line => line.Substring("      - name: ".Length))
            .Select(sln => Path.GetFullPath($"{slnListFilePath}\\..\\..\\{sln}.sln"));

        public static IEnumerable<ProjectEx> YieldSolutionProjects(this string slnFilePath, bool includeAll = false) => slnFilePath
            .YieldProjectFiles()
            .Select(projFilePath => ProjectEx.Create(projFilePath, includeAll))
            .Where(p => p != null);

        public static IEnumerable<string> YieldProjectFiles(this string slnFilePath) => SolutionFile
            .Parse(slnFilePath)
            .ProjectsInOrder
            .Select(o => o.AbsolutePath)
            .Where(o => o.EndsWith(".csproj"));

        public static IEnumerable<string> YieldCSFiles(this Project project) => project.GetItems("Compile").Select(item => item.GetMetadataValue("FullPath"));

        public static string EnsureTrailingBackslash(this string s) => (s == null || s == "" || s[^1] != '\\') ? s + '\\' : s;
    }
}
