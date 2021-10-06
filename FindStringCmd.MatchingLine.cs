namespace TPLDataFlow
{
    partial class FindStringCmd
    {
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
    }
}
