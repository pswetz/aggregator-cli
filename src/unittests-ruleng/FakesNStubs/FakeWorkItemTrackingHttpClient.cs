﻿using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace aggregator.unittests
{
    internal class FakeWorkItemTrackingHttpClient : WorkItemTrackingHttpClientBase
    {
        Dictionary<int, Func<WorkItem>> workItemFactory = new Dictionary<int, Func<WorkItem>>();

        public FakeWorkItemTrackingHttpClient(Uri baseUrl, VssCredentials credentials)
            : base(baseUrl, credentials)
        {
            string workItemsBaseUrl = $"{baseUrl.AbsoluteUri}/example-project/_apis/wit/workItems";
            workItemFactory.Add(1, () => new WorkItem()
            {
                Id = 1,
                Fields = new Dictionary<string, object>()
                {
                    { "System.WorkItemType", "User Story" },
                    { "System.State", "Open" },
                    { "System.TeamProject", "example-project" },
                    { "System.Title", "Hello" },
                },
                Rev = 12,
                Relations = new List<WorkItemRelation>()
                {
                    new WorkItemRelation
                    {
                        Rel = "System.LinkTypes.Hierarchy-Forward",
                        Url = $"{workItemsBaseUrl}/42"
                    },
                    new WorkItemRelation
                    {
                        Rel = "System.LinkTypes.Hierarchy-Forward",
                        Url = $"{workItemsBaseUrl}/99"
                    }
                },
                Url = $"{workItemsBaseUrl}/1"
            });

            workItemFactory.Add(42, () => new WorkItem()
            {
                Id = 42,
                Fields = new Dictionary<string, object>()
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.State", "Open" },
                    { "System.TeamProject", "example-project" },
                    { "System.Title", "Hello" },
                },
                Rev = 3,
                Relations = new List<WorkItemRelation>()
                {
                    new WorkItemRelation
                    {
                        Rel = "System.LinkTypes.Hierarchy-Reverse",
                        Url = $"{workItemsBaseUrl}/1"
                    }
                },
                Url = $"{workItemsBaseUrl}/42"
            });

            workItemFactory.Add(99, () => new WorkItem()
            {
                Id = 99,
                Fields = new Dictionary<string, object>()
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.State", "Open" },
                    { "System.TeamProject", "example-project" },
                    { "System.Title", "Hello" },
                },
                Rev = 3,
                Relations = new List<WorkItemRelation>()
                {
                    new WorkItemRelation
                    {
                        Rel = "System.LinkTypes.Hierarchy-Reverse",
                        Url = $"{workItemsBaseUrl}/1"
                    }
                },
                Url = $"{workItemsBaseUrl}/99"
            });
        }

        public override Task<WorkItem> GetWorkItemAsync(int id, IEnumerable<string> fields = null, DateTime? asOf = null, WorkItemExpand? expand = null, object userState = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            Debug.WriteLine($"FakeWorkItemTrackingHttpClient.GetWorkItemAsync({id})");
            if (expand == null)
            {
                throw new ArgumentNullException(nameof(expand));
            }
            if (expand != WorkItemExpand.All)
            {
                throw new ArgumentException("Must be WorkItemExpand.All", nameof(expand));
            }

            if (workItemFactory.ContainsKey(id))
            {
                var t = new Task<WorkItem>(workItemFactory[id]);
                t.RunSynchronously();
                return t;
            } else
            {
                return null;
            }
        }

        public override Task<List<WorkItem>> GetWorkItemsAsync(IEnumerable<int> ids, IEnumerable<string> fields = null, DateTime? asOf = null, WorkItemExpand? expand = null, WorkItemErrorPolicy? errorPolicy = null, object userState = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            string sid = ids.Aggregate(string.Empty, (s, i) => s + "," + i.ToString());
            Debug.WriteLine($"FakeWorkItemTrackingHttpClient.GetWorkItemsAsync({sid})");
            if (expand == null)
            {
                throw new ArgumentNullException(nameof(expand));
            }
            if (expand != WorkItemExpand.All)
            {
                throw new ArgumentException("Must be WorkItemExpand.All", nameof(expand));
            }

            var t = new Task<List<WorkItem>>(() => {
                var result = new List<WorkItem>();
                foreach (var id in ids)
                {
                    if (workItemFactory.ContainsKey(id))
                    {
                        var wi = workItemFactory[id]();
                        result.Add(wi);
                    }
                }
                return result;
            });
            t.RunSynchronously();
            return t;
        }

        public override Task<WorkItemType> GetWorkItemTypeAsync(Guid project, string type, object userState = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var t = new Task<WorkItemType>(() => new WorkItemType()
            {
                Name = "Bug",
                IsDisabled = false,
                FieldInstances = new List<WorkItemTypeFieldInstance>()
                {
                    new WorkItemTypeFieldInstance()
                    {
                        ReferenceName = "System.State",
                        IsIdentity = false,
                        AlwaysRequired = true
                        // MISSING: Data type!
                    }
                },
                Transitions = new Dictionary<string, WorkItemStateTransition[]>()
                {
                    {
                        "New", (new WorkItemStateTransition[] {
                            new WorkItemStateTransition()
                            {
                                To = "Active", Actions = new string[] { "" }
                            }
                        })
                    },
                    {
                        "Active", (new WorkItemStateTransition[] {
                            new WorkItemStateTransition()
                            {
                                To = "Closed", Actions = new string[] { "" }
                            }
                        })
                    }
                }
            });
            t.RunSynchronously();
            return t;
        }

        public override Task<WorkItemQueryResult> QueryByWiqlAsync(Wiql wiql, Guid project, bool? timePrecision = null, int? top = null, object userState = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var t = new Task<WorkItemQueryResult>(() => new WorkItemQueryResult() {
                QueryType = QueryType.Flat,
                WorkItems = new List<WorkItemReference>()
                {
                    new WorkItemReference()
                    {
                        Id = 33,
                        Url = $"{BaseAddress.AbsoluteUri}/{project}/_apis/wit/workItems/33"
                    }
                }
            });

            t.RunSynchronously();
            return t;
        }

        static int newId = 100;
        public override Task<WorkItem> CreateWorkItemAsync(JsonPatchDocument document, Guid project, string type, bool? validateOnly = null, bool? bypassRules = null, bool? suppressNotifications = null, object userState = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            string workItemsBaseUrl = $"{BaseAddress.AbsoluteUri}/example-project/_apis/wit/workItems";

            workItemFactory.Add(newId, () => new WorkItem()
            {
                Id = newId,
                Fields = new Dictionary<string, object>()
                {
                    { "System.WorkItemType", type },
                    { "System.State", "New" },
                    { "System.TeamProject", "example-project" },
                    { "System.Title", "Hello" },
                },
                Rev = 1,
                Relations = new List<WorkItemRelation>(),
                Url = $"{workItemsBaseUrl}/{newId}"
            });

            var t = new Task<WorkItem>(workItemFactory[newId]);
            t.RunSynchronously();
            return t;
        }

        public override Task<WorkItem> UpdateWorkItemAsync(JsonPatchDocument document, int id, bool? validateOnly = null, bool? bypassRules = null, bool? suppressNotifications = null, object userState = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var t = new Task<WorkItem>(() => new WorkItem());
            t.RunSynchronously();
            return t;
        }
    }
}
