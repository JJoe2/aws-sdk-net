
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Amazon.SimpleWorkflow;
using Amazon.SimpleWorkflow.Model;

using System.Threading;
using Xunit;
using Amazon.DNXCore.IntegrationTests;

namespace Amazon.DNXCore.IntegrationTests
{
    //
    public class SimpleWorkflow : TestBase<AmazonSimpleWorkflowClient>
    {
        private static readonly TimeSpan SleepDuration = TimeSpan.FromSeconds(5);
        private const int MaxRetries = 3;

        public static readonly string ONE_HOUR_DURATION = (60 * 60).ToString();
        public static readonly string TWO_HOUR_DURATION = (60 * 60 * 2).ToString();
        public static readonly string THREE_HOUR_DURATION = (60 * 60 * 3).ToString();
        public static readonly string FOUR_HOUR_DURATION = (60 * 60 * 4).ToString();
        public static readonly string FIVE_HOUR_DURATION = (60 * 60 * 5).ToString();

        string DOMAIN = UtilityMethods.SDK_TEST_PREFIX + DateTime.Now.Ticks;
        static WorkflowType WORKFLOW_TYPE = new WorkflowType()
        {
            Name = UtilityMethods.SDK_TEST_PREFIX + DateTime.Now.Ticks,
            Version = "1.0"
        };

        static TaskList TASKLIST = new TaskList() { Name = "TestTaskList" };
        static ActivityType ACTIVITY_TYPE = new ActivityType()
        {
            Name = UtilityMethods.SDK_TEST_PREFIX + DateTime.Now.Ticks,
            Version = "1.0"
        };

        public SimpleWorkflow()
        {
            var registerDomainRequest = new RegisterDomainRequest()
            {
                Name = DOMAIN,
                Description = "A Description",
                WorkflowExecutionRetentionPeriodInDays = "5"
            };
            Client.RegisterDomainAsync(registerDomainRequest).Wait();

            var registerWorkflowRequest = new RegisterWorkflowTypeRequest()
            {
                Name = WORKFLOW_TYPE.Name,
                Domain = DOMAIN,
                Description = "Another Description",
                Version = WORKFLOW_TYPE.Version,
                DefaultTaskList = TASKLIST,
                DefaultExecutionStartToCloseTimeout = FIVE_HOUR_DURATION,
                DefaultTaskStartToCloseTimeout = FOUR_HOUR_DURATION
            };

            Client.RegisterWorkflowTypeAsync(registerWorkflowRequest).Wait();

            var registerActivityRequest = new RegisterActivityTypeRequest()
            {
                Name = ACTIVITY_TYPE.Name,
                Domain = DOMAIN,
                Description = "My Description Activity",
                Version = ACTIVITY_TYPE.Version,
                DefaultTaskList = TASKLIST,
                DefaultTaskHeartbeatTimeout = ONE_HOUR_DURATION,
                DefaultTaskScheduleToCloseTimeout = TWO_HOUR_DURATION,
                DefaultTaskScheduleToStartTimeout = THREE_HOUR_DURATION,
                DefaultTaskStartToCloseTimeout = FOUR_HOUR_DURATION
            };

            var registerActivityResponse =
                Client.RegisterActivityTypeAsync(registerActivityRequest).Result;
        }
        
        protected override void Dispose(bool disposing)
        {
            var deprecateWorkflowRequest = new DeprecateWorkflowTypeRequest()
            {
                Domain = DOMAIN,
                WorkflowType = WORKFLOW_TYPE
            };
            var deprecateWorkflowTypeResponse = Client.DeprecateWorkflowTypeAsync(deprecateWorkflowRequest).Result;
            Assert.NotNull(deprecateWorkflowTypeResponse.ResponseMetadata.RequestId);

            var deprecateDomainRequest = new DeprecateDomainRequest() { Name = DOMAIN };
            var deprecateDomainResponse = Client.DeprecateDomainAsync(deprecateDomainRequest).Result;
            Assert.NotNull(deprecateDomainResponse.ResponseMetadata.RequestId);
            base.Dispose(disposing);
        }
        
