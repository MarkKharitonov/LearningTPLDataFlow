using Microsoft.Build.Evaluation;
using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.XPath;

namespace CSTool.ObjectModel
{
    public class ProjectEx
    {
        private readonly Lazy<Project> m_project;
        public string AssemblyName { get; }
        public string Name { get; }

        public static ProjectEx Create(string projectFilePath, bool includeAll = false)
        {
            var (nav, nsmgr) = GetProjectXPathNavigator(projectFilePath);
            var buildTargetNode = nav.SelectSingleNode("/p:Project/p:Target[@Name='Build']", nsmgr);
            if (buildTargetNode != null)
            {
                if (!includeAll)
                {
                    return null;
                }
            }

            var bridgeImportNode = nav.SelectSingleNode(@"/p:Project/p:Import[substring(@Project,string-length(@Project)-string-length('\Bridge.targets')+1)='\Bridge.targets']", nsmgr);
            if (bridgeImportNode != null)
            {
                if (!includeAll)
                {
                    return null;
                }
            }

            return new ProjectEx(projectFilePath, nav, nsmgr);
        }

        private ProjectEx(string projectFilePath, XPathNavigator nav, XmlNamespaceManager nsmgr)
        {
            ProjectPath = projectFilePath;
            Name = Path.GetFileNameWithoutExtension(projectFilePath);
            AssemblyName = nav.SelectSingleNode("/p:Project/p:PropertyGroup/p:AssemblyName/text()", nsmgr)?.Value ?? Name;
            m_project = new Lazy<Project>(() =>
            {
                var found = ProjectCollection.GlobalProjectCollection.LoadedProjects.FirstOrDefault(p => p.FullPath.Equals(ProjectPath, StringComparison.OrdinalIgnoreCase));
                return found ?? new Project(ProjectPath);
            });
        }

        public Project MSBuildProject => m_project.Value;

        public string ProjectPath { get; }

        public override string ToString() => AssemblyName;

        private static (XPathNavigator, XmlNamespaceManager) GetProjectXPathNavigator(string projectFile)
        {
            var doc = new XPathDocument(projectFile);
            var nav = doc.CreateNavigator();
            var nsmgr = new XmlNamespaceManager(nav.NameTable);
            nav.MoveToFollowing(XPathNodeType.Element);
            var ns = nav.GetNamespacesInScope(XmlNamespaceScope.Local).FirstOrDefault();
            nsmgr.AddNamespace("p", ns.Value ?? "");
            return (nav, nsmgr);
        }
    }
}
