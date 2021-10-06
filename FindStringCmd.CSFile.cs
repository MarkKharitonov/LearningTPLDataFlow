using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TPLDataFlow
{
    partial class FindStringCmd
    {
        private class CSFile : IComparable<CSFile>, IEquatable<CSFile>
        {
            public readonly ProjectEx Project;
            public readonly string FilePath;
            public byte[] Content { get; private set; }

            public CSFile(ProjectEx project, string filePath)
            {
                Project = project;
                FilePath = filePath;
            }

            public IEnumerable<MatchingLine> YieldMatchingLines(string[] literals, int index, int length, Progress progress)
            {
                using var sr = new StreamReader(new MemoryStream(Content));
                string line;
                int lineNo = 0;
                var totalCount = 0;
                while ((line = sr.ReadLine()) != null)
                {
                    ++lineNo;
                    for (int i = 0; i < length; ++i)
                    {
                        ++totalCount;
                        var literal = literals[index + i];
                        int pos = -1;
                        while ((pos = line.IndexOf(literal, pos + 1)) >= 0)
                        {
                            var endPos = pos + literal.Length;
                            if ((pos == 0 || !char.IsLetterOrDigit(line[pos - 1]) && line[pos - 1] != '_') &&
                                (endPos == line.Length || !char.IsLetterOrDigit(line[endPos]) && line[endPos] != '_'))
                            {
                                yield return new MatchingLine(this, line, lineNo, literal);
                                break;
                            }
                        }
                    }
                }
                Interlocked.Add(ref progress.Current, totalCount);
            }

            public static async Task<CSFile> PopulateContentAsync(CSFile csFile)
            {
                csFile.Content = await File.ReadAllBytesAsync(csFile.FilePath);
                return csFile;
            }

            public override string ToString() => FilePath + " / " + Project;

            public IEnumerable<(CSFile CSFile, int Pos, int Length)> YieldWorkItems(string[] literals, int workSize, Progress progress)
            {
                var count = Content.Count(b => b == '\n') + 1;
                Interlocked.Add(ref progress.Total, count * literals.Length);

                int countLiterals = (workSize + count - 1) / count;
                int i;
                for (i = 0; i < literals.Length / countLiterals; ++i)
                {
                    yield return (this, i * countLiterals, countLiterals);
                }
                i *= countLiterals;
                if (i < literals.Length)
                {
                    yield return (this, i, literals.Length - i);
                }
            }

            public int CompareTo(CSFile other)
            {
                var res = Project.AssemblyName.CompareTo(other.Project.AssemblyName);
                if (res == 0)
                {
                    res = FilePath.CompareTo(other.FilePath);
                }
                return res;
            }

            public override bool Equals(object obj) => Equals(obj as CSFile);

            public bool Equals(CSFile other) => other != null && Project.AssemblyName == other.Project.AssemblyName && FilePath == other.FilePath;

            public override int GetHashCode() => HashCode.Combine(Project.AssemblyName, FilePath);

            public static bool operator ==(CSFile left, CSFile right) => EqualityComparer<CSFile>.Default.Equals(left, right);

            public static bool operator !=(CSFile left, CSFile right) => !(left == right);

            public static bool operator <(CSFile left, CSFile right) => left.CompareTo(right) < 0;

            public static bool operator <=(CSFile left, CSFile right) => left.CompareTo(right) <= 0;

            public static bool operator >(CSFile left, CSFile right) => left.CompareTo(right) > 0;

            public static bool operator >=(CSFile left, CSFile right) => left.CompareTo(right) >= 0;
        }
    }
}
