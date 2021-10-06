using ManyConsole;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace TPLDataFlow
{
    partial class FindStringCmd : ConsoleCommand
    {
        private class Progress
        {
            public bool Done;
            public long Total;
            public long Current;
        }

        private string m_dir;
        private string m_literal;
        private bool m_searchAllFiles;
        private int m_maxDOP1 = 3;
        private int m_maxDOP2 = 20;
        private int m_maxDOP3 = Environment.ProcessorCount;
        private int m_maxDOP4 = Environment.ProcessorCount;
        private int m_workSize = 1_000_000;
        private string m_outDir;

        public FindStringCmd()
        {
            IsCommand("find-string", "Search the given string in the C# source code across all the solutions.");

            HasRequiredOption("d|dir=", "The root source directory.", v => m_dir = v.EnsureTrailingBackslash());
            HasRequiredOption("l|literal=", "A literal string to search for or a path to a file containing one literal per line.", v => m_literal = v);
            HasRequiredOption("o|outDir=", "The output directory where the matches would be saved as JSON in files with names derived from the --literal argument value.", v => m_outDir = v.EnsureTrailingBackslash());
            HasOption("a|searchAllFiles", "Search all files, including the auto generated ones. The default is false.", _ => m_searchAllFiles = true);
            HasOption("w|workSize=", "The work item size, roughly equal (# File Lines) * (# Literals). Defaults to " + m_workSize, (int v) => m_workSize = v);
            HasOption("maxDOP1=", "Maximum degree of parallelism for producing the stream of CS files (sync I/O + CPU, heavy). Defaults to " + m_maxDOP1, (int v) => m_maxDOP1 = v);
            HasOption("maxDOP2=", "Maximum degree of parallelism for producing CS file content (async I/O). Defaults to " + m_maxDOP2, (int v) => m_maxDOP2 = v);
            HasOption("maxDOP3=", "Maximum degree of parallelism for producing work items corresponding roughly to the given work size (CPU, light). Defaults to " + m_maxDOP3, (int v) => m_maxDOP3 = v);
            HasOption("maxDOP4=", "Maximum degree of parallelism for producing matching lines (CPU, heavy). Defaults to " + m_maxDOP4, (int v) => m_maxDOP4 = v);
        }

        public override int Run(string[] remainingArguments)
        {
            string fileName;
            string[] literals;
            if (File.Exists(m_literal))
            {
                fileName = Path.GetFileNameWithoutExtension(m_literal);
                literals = File.ReadAllLines(m_literal).Where(o => !string.IsNullOrWhiteSpace(o)).ToArray();
                Console.WriteLine($"Locating all the instances of the {literals.Length} literals found in the file {m_literal} in the C# code ... ");
            }
            else
            {
                Console.WriteLine($"Locating all the instances of {m_literal} in the C# code ... ");
                fileName = m_literal;
                literals = new[] { m_literal };
            }

            Run(m_dir, m_outDir + $"FindLiteral-{fileName}.json", literals, m_searchAllFiles, m_workSize, m_maxDOP1, m_maxDOP2, m_maxDOP3, m_maxDOP4);
            return 0;
        }

        private void Run(string workspaceRoot, string outFilePath, string[] literals, bool searchAllFiles, int workSize, int maxDOP1, int maxDOP2, int maxDOP3, int maxDOP4)
        {
            var res = new SortedDictionary<string, List<MatchingLine>>();
            var projects = (workspaceRoot + "build\\projects.yml").YieldAllProjects();
            var progress = new Progress();

            var taskSchedulerPair = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, Environment.ProcessorCount);

            var produceCSFiles = new TransformManyBlock<ProjectEx, CSFile>(p => YieldCSFiles(p, searchAllFiles), new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxDOP1
            });
            var produceCSFileContent = new TransformBlock<CSFile, CSFile>(CSFile.PopulateContentAsync, new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxDOP2
            });
            var produceWorkItems = new TransformManyBlock<CSFile, (CSFile CSFile, int Pos, int Length)>(csFile => csFile.YieldWorkItems(literals, workSize, progress), new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxDOP3,
                TaskScheduler = taskSchedulerPair.ConcurrentScheduler
            });
            var produceMatchingLines = new TransformManyBlock<(CSFile CSFile, int Pos, int Length), MatchingLine>(o => o.CSFile.YieldMatchingLines(literals, o.Pos, o.Length, progress), new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxDOP4,
                TaskScheduler = taskSchedulerPair.ConcurrentScheduler
            });
            var getMatchingLines = new ActionBlock<MatchingLine>(o => AddResult(res, o));

            var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

            produceCSFiles.LinkTo(produceCSFileContent, linkOptions);
            produceCSFileContent.LinkTo(produceWorkItems, linkOptions);
            produceWorkItems.LinkTo(produceMatchingLines, linkOptions);
            produceMatchingLines.LinkTo(getMatchingLines, linkOptions);

            var progressTask = Task.Factory.StartNew(() =>
            {
                var delay = literals.Length < 10 ? 1000 : 10000;
                for (; ; )
                {
                    var current = Interlocked.Read(ref progress.Current);
                    var total = Interlocked.Read(ref progress.Total);
                    Console.Write("Total = {0:n0}, Current = {1:n0}, Percents = {2:P}   \r", total, current, ((double)current) / total);
                    if (progress.Done)
                    {
                        break;
                    }
                    Thread.Sleep(delay);
                }
                Console.WriteLine();
            }, TaskCreationOptions.LongRunning);

            projects.ForEach(p => produceCSFiles.Post(p));
            produceCSFiles.Complete();
            getMatchingLines.Completion.GetAwaiter().GetResult();
            progress.Done = true;
            progressTask.GetAwaiter().GetResult();

            res.SaveAsJson(outFilePath);
        }

        private static void AddResult(IDictionary<string, List<MatchingLine>> res, MatchingLine o)
        {
            if (!res.TryGetValue(o.Literal, out var matches))
            {
                res[o.Literal] = matches = new();
            }
            var index = matches.BinarySearch(o);
            matches.Insert(~index, o);
        }

        private static IEnumerable<CSFile> YieldCSFiles(ProjectEx project, bool searchAllFiles)
        {
            var csFilePaths = project.MSBuildProject.YieldCSFiles();
            if (!searchAllFiles)
            {
                csFilePaths = csFilePaths.Where(o => !o.EndsWith(".g.cs"));
            }
            return csFilePaths.Select(csFilePath => new CSFile(project, csFilePath));
        }
    }
}
