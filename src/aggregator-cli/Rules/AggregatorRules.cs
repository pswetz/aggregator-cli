﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json;

namespace aggregator.cli
{
    class AggregatorRules
    {
        private readonly IAzure azure;
        private readonly ILogger logger;

        public AggregatorRules(IAzure azure, ILogger logger)
        {
            this.azure = azure;
            this.logger = logger;
        }


        internal async Task<IEnumerable<KuduFunction>> ListAsync(InstanceName instance)
        {
            var instances = new AggregatorInstances(azure, logger);
            var kudu = new KuduApi(instance, azure, logger);
            logger.WriteInfo($"Retrieving Functions in {instance.PlainName}...");
            using (var client = new HttpClient())
            using (var request = await kudu.GetRequestAsync(HttpMethod.Get, $"api/functions"))
            using (var response = await client.SendAsync(request))
            {
                var stream = await response.Content.ReadAsStreamAsync();

                if (response.IsSuccessStatusCode)
                {
                    using (var sr = new StreamReader(stream))
                    using (var jtr = new JsonTextReader(sr))
                    {
                        var js = new JsonSerializer();
                        var functionList = js.Deserialize<KuduFunction[]>(jtr);
                        return functionList;
                    }
                }
                else
                {
                    logger.WriteError($"{response.ReasonPhrase} {await response.Content.ReadAsStringAsync()}");
                    return new KuduFunction[0];
                }
            }
        }

        internal static string GetInvocationUrl(InstanceName instance, string rule)
        {
            return $"{instance.FunctionAppUrl}/api/{rule}";
        }