        [Fact]
        [Trait(CategoryAttribute,"SimpleWorkflow")]
        public void TestPollDecider()
        {
            string workflowId = DateTime.Now.Ticks.ToString();
            var startRequest = new StartWorkflowExecutionRequest()
            {
                Domain = DOMAIN,
                WorkflowId = workflowId,
                ChildPolicy = "TERMINATE",
                TaskList = TASKLIST,
                Input = "ImportantKeyId",
                WorkflowType = WORKFLOW_TYPE
            };

            var startResponse = Client.StartWorkflowExecutionAsync(startRequest).Result;


            var pollDeciderRequest = new PollForDecisionTaskRequest()
            {
                Domain = DOMAIN,
                TaskList = TASKLIST,
                Identity = "testdecider"
            };

            var pollDeciderResponse = Client.PollForDecisionTaskAsync(pollDeciderRequest).Result;

            Assert.NotNull(pollDeciderResponse.DecisionTask);
            Assert.True(pollDeciderResponse.DecisionTask.Events.Count > 0);
            Assert.NotNull(pollDeciderResponse.DecisionTask.Events[0].EventId);
            Assert.NotNull(pollDeciderResponse.DecisionTask.Events[0].EventType);
            Assert.Equal(DateTime.Today, pollDeciderResponse.DecisionTask.Events[0].EventTimestamp.ToLocalTime().Date);

            var task = pollDeciderResponse.DecisionTask;
            var respondRequest = new RespondDecisionTaskCompletedRequest()
            {
                TaskToken = task.TaskToken,
                Decisions = new List<Decision>()
                {
                    new Decision()
                    {
                        DecisionType = "ScheduleActivityTask",
                        ScheduleActivityTaskDecisionAttributes = new ScheduleActivityTaskDecisionAttributes()
                        {
                            ActivityType = ACTIVITY_TYPE,
                            ActivityId = DateTime.Now.Ticks.ToString(),
                            TaskList = TASKLIST
                        }
                    }
                }
            };

            Client.RespondDecisionTaskCompletedAsync(respondRequest).Wait();

            var pollActivityRequest = new PollForActivityTaskRequest()
            {
                Domain = DOMAIN,
                TaskList = TASKLIST,
                Identity = "testactivity"
            };

            var pollActivityResponse = Client.PollForActivityTaskAsync(pollActivityRequest).Result;
            Assert.NotNull(pollActivityResponse.ResponseMetadata.RequestId);
            Assert.Equal(ACTIVITY_TYPE.Name, pollActivityResponse.ActivityTask.ActivityType.Name);
            Assert.Equal(ACTIVITY_TYPE.Version, pollActivityResponse.ActivityTask.ActivityType.Version);


            var signalRequest = new SignalWorkflowExecutionRequest()
            {
                Domain = DOMAIN,
                RunId = startResponse.Run.RunId,
                WorkflowId = startRequest.WorkflowId,
                SignalName = "TestSignal"
            };

            var signalResponse = Client.SignalWorkflowExecutionAsync(signalRequest).Result;
            Assert.NotNull(signalResponse.ResponseMetadata.RequestId);
        }
                
        [Fact]
        [Trait(CategoryAttribute,"SimpleWorkflow")]
        public void CompleteActivity()
        {
            var workflowId = DateTime.Now.Ticks.ToString();
            var startRequest = new StartWorkflowExecutionRequest()
            {
                Domain = DOMAIN,
                WorkflowId = workflowId,
                ChildPolicy = "TERMINATE",
                TaskList = TASKLIST,
                Input = "ImportantKeyId",
                WorkflowType = WORKFLOW_TYPE
            };

            var runId = Client.StartWorkflowExecutionAsync(startRequest).Result.Run.RunId;


            var task = startActivity(runId);

            var respondCompleteRequest = new RespondActivityTaskCompletedRequest()
            {
                TaskToken = task.TaskToken,
                Result = "completed"
            };

            var respondCompleteRespond = Client.RespondActivityTaskCompletedAsync(respondCompleteRequest).Result;
            Assert.NotNull(respondCompleteRespond.ResponseMetadata.RequestId);
        }


        [Fact]
        [Trait(CategoryAttribute,"SimpleWorkflow")]
        public void CancelActivity()
        {
            var workflowId = DateTime.Now.Ticks.ToString();
            var startRequest = new StartWorkflowExecutionRequest()
            {
                Domain = DOMAIN,
                WorkflowId = workflowId,
                ChildPolicy = "TERMINATE",
                TaskList = TASKLIST,
                Input = "ImportantKeyId",
                WorkflowType = WORKFLOW_TYPE
            };

            var runId = Client.StartWorkflowExecutionAsync(startRequest).Result.Run.RunId;


            var task = startActivity(runId);

            var respondCanceledRequest = new RespondActivityTaskCanceledRequest()
            {
                TaskToken = task.TaskToken,
                Details = "cancel task"
            };

            var respondCanceledRespond = Client.RespondActivityTaskCanceledAsync(respondCanceledRequest).Result;
            Assert.NotNull(respondCanceledRespond.ResponseMetadata.RequestId);
        }

