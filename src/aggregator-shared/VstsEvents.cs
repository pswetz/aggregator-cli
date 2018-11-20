﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace aggregator
{
    /// <summary>
    /// This class tracks the VSTS/AzureDevOps Events exposed both in CLI and Rules
    /// </summary>
    public class DevOpsEvents
    {
        // TODO this table should be visible in the help
        static string[] validValues = new string[] {
            "workitem.created",
            "workitem.deleted",
            "workitem.restored",
            "workitem.updated",
            "workitem.commented"
        };

        public static bool IsValidEvent(string @event)
        {
            return validValues.Contains(@event);
        }

        public static string PublisherId => "tfs";
    }
}
