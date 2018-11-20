﻿using CommandLine;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace aggregator.cli
{

    [Verb("list.mappings", HelpText = "Lists mappings from existing Azure DevOps Projects to Aggregator Rules.")]
    class ListMappingsCommand : CommandBase
    {
        [Option('i', "instance", Required = false, HelpText = "Aggregator instance name.")]
        public string Instance { get; set; }

        [Option('g', "resourceGroup", Required = false, Default = "", HelpText = "Azure Resource Group hosting the Aggregator instance.")]
        public string ResourceGroup { get; set; }

        [Option('p', "project", Required = false, Default = "", HelpText = "Azure DevOps project name.")]
        public string Project { get; set; }

        internal override async Task<int> RunAsync()
        {
            var context = await Context
                .WithDevOpsLogon()
                .Build();
            var instance = string.IsNullOrEmpty(Instance) ? null : new InstanceName(Instance, ResourceGroup);
            // HACK we pass null as the next calls do not use the Azure connection 
            var mappings = new AggregatorMappings(context.Devops, null, context.Logger);
            bool any = false;
            foreach (var item in await mappings.ListAsync(instance, Project))
            {
                context.Logger.WriteOutput(
                    item,
                    (data) => $"Project {item.project} invokes rule {item.rule} for {item.@event} (status {item.status})");
                any = true;
            }
            if (!any)
            {
                context.Logger.WriteInfo("No rule mappings found.");
            }
            return 0;
        }
    }
}