        [Fact]
        [Trait(CategoryAttribute,"SimpleWorkflow")]
        public void FailedActivity()
        {
            var workflowId = DateTime.Now.Ticks.ToString();
            var startRequest = new StartWorkflowExecutionRequest()
            {
                Domain = DOMAIN,
                WorkflowId = workflowId,
                ChildPolicy = "TERMINATE",
                TaskList = TASKLIST,
                Input = "ImportantKeyId",
                WorkflowType = WORKFLOW_TYPE
            };

            var runId = Client.StartWorkflowExecutionAsync(startRequest).Result.Run.RunId;


            var task = startActivity(runId);

            var respondFailedRequest = new RespondActivityTaskFailedRequest()
            {
                TaskToken = task.TaskToken,
                Details = "fail task",
                Reason = "result not required"
            };

            var respondFailedRespond = Client.RespondActivityTaskFailedAsync(respondFailedRequest).Result;
            Assert.NotNull(respondFailedRespond.ResponseMetadata.RequestId);
        }

        [Fact]
        [Trait(CategoryAttribute,"SimpleWorkflow")]
        public void CancelWorkflow()
        {
            var workflowId = DateTime.Now.Ticks.ToString();
            var startRequest = new StartWorkflowExecutionRequest()
            {
                Domain = DOMAIN,
                WorkflowId = workflowId,
                ChildPolicy = "TERMINATE",
                TaskList = TASKLIST,
                Input = "ImportantKeyId",
                WorkflowType = WORKFLOW_TYPE
            };

            var runId = Client.StartWorkflowExecutionAsync(startRequest).Result.Run.RunId;

            var cancelRequest = new RequestCancelWorkflowExecutionRequest()
            {
                Domain = DOMAIN,
                RunId = runId,
                WorkflowId = workflowId
            };

            var cancelResponse = Client.RequestCancelWorkflowExecutionAsync(cancelRequest).Result;
            Assert.NotNull(cancelResponse.ResponseMetadata.RequestId);
        }

