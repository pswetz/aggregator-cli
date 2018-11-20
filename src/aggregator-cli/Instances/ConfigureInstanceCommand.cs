﻿using CommandLine;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace aggregator.cli
{
    [Verb("configure.instance", HelpText = "Configures an existing Aggregator instance.")]
    internal class ConfigureInstanceCommand : CommandBase
    {
        [Option('n', "name", Required = true, HelpText = "Aggregator instance name.")]
        public string Name { get; set; }

        [Option('l', "location", Required = true, HelpText = "Aggregator instance location (Azure region).")]
        public string Location { get; set; }

        [Option('g', "resourceGroup", Required = false, Default = "", HelpText = "Azure Resource Group hosting the Aggregator instance.")]
        public string ResourceGroup { get; set; }

        [Option('a', "authentication", SetName = "auth", Required = true, HelpText = "Refresh authentication data.")]
        public bool Authentication { get; set; }

        [Option('m', "saveMode", SetName = "save", Required = false, HelpText = "Save behaviour.")]
        public SaveMode SaveMode { get; set; }
        
        // TODO add --swap.slot to support App Service Deployment Slots


        internal override async Task<int> RunAsync()
        {
            var context = await Context
                .WithAzureLogon()
                .WithDevOpsLogon() // need the token, so we can save it in the app settings
                .Build();
            var instances = new AggregatorInstances(context.Azure, context.Logger);
            var instance = new InstanceName(Name, ResourceGroup);
            bool ok = false;
            if (Authentication)
            {
                ok = await instances.ChangeAppSettings(instance, Location, SaveMode);
            } else
            {
                context.Logger.WriteError($"Unsupported command option(s)");
            }
            return ok ? 0 : 1;
        }
    }
}