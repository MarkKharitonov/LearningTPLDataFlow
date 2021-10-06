using ManyConsole;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks.Dataflow;

namespace TPLDataFlow
{
    partial class FindStringCmd : ConsoleCommand
    {
        private string m_dir;
        private string m_literal;
        private int m_maxDOP1 = 1;
        private int m_maxDOP2 = 1;
        private bool m_quiet;

        public FindStringCmd()
        {
            IsCommand("find-string", "Search the given string in the C# source code across all the solutions.");

            HasRequiredOption("d|dir=", "The root source directory.", v => m_dir = Path.GetFullPath(v).EnsureTrailingBackslash());
            HasRequiredOption("l|literal=", "A literal string to search for.", v => m_literal = v);
            HasOption("q|quiet", "Show only timings", _ => m_quiet = true);
            HasOption("maxDOP1=", "", (int v) => m_maxDOP1 = v);
            HasOption("maxDOP2=", "", (int v) => m_maxDOP2 = v);
        }

        public override int Run(string[] remainingArguments)
        {
            if (!m_quiet)
            {
                Console.WriteLine($"Locating all the instances of {m_literal} in the C# code ... ");
            }
            var res = Run(m_dir, m_literal, m_maxDOP1, m_maxDOP2);
            if (!m_quiet)
            {
                res.ForEach(o => Console.WriteLine(o.ToString(m_dir)));
            }
            return 0;
        }

        private List<MatchingLine> Run(string workspaceRoot, string literal, int maxDOP1 = 1, int maxDOP2 = 1)
        {
            var res = new List<MatchingLine>();
            var projects = (workspaceRoot + "build\\projects.yml").YieldAllProjects();

            var produceCSFiles = new TransformManyBlock<ProjectEx, CSFile>(YieldCSFiles, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = maxDOP1 });
            var produceMatchingLines = new TransformManyBlock<CSFile, MatchingLine>(csFile => csFile.YieldMatchingLines(literal), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = maxDOP2 });
            var getMatchingLines = new ActionBlock<MatchingLine>(res.Add);

            var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

            produceCSFiles.LinkTo(produceMatchingLines, linkOptions);
            produceMatchingLines.LinkTo(getMatchingLines, linkOptions);

            projects.ForEach(p => produceCSFiles.Post(p));
            produceCSFiles.Complete();
            getMatchingLines.Completion.Wait();

            return res;
        }

        private static IEnumerable<CSFile> YieldCSFiles(ProjectEx project) =>
            project.MSBuildProject.YieldCSFiles().Select(csFilePath => new CSFile(project, csFilePath));
    }
}
