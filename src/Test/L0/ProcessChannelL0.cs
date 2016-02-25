using System;
using Xunit;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class ProcessChannelL0
    {      
        //RunAsync is an "echo" type service which reads
        //one message and sends back to the server same data 
        public static async Task RunAsync(string[] args)
        {
            using (var client = new ProcessChannel())
            {
                SemaphoreSlim signal = new SemaphoreSlim(0, 1);
                Func<JobRequestMessage, CancellationToken, Task> echoFunc = async (message, ct) =>
                {
                    var cs2 = new CancellationTokenSource();
                    await client.SendAsync(message, cs2.Token);
                    signal.Release();
                };
                client.JobRequestMessageReceived += echoFunc;
                client.StartClient(args[1], args[2]);
                // Wait server calls us once and we reply back
                await signal.WaitAsync(5000);
                client.JobRequestMessageReceived -= echoFunc;
                await client.Stop();
            }
        }

        //RunIPCEndToEnd test starts another process (the RunAsync function above),
        //sends one packet and receives one packet using ProcessChannel class,
        //and finally verifies if the data we sent is identical to what we have received
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task RunIPCEndToEnd()
        {
            using (var server = new ProcessChannel())
            {
                SemaphoreSlim signal = new SemaphoreSlim(0, 1);
                JobRequestMessage result = null;                
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = null;
                JobEnvironment environment = new JobEnvironment();
                List<TaskInstance> tasks = new List<TaskInstance>();
                Guid JobId = Guid.NewGuid();
                var jobRequest = new JobRequestMessage(plan, timeline, JobId, "someJob", environment, tasks);
                Func<JobRequestMessage, CancellationToken, Task> verifyFunc = (message, ct) =>
                {
                    result = message;
                    signal.Release();
                    return Task.CompletedTask;
                };
                server.JobRequestMessageReceived += verifyFunc;
                Process jobProcess;
                server.StartServer((p1, p2) =>
                {
                    string clientFileName = "Test";
#if OS_WINDOWS
                    clientFileName += ".exe";
#endif
                    jobProcess = new Process();
                    jobProcess.StartInfo.FileName = clientFileName;
                    jobProcess.StartInfo.Arguments = "spawnclient " + p1 + " " + p2;
                    jobProcess.EnableRaisingEvents = true;                    
                    jobProcess.Start();
                });
                var cs = new CancellationTokenSource();
                await server.SendAsync(jobRequest, cs.Token);

                bool timedOut = !await signal.WaitAsync(5000);
                // Wait until response is received
                if (timedOut)
                {
                    Assert.True(false, "Test timed out.");
                }
                else {
                    Assert.True(jobRequest.JobId.Equals(result.JobId) && jobRequest.JobName.Equals(result.JobName));
                }
                server.JobRequestMessageReceived -= verifyFunc;
                await server.Stop();
            }
        }
    }
}