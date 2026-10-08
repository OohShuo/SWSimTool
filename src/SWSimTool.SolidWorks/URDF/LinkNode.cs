using log4net;
using SWSimTool.Utilities;
using System.Windows.Forms;

namespace SWSimTool.URDF
{
    //A LinkNode is derived from a TreeView TreeNode. I've added many new fields to it so
    // that information can be passed around from the TreeView itself.
    public class LinkNode : TreeNode
    {
        private static readonly ILog logger = Logger.GetLogger();

        public Link Link
        { get; set; }

        public bool IsBaseNode
        { get=>Parent==null; set { } }

        public bool IsIncomplete
        { get; set; }

        public bool NeedsSaving
        { get; set; }

        public string WhyIncomplete
        { get; set; }

        public LinkNode()
        {
            Link = new Link();
        }

        public LinkNode(Link link)
        {
            logger.Info("Building node " + link.Name);

            IsBaseNode = link.Parent == null;
            IsIncomplete = true;
            Link = link;

            Name = Link.Name;
            Text = Link.Name;

            foreach (Link child in link.Children)
            {
                Nodes.Add(new LinkNode(child));
            }
        }

        public Link UpdateLinkTree(Link parent)
        {
            Link.Children.Clear();
            Link.Parent = parent;
            IsBaseNode=parent==null;
            foreach (LinkNode child in Nodes)
            {
                Link.Children.Add(child.UpdateLinkTree(Link));
            }
            return Link;
        }

        public override object Clone()
        {
            return new LinkNode(Snapshot());
        }

        // The UI topology is authoritative only inside this page. Reading it
        // creates a new business tree; it never mutates links held by the page.
        public Link Snapshot(Link parent=null)
        {
            var copy=Link.CopyProperties();copy.Name=Name;copy.Parent=parent;
            foreach(LinkNode child in Nodes)copy.Children.Add(child.Snapshot(copy));
            return copy;
        }

        public Link RebuildLink()
        {
            return Snapshot();
        }

        public static bool CanMove(LinkNode node,LinkNode target)
        {
            if(node==null||target==null||node.Parent==null||node.TreeView!=target.TreeView)return false;
            for(TreeNode p=target;p!=null;p=p.Parent)if(ReferenceEquals(p,node))return false;
            return true;
        }
        public static void Move(LinkNode node,LinkNode target)
        {
            if(!CanMove(node,target))return;
            var parent=node.Parent;int index=node.Index;
            try{node.Remove();target.Nodes.Add(node);}
            catch{node.Remove();parent.Nodes.Insert(index,node);throw;}
            ((LinkNode)node.TreeView.Nodes[0]).UpdateLinkTree(null);
        }
    }
}
