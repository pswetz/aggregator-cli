﻿namespace aggregator.Engine
{
    internal class CoreRelationRefNames
    {
        public const string Parent = "System.LinkTypes.Hierarchy-Reverse";
        public const string Children = "System.LinkTypes.Hierarchy-Forward";
        public const string Related = "System.LinkTypes.Related";
        public const string Hyperlink = "System.LinkTypes.Hyperlink";
        // TODO this is not implemented but should be
        public const string AttachedFile = "AttachedFile";
    }
}
