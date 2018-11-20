﻿using CommandLine;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace aggregator.cli
{
    [Verb("configure.rule", HelpText = "Change a rule configuration.")]
    class ConfigureRuleCommand : CommandBase
    {
        [Option('g', "resourceGroup", Required = false, Default = "", HelpText = "Azure Resource Group hosting the Aggregator instance.")]
        public string ResourceGroup { get; set; }

        [Option('i', "instance", Required = true, HelpText = "Aggregator instance name.")]
        public string Instance { get; set; }

        [Option('n', "name", Required = true, HelpText = "Aggregator rule name.")]
        public string Name { get; set; }

        [Option('d', "disable", SetName = "disable", HelpText = "Disable the rule.")]
        public bool Disable { get; set; }
        [Option('e', "enable", SetName = "enable", HelpText = "Enable the rule.")]
        public bool Enable { get; set; }

        internal override async Task<int> RunAsync()
        {
            var context = await Context
                .WithAzureLogon()
                .Build();
            var instance = new InstanceName(Instance, ResourceGroup);
            var rules = new AggregatorRules(context.Azure, context.Logger);
            bool ok = false;
            if (Disable || Enable)
            {
                ok = await rules.EnableAsync(instance, Name, Disable);
            }
            return ok ? 0 : 1;
        }
    }
}