        [Fact]
        [Trait(CategoryAttribute,"SimpleWorkflow")]
        public void CRUDTest()
        {
            var domainName = "sdk-dotnet-crud-" + DateTime.Now.Ticks;
            var regRequest = new RegisterDomainRequest()
            {
                Name = domainName,
                Description = "A Description",
                WorkflowExecutionRetentionPeriodInDays = "3"
            };

            var regResponse = Client.RegisterDomainAsync(regRequest).Result;
            try
            {
                Sleep(); // Sleep for the eventual consistency
                Assert.NotNull(regResponse.ResponseMetadata.RequestId);
                var descRequest = new DescribeDomainRequest() { Name = domainName };
                var descResponse = Client.DescribeDomainAsync(descRequest).Result;

                Assert.Equal(domainName, descResponse.DomainDetail.DomainInfo.Name);
                Assert.Equal("A Description", descResponse.DomainDetail.DomainInfo.Description);
                Assert.Equal("3", descResponse.DomainDetail.Configuration.WorkflowExecutionRetentionPeriodInDays);
                Assert.NotNull(descResponse.DomainDetail.DomainInfo.Status);


                DomainInfo info = null;
                for (int i = 0; i < MaxRetries; i++)
                {
                    Sleep(); // Sleep for the eventual consistency
                    var listDomainResponse = Client.ListDomainsAsync(
                        new ListDomainsRequest()
                        {
                            RegistrationStatus = descResponse.DomainDetail.DomainInfo.Status
                        }).Result;

                    Assert.True(listDomainResponse.DomainInfos.Infos.Count > 0);
                    info = listDomainResponse.DomainInfos.Infos.FirstOrDefault(x => string.Equals(x.Name, domainName));
                    if (info != null)
                        break;
                }
                Assert.NotNull(info);
                Assert.NotNull(info.Status);


                var activityDescription = "My Description Activity" + DateTime.Now.Ticks;
                var regActivityRequest = new RegisterActivityTypeRequest()
                {
                    Name = "My Activity",
                    Domain = domainName,
                    Description = activityDescription,
                    Version = "1.0",
                    DefaultTaskList = new TaskList() { Name = "ImportantTasks" },
                    DefaultTaskHeartbeatTimeout = ONE_HOUR_DURATION,
                    DefaultTaskScheduleToCloseTimeout = TWO_HOUR_DURATION,
                    DefaultTaskScheduleToStartTimeout = THREE_HOUR_DURATION,
                    DefaultTaskStartToCloseTimeout = FOUR_HOUR_DURATION
                };

                var regActivityResponse = Client.RegisterActivityTypeAsync(regActivityRequest).Result;
                Assert.NotNull(regActivityResponse.ResponseMetadata.RequestId);
                try
                {
                    Sleep(); // Sleep for the eventual consistency
                    var descActivityTypeRequest = new DescribeActivityTypeRequest()
                    {
                        Domain = domainName,
                        ActivityType = new ActivityType()
                        {
                            Name = "My Activity",
                            Version = "1.0"
                        }
                    };

                    var descActivityTypeResponse = Client.DescribeActivityTypeAsync(descActivityTypeRequest).Result;

                    Assert.Equal(ONE_HOUR_DURATION, descActivityTypeResponse.ActivityTypeDetail.Configuration.DefaultTaskHeartbeatTimeout);
                    Assert.Equal(TWO_HOUR_DURATION, descActivityTypeResponse.ActivityTypeDetail.Configuration.DefaultTaskScheduleToCloseTimeout);
                    Assert.Equal(THREE_HOUR_DURATION, descActivityTypeResponse.ActivityTypeDetail.Configuration.DefaultTaskScheduleToStartTimeout);
                    Assert.Equal(FOUR_HOUR_DURATION, descActivityTypeResponse.ActivityTypeDetail.Configuration.DefaultTaskStartToCloseTimeout);

                    ListActivityTypesResponse listActivityResponse = null;
                    for (int i = 0; i < MaxRetries; i++)
                    {
                        Sleep(); // Sleep for the eventual consistency
                        listActivityResponse = Client.ListActivityTypesAsync(
                            new ListActivityTypesRequest()
                            {
                                Domain = domainName,
                                RegistrationStatus = descActivityTypeResponse.ActivityTypeDetail.TypeInfo.Status
                            }).Result;
                        if (listActivityResponse.ActivityTypeInfos.TypeInfos.Count > 0)
                            break;
                    }
                    Assert.NotNull(listActivityResponse);
                    Assert.True(listActivityResponse.ActivityTypeInfos.TypeInfos.Count > 0);
                    var acInfo = listActivityResponse.ActivityTypeInfos.TypeInfos.FirstOrDefault(x => x.Description == activityDescription);
                    Assert.NotNull(acInfo);
                }
                finally
                {
                    var depActivityRequest = new DeprecateActivityTypeRequest()
                    {
                        Domain = domainName,
                        ActivityType = new ActivityType()
                        {
                            Name = "My Activity",
                            Version = "1.0"
                        }
                    };

                    var depActivityTypeResponse = Client.DeprecateActivityTypeAsync(depActivityRequest).Result;
                }

                var workflowDescription = "My Workflow Description" + DateTime.Now.Ticks;
                var regWorkflowRequest = new RegisterWorkflowTypeRequest()
                {
                    Name = "My Workflow",
                    Domain = domainName,
                    Description = workflowDescription,
                    Version = "1.0",
                    DefaultTaskList = new TaskList() { Name = "ImportantTasks" },
                    DefaultExecutionStartToCloseTimeout = THREE_HOUR_DURATION,
                    DefaultTaskStartToCloseTimeout = FOUR_HOUR_DURATION
                };

                var regWorkflowResponse = Client.RegisterWorkflowTypeAsync(regWorkflowRequest).Result;
                try
                {
                    Sleep(); // Sleep for the eventual consistency
                    var descWorkFlowRequest = new DescribeWorkflowTypeRequest()
                    {
                        Domain = domainName,
                        WorkflowType = new WorkflowType()
                        {
                            Name = "My Workflow",
                            Version = "1.0"
                        }
                    };
                    var descWorkflowResponse = Client.DescribeWorkflowTypeAsync(descWorkFlowRequest).Result;

                    Assert.Equal("My Workflow", descWorkflowResponse.WorkflowTypeDetail.TypeInfo.WorkflowType.Name);
                    Assert.Equal("1.0", descWorkflowResponse.WorkflowTypeDetail.TypeInfo.WorkflowType.Version);
                    Assert.Equal(THREE_HOUR_DURATION, descWorkflowResponse.WorkflowTypeDetail.Configuration.DefaultExecutionStartToCloseTimeout);
                    Assert.Equal(FOUR_HOUR_DURATION, descWorkflowResponse.WorkflowTypeDetail.Configuration.DefaultTaskStartToCloseTimeout);
                    Assert.Equal("ImportantTasks", descWorkflowResponse.WorkflowTypeDetail.Configuration.DefaultTaskList.Name);

                    ListWorkflowTypesResponse listWorkflowResponse = null;
                    for (int retries = 0; retries < 5; retries++)
                    {
                        UtilityMethods.Sleep(TimeSpan.FromSeconds(retries));
                        listWorkflowResponse = Client.ListWorkflowTypesAsync(
                            new ListWorkflowTypesRequest()
                            {
                                Domain = domainName,
                                RegistrationStatus = descWorkflowResponse.WorkflowTypeDetail.TypeInfo.Status
                            }).Result;

                        if (listWorkflowResponse.WorkflowTypeInfos.TypeInfos.Count > 0)
                            break;
                    }

                    Assert.True(listWorkflowResponse.WorkflowTypeInfos.TypeInfos.Count > 0);
                    var wfInfo = listWorkflowResponse.WorkflowTypeInfos.TypeInfos.FirstOrDefault(x => x.Description == workflowDescription);
                    Assert.NotNull(wfInfo);

                }
                finally
                {
                    var depWorkflowRequest = new DeprecateWorkflowTypeRequest()
                    {
                        Domain = domainName,
                        WorkflowType = new WorkflowType()
                        {
                            Name = "My Workflow",
                            Version = "1.0"
                        }
                    };
                    var depWorkflowTypeResponse = Client.DeprecateWorkflowTypeAsync(depWorkflowRequest).Result;
                    Assert.NotNull(depWorkflowTypeResponse.ResponseMetadata.RequestId);
                }

            }
            finally
            {
                var depRequest = new DeprecateDomainRequest()
                {
                    Name = domainName
                };
                var depResponse = Client.DeprecateDomainAsync(depRequest).Result;
                Assert.NotNull(depResponse.ResponseMetadata.RequestId);
            }
        }

