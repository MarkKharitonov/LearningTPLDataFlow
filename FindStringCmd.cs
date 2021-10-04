﻿using CSTool.ObjectModel;
using ManyConsole;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks.Dataflow;

namespace CSTool
{
    class FindStringCmd : ConsoleCommand
    {
        private struct CSFile
        {
            public readonly ProjectEx Project;
            public readonly string FilePath;

            public CSFile(ProjectEx project, string filePath)
            {
                Project = project;
                FilePath = filePath;
            }

            public IEnumerable<MatchingLine> YieldMatchingLines(string literal)
            {
                using var sr = new StreamReader(FilePath);
                string line;
                int lineNo = 0;
                while ((line = sr.ReadLine()) != null)
                {
                    ++lineNo;
                    int pos = -1;
                    while ((pos = line.IndexOf(literal, pos + 1)) >= 0)
                    {
                        var endPos = pos + literal.Length;
                        if ((pos == 0 || !char.IsLetterOrDigit(line[pos - 1]) && line[pos - 1] != '_') &&
                            (endPos == line.Length || !char.IsLetterOrDigit(line[endPos]) && line[endPos] != '_'))
                        {
                            yield return new MatchingLine(this, line, lineNo);
                            break;
                        }
                    }
                }
            }
        }

        private struct MatchingLine
        {
            public readonly CSFile CSFile;
            public readonly string Line;
            public readonly int LineNo;

            public MatchingLine(CSFile csFile, string line, int lineNo)
            {
                CSFile = csFile;
                Line = line;
                LineNo = lineNo;
            }

            public string ToString(string workspaceRoot) => $" *** {CSFile.Project} : {CSFile.FilePath[workspaceRoot.Length..]} : {LineNo}\n --> {Line}";
        }

        private string m_dir;
        private string m_literal;
        private int m_maxDOP1 = 1;
        private int m_maxDOP2 = 1;

        public FindStringCmd()
        {
            IsCommand("find-string", "Search the given string in the C# source code across all the solutions.");

            HasRequiredOption("d|dir=", "The root source directory.", v => m_dir = Path.GetFullPath(v));
            HasRequiredOption("l|literal=", "A literal string to search for.", v => m_literal = v);
            HasOption("maxDOP1=", "", (int v) => m_maxDOP1 = v);
            HasOption("maxDOP2=", "", (int v) => m_maxDOP2 = v);
        }

        public override int Run(string[] remainingArguments)
        {
            Run(m_dir + "\\", m_literal, m_maxDOP1, m_maxDOP2);
            return 0;
        }

        private void Run(string workspaceRoot, string literal, int maxDOP1 = 1, int maxDOP2 = 1)
        {
            var projects = (workspaceRoot + "build\\projects.yml").YieldAllProjects();

            var produceCSFiles = new TransformManyBlock<ProjectEx, CSFile>(YieldCSFiles, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = maxDOP1 });
            var produceMatchingLines = new TransformManyBlock<CSFile, MatchingLine>(csFile => csFile.YieldMatchingLines(literal), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = maxDOP2 });
            var getMatchingLines = new ActionBlock<MatchingLine>(o => Console.WriteLine(o.ToString(workspaceRoot)));

            var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

            produceCSFiles.LinkTo(produceMatchingLines, linkOptions);
            produceMatchingLines.LinkTo(getMatchingLines, linkOptions);

            Console.WriteLine($"Locating all the instances of {literal} in the C# code ... ");
            var sw = Stopwatch.StartNew();

            projects.ForEach(p => produceCSFiles.Post(p));
            produceCSFiles.Complete();
            getMatchingLines.Completion.Wait();

            sw.Stop();
            Console.WriteLine(sw.Elapsed);
        }

        private static IEnumerable<CSFile> YieldCSFiles(ProjectEx project) =>
            project.MSBuildProject.YieldCSFiles().Select(csFilePath => new CSFile(project, csFilePath));
    }
}
