using System.Collections.Generic;
using System.IO;

namespace TPLDataFlow
{
    partial class FindStringCmd
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
    }
}