        [Fact]
        [Trait(CategoryAttribute,"SimpleWorkflow")]
        public void StartAndTerminateWorkflowExecution()
        {
            string domainName = "sdk-dotnet-start-" + DateTime.Now.Ticks;
            var regRequest = new RegisterDomainRequest()
            {
                Name = domainName,
                Description = "A Description",
                WorkflowExecutionRetentionPeriodInDays = "4"
            };

            Client.RegisterDomainAsync(regRequest).Wait();
            try
            {
                var regWorkflowRequest = new RegisterWorkflowTypeRequest()
                {
                    Name = "Start and Terminate Workflow",
                    Domain = domainName,
                    Description = "Another Description",
                    Version = "1.0",
                    DefaultExecutionStartToCloseTimeout = FIVE_HOUR_DURATION,
                    DefaultTaskStartToCloseTimeout = FOUR_HOUR_DURATION
                };

                var regWorkflowResponse = Client.RegisterWorkflowTypeAsync(regWorkflowRequest).Result;
                try
                {
                    Sleep(); // Sleep for the eventual consistency
                    var workflowId = DateTime.Now.Ticks.ToString();
                    var startRequest = new StartWorkflowExecutionRequest()
                    {
                        Domain = domainName,
                        WorkflowId = workflowId,
                        ChildPolicy = "TERMINATE",
                        TaskList = new TaskList() { Name = "ImportantTasks" },
                        WorkflowType = new WorkflowType() { Name = regWorkflowRequest.Name, Version = regWorkflowRequest.Version }
                    };

                    var startResponse = Client.StartWorkflowExecutionAsync(startRequest).Result;
                    Assert.NotNull(startResponse.Run.RunId);

                    UtilityMethods.Sleep(TimeSpan.FromSeconds(10));
                    var countWorkRequest = new CountOpenWorkflowExecutionsRequest()
                    {
                        Domain = domainName,
                        StartTimeFilter = new ExecutionTimeFilter()
                        {
                            OldestDate = DateTime.Now.AddDays(-4),
                            LatestDate = DateTime.Now.AddDays(1)
                        },
                        ExecutionFilter = new WorkflowExecutionFilter()
                        {
                            WorkflowId = workflowId
                        }
                    };

                    var countWorkResponse = Client.CountOpenWorkflowExecutionsAsync(countWorkRequest).Result;

                    Assert.Equal(1, countWorkResponse.WorkflowExecutionCount.Count);

                    var listWorkRequest = new ListOpenWorkflowExecutionsRequest()
                    {
                        Domain = domainName,
                        StartTimeFilter = new ExecutionTimeFilter()
                        {
                            OldestDate = DateTime.Now.AddDays(-4),
                            LatestDate = DateTime.Now.AddDays(1)
                        },
                        ExecutionFilter = new WorkflowExecutionFilter()
                        {
                            WorkflowId = workflowId
                        }
                    };

                    var listWorkResponse = Client.ListOpenWorkflowExecutionsAsync(listWorkRequest).Result;
                    Assert.Equal(1, listWorkResponse.WorkflowExecutionInfos.ExecutionInfos.Count);

                    var info = listWorkResponse.WorkflowExecutionInfos.ExecutionInfos[0];
                    Assert.Equal(regWorkflowRequest.Name, info.WorkflowType.Name);
                    Assert.Equal(regWorkflowRequest.Version, info.WorkflowType.Version);
                    Assert.Equal(startResponse.Run.RunId, info.Execution.RunId);
                    Assert.Equal(startRequest.WorkflowId, info.Execution.WorkflowId);

                    var descRequest = new DescribeWorkflowExecutionRequest()
                    {
                        Domain = domainName,
                        Execution = new WorkflowExecution()
                        {
                            RunId = startResponse.Run.RunId,
                            WorkflowId = startRequest.WorkflowId
                        }
                    };

                    var descResponse = Client.DescribeWorkflowExecutionAsync(descRequest).Result;

                    Assert.NotNull(descResponse.WorkflowExecutionDetail);
                    Assert.Equal(startRequest.TaskList.Name, descResponse.WorkflowExecutionDetail.ExecutionConfiguration.TaskList.Name);

                    var termRequest = new TerminateWorkflowExecutionRequest()
                    {
                        Domain = domainName,
                        ChildPolicy = "TERMINATE",
                        WorkflowId = workflowId
                    };

                    var termResponse = Client.TerminateWorkflowExecutionAsync(termRequest).Result;
                    Assert.NotNull(termResponse.ResponseMetadata.RequestId);
                }
                finally
                {
                    Client.DeprecateWorkflowTypeAsync(new DeprecateWorkflowTypeRequest()
                    {
                        Domain = domainName,
                        WorkflowType = new WorkflowType()
                        {
                            Name = "Start and Terminate Workflow",
                            Version = "1.0"
                        }
                    }).Wait();
                }
            }
            finally
            {
                var depRequest = new DeprecateDomainRequest() { Name = domainName };
                Client.DeprecateDomainAsync(depRequest).Wait();
            }
        }


