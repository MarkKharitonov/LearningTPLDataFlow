using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace TPLDataFlow
{
    partial class FindStringCmd
    {
        private struct MatchingLine : IComparable<MatchingLine>, IEquatable<MatchingLine>
        {
            [JsonIgnore]
            public readonly CSFile CSFile;
            public readonly string Line;
            public readonly int LineNo;
            public readonly string Literal;

            public string ProjectPath => CSFile.Project.ProjectPath;
            public string FilePath => CSFile.FilePath;

            public MatchingLine(CSFile csFile, string line, int lineNo, string literal)
            {
                CSFile = csFile;
                Line = line;
                LineNo = lineNo;
                Literal = literal;
            }

            public override string ToString() => LineNo + " / " + CSFile;

            public string ToString(string workspaceRoot) => $" *** {CSFile.Project} : {CSFile.FilePath[workspaceRoot.Length..]} : {LineNo}\n --> {Line}";

            public int CompareTo(MatchingLine other)
            {
                var res = CSFile.CompareTo(other.CSFile);
                if (res == 0)
                {
                    res = LineNo - other.LineNo;
                }
                return res;
            }

            public override bool Equals(object obj) => obj is MatchingLine line && Equals(line);

            public bool Equals(MatchingLine other) => EqualityComparer<CSFile>.Default.Equals(CSFile, other.CSFile) && LineNo == other.LineNo;

            public override int GetHashCode() => HashCode.Combine(CSFile, LineNo);

            public static bool operator ==(MatchingLine left, MatchingLine right) => left.Equals(right);

            public static bool operator !=(MatchingLine left, MatchingLine right) => !(left == right);

            public static bool operator <(MatchingLine left, MatchingLine right) => left.CompareTo(right) < 0;

            public static bool operator <=(MatchingLine left, MatchingLine right) => left.CompareTo(right) <= 0;

            public static bool operator >(MatchingLine left, MatchingLine right) => left.CompareTo(right) > 0;

            public static bool operator >=(MatchingLine left, MatchingLine right) => left.CompareTo(right) >= 0;
        }
    }
}