        internal async Task<(string url, string key)> GetInvocationUrlAndKey(InstanceName instance, string rule)
        {
            var instances = new AggregatorInstances(azure, logger);
            var kudu = new KuduApi(instance, azure, logger);

            // see https://github.com/projectkudu/kudu/wiki/Functions-API
            using (var client = new HttpClient())
            using (var request = await kudu.GetRequestAsync(HttpMethod.Post, $"api/functions/{rule}/listsecrets"))
            {
                using (var response = await client.SendAsync(request))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var sr = new StreamReader(stream))
                        using (var jtr = new JsonTextReader(sr))
                        {
                            var js = new JsonSerializer();
                            var secret = js.Deserialize<KuduSecret>(jtr);

                            (string url, string key) invocation = (GetInvocationUrl(instance, rule), secret.Key);
                            return invocation;
                        }
                    }
                    else
                    {
                        string error = await response.Content.ReadAsStringAsync();
                        logger.WriteError($"Failed to retrieve function key: {error}");
                        throw new ApplicationException("Failed to retrieve function key.");
                    }
                }
            }
        }

        internal async Task<bool> AddAsync(InstanceName instance, string name, string filePath)
        {
            var kudu = new KuduApi(instance, azure, logger);

            logger.WriteVerbose($"Layout rule files");
            string baseDirPath = LayoutRuleFiles(name, filePath);
            logger.WriteInfo($"Packaging {filePath} into rule {name} complete.");

            logger.WriteVerbose($"Uploading rule files to {instance.PlainName}");
            bool ok = await UploadRuleFiles(instance, name, baseDirPath);
            if (ok)
            {
                logger.WriteInfo($"All {name} files uploaded to {instance.PlainName}.");
            }
            CleanupRuleFiles(baseDirPath);
            logger.WriteInfo($"Cleaned local working directory.");
            return ok;
        }

        private static string LayoutRuleFiles(string name, string filePath)
        {
            // working directory
            var rand = new Random((int)DateTime.UtcNow.Ticks);
            string baseDirPath = Path.Combine(
                Path.GetTempPath(),
                $"aggregator-{rand.Next().ToString()}");
            string tempDirPath = Path.Combine(
                baseDirPath,
                name);
            Directory.CreateDirectory(tempDirPath);

            // copy rule content to fixed file name
            File.Copy(filePath, Path.Combine(tempDirPath, $"{name}.rule"));

            // copy templates
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream reader = assembly.GetManifestResourceStream("aggregator.cli.Rules.function.json"))
            // TODO we can deserialize a KuduFunctionConfig instead of using a fixed file...
            using (var writer = File.Create(Path.Combine(tempDirPath, "function.json")))
            {
                reader.CopyTo(writer);
            }
            using (Stream reader = assembly.GetManifestResourceStream("aggregator.cli.Rules.run.csx"))
            using (var writer = File.Create(Path.Combine(tempDirPath, "run.csx")))
            {
                reader.CopyTo(writer);
            }

            return baseDirPath;
        }

        void CleanupRuleFiles(string baseDirPath)
        {
            // clean-up: everything is in memory
            Directory.Delete(baseDirPath, true);
        }

        private async Task<bool> UploadRuleFiles(InstanceName instance, string name, string baseDirPath)
        {
            /*
            PUT /api/vfs/{path}
            Puts a file at path.

            PUT /api/vfs/{path}/
            Creates a directory at path. The path can be nested, e.g. `folder1/folder2`.

            Note: when updating or deleting a file, ETag behavior will apply. You can pass a If-Match: "*" header to disable the ETag check.
            */
            var kudu = new KuduApi(instance, azure, logger);
            string relativeUrl = $"api/vfs/site/wwwroot/{name}/";

            var instances = new AggregatorInstances(azure, logger);
            using (var client = new HttpClient())
            {
                bool exists = false;

                // check if function already exists
                using (var request = await kudu.GetRequestAsync(HttpMethod.Head, relativeUrl))
                {
                    logger.WriteVerbose($"Checking if function {name} already exists in {instance.PlainName}...");
                    using (var response = await client.SendAsync(request))
                    {
                        exists = response.IsSuccessStatusCode;
                    }
                }

                if (!exists)
                {
                    logger.WriteVerbose($"Creating function {name} in {instance.PlainName}...");
                    using (var request = await kudu.GetRequestAsync(HttpMethod.Put, relativeUrl))
                    {
                        using (var response = await client.SendAsync(request))
                        {
                            bool ok = response.IsSuccessStatusCode;
                            if (!ok)
                            {
                                logger.WriteError($"Upload failed with {response.ReasonPhrase}");
                                return ok;
                            }
                        }
                    }
                    logger.WriteInfo($"Function {name} created.");
                }

                var files = Directory.EnumerateFiles(baseDirPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    logger.WriteVerbose($"Uploading {Path.GetFileName(file)} to {instance.PlainName}...");
                    string fileUrl = $"{relativeUrl}{Path.GetFileName(file)}";
                    using (var request = await kudu.GetRequestAsync(HttpMethod.Put, fileUrl))
                    {
                        //HACK -> request.Headers.IfMatch.Add(new EntityTagHeaderValue("*", false)); <- won't work
                        request.Headers.Add("If-Match", "*");
                        request.Content = new StringContent(File.ReadAllText(file));
                        using (var response = await client.SendAsync(request))
                        {
                            bool ok = response.IsSuccessStatusCode;
                            if (!ok)
                            {
                                logger.WriteError($"Failed uploading {file} with {response.ReasonPhrase}");
                                return ok;
                            }
                        }
                    }
                    logger.WriteInfo($"{Path.GetFileName(file)} uploaded to {instance.PlainName}.");
                }//for
            }
            return true;
        }

        internal async Task<bool> RemoveAsync(InstanceName instance, string name)
        {
            var kudu = new KuduApi(instance, azure, logger);
            var instances = new AggregatorInstances(azure, logger);
            // undocumented but works, see https://github.com/projectkudu/kudu/wiki/Functions-API
            logger.WriteInfo($"Removing Function {name} in {instance.PlainName}...");
            using (var client = new HttpClient())
            using (var request = await kudu.GetRequestAsync(HttpMethod.Delete, $"api/functions/{name}"))
            using (var response = await client.SendAsync(request))
            {
                bool ok = response.IsSuccessStatusCode;
                if (!ok)
                {
                    logger.WriteError($"Failed removing Function {name} from {instance.PlainName} with {response.ReasonPhrase}");
                }
                return ok;
            }
        }

        internal async Task<bool> EnableAsync(InstanceName instance, string name, bool disable)
        {
            var webFunctionApp = await azure
                .AppServices
                .WebApps
                .GetByResourceGroupAsync(
                    instance.ResourceGroupName,
                    instance.FunctionAppName);
            webFunctionApp
                .Update()
                .WithAppSetting($"AzureWebJobs.{name}.Disabled", disable.ToString().ToLower())
                .Apply();

            return true;
        }

        internal async Task<bool> UpdateAsync(InstanceName instance, string name, string filePath, string requiredVersion)
        {
            // check runtime package
            var package = new FunctionRuntimePackage(logger);
            bool ok = await package.UpdateVersion(requiredVersion, instance, azure);
            if (ok)
            {
                ok = await AddAsync(instance, name, filePath);
            }
            return ok;
        }

        internal async Task<bool> InvokeLocalAsync(string projectName, string @event, int workItemId, string ruleFilePath, bool dryRun, SaveMode saveMode)
        {
            if (!File.Exists(ruleFilePath))
            {
                logger.WriteError($"Rule code not found at {ruleFilePath}");
                return false;
            }

            var devopsLogonData = DevOpsLogon.Load().connection;

            logger.WriteVerbose($"Connecting to Azure DevOps using {devopsLogonData.Mode}...");
            var clientCredentials = default(VssCredentials);
            if (devopsLogonData.Mode == DevOpsTokenType.PAT)
            {
                clientCredentials = new VssBasicCredential(devopsLogonData.Mode.ToString(), devopsLogonData.Token);
            }
            else
            {
                logger.WriteError($"Azure DevOps Token type {devopsLogonData.Mode} not supported!");
                throw new ArgumentOutOfRangeException(nameof(devopsLogonData.Mode));
            }

            string collectionUrl = devopsLogonData.Url;
            using (var devops = new VssConnection(new Uri(collectionUrl), clientCredentials))
            {
                await devops.ConnectAsync();
                logger.WriteInfo($"Connected to Azure DevOps");

                Guid teamProjectId;
                string teamProjectName;
                using (var projectClient = devops.GetClient<ProjectHttpClient>())
                {
                    logger.WriteVerbose($"Reading Azure DevOps project data...");
                    var project = await projectClient.GetProject(projectName);
                    logger.WriteInfo($"Project {projectName} data read.");
                    teamProjectId = project.Id;
                    teamProjectName = project.Name;
                }

                using (var witClient = devops.GetClient<WorkItemTrackingHttpClient>())
                {
                    logger.WriteVerbose($"Rule code found at {ruleFilePath}");
                    string[] ruleCode = File.ReadAllLines(ruleFilePath);

                    var engineLogger = new EngineWrapperLogger(logger);
                    var engine = new Engine.RuleEngine(engineLogger, ruleCode, saveMode);
                    engine.DryRun = dryRun;

                    string result = await engine.ExecuteAsync(collectionUrl, teamProjectId, teamProjectName, devopsLogonData.Token, workItemId, witClient);
                    logger.WriteInfo($"Rule returned '{result}'");

                    return true;
                }
            }
        }
    }
}