        ActivityTask pollActivity()
        {
            var pollActivityRequest = new PollForActivityTaskRequest()
            {
                Domain = DOMAIN,
                TaskList = TASKLIST,
                Identity = "testactivity"
            };

            var pollActivityResponse = Client.PollForActivityTaskAsync(pollActivityRequest).Result;
            return pollActivityResponse.ActivityTask;
        }

        ActivityTask startActivity(string runId)
        {
            var pollDeciderRequest = new PollForDecisionTaskRequest()
            {
                Domain = DOMAIN,
                TaskList = TASKLIST,
                Identity = "testdecider"
            };

            var pollDeciderResponse = Client.PollForDecisionTaskAsync(pollDeciderRequest).Result;

            var task = pollDeciderResponse.DecisionTask;
            var respondRequest = new RespondDecisionTaskCompletedRequest()
            {
                TaskToken = task.TaskToken,
                Decisions = new List<Decision>()
                {
                    new Decision()
                    {
                        DecisionType = "ScheduleActivityTask",
                        ScheduleActivityTaskDecisionAttributes = new ScheduleActivityTaskDecisionAttributes()
                        {
                            ActivityType = ACTIVITY_TYPE,
                            ActivityId = DateTime.Now.Ticks.ToString(),
                            TaskList = TASKLIST
                        }
                    }
                }
            };

            Client.RespondDecisionTaskCompletedAsync(respondRequest).Wait();
            return pollActivity();
        }

        void Sleep()
        {
            UtilityMethods.Sleep(SleepDuration);
        }
    }
}
