﻿namespace NServiceBus.IntegrationTests.Support
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class ScenarioRunner
    {
        public static void Run(IEnumerable<RunDescriptor> runDescriptors,
                               IEnumerable<BehaviorDescriptor> behaviorDescriptors,
                               IList<IScenarioVerification> shoulds)
        {
            var totalRuns = runDescriptors.Count();

            var runNumber = 1;

            foreach (var runDescriptor in runDescriptors)
            {
             
                Console.Out.Write("Running test for : {0}", runDescriptor.Key);

                if (totalRuns > 1)
                    Console.Out.WriteLine(" - Permutation: {0}({1})", runNumber, totalRuns);
                Console.Out.Write("");

                PrintSettings(runDescriptor.Settings);

                var runTimer = new Stopwatch();

                runTimer.Start();
                var runners = InitializeRunners(runDescriptor, behaviorDescriptors);

                try
                {
                    PerformScenarios(runners);

                }
                finally
                {
                    foreach (var runner in runners)
                    {
                        AppDomain.Unload(runner.AppDomain);
                    }
                }

                runTimer.Stop();

                foreach (var descriptor in behaviorDescriptors)
                {
                    if (descriptor.Context == null)
                        continue;

                    shoulds.Where(s => s.ContextType == descriptor.Context.GetType()).ToList()
                           .ForEach(v => v.Verify(descriptor.Context));
                }

                Console.Out.WriteLine("Result: Successful - Duration: {0}", runTimer.Elapsed);
                Console.Out.WriteLine("------------------------------------------------------");

                runNumber++;
            }
        }

        static IDictionary<Type, string> CreateRoutingTable(RunDescriptor runDescriptor, IEnumerable<BehaviorDescriptor> behaviorDescriptors)
        {
            var routingTable = new Dictionary<Type, string>();

            foreach (var behaviorDescriptor in behaviorDescriptors)
            {
                routingTable[behaviorDescriptor.EndpointBuilderType] = GetEndpointNameForRun(runDescriptor,behaviorDescriptor);
            }

            return routingTable;
        }

        private static void PrintSettings(IEnumerable<KeyValuePair<string, string>> settings)
        {
            Console.WriteLine("");
            Console.WriteLine("Using settings:");
            foreach (KeyValuePair<string, string> pair in settings)
            {
                Console.Out.WriteLine("   {0}: {1}", pair.Key, pair.Value);
            }
            Console.WriteLine();
        }

        static void PerformScenarios(IEnumerable<ActiveRunner> runners)
        {
            var endpoints = runners.Select(r => r.Instance).ToList();

            StartEndpoints(endpoints);

            var startTime = DateTime.UtcNow;
            var maxTime = TimeSpan.FromSeconds(30);

            Task.WaitAll(endpoints.Select(endpoint => Task.Factory.StartNew(() => SpinWait.SpinUntil(endpoint.Done, maxTime))).Cast<Task>().ToArray());

            try
            {
                if ((DateTime.UtcNow - startTime) > maxTime)
                {
                    throw new ScenarioException(GenerateTestTimedOutMessage(endpoints, maxTime));
                }
            }
            finally
            {
                StopEndpoints(endpoints);
            }
        }

        static string GenerateTestTimedOutMessage(List<EndpointRunner> endpoints, TimeSpan maxTime)
        {
            var sb = new StringBuilder();

            sb.AppendLine(string.Format("The maximum time limit for this test({0}s) has been reached",
                                        maxTime.TotalSeconds));
            sb.AppendLine("----------------------------------------------------------------------------");
            sb.AppendLine("Endpoint statuses:");
            endpoints.ForEach(e =>
                {
                    sb.AppendLine(string.Format("{0} - {1}", e.Name(), e.Done() ? "Done" : "Not done"));
                });

            return sb.ToString();
        }

        static void StartEndpoints(IEnumerable<EndpointRunner> endpoints)
        {
            var tasks = endpoints.Select(endpoint => Task.Factory.StartNew(() =>
                {
                    endpoint.Start();
                    endpoint.ApplyWhens();
                })).ToArray();

            Task.WaitAll(tasks);
        }

        static void StopEndpoints(IEnumerable<EndpointRunner> endpoints)
        {
            var tasks = endpoints.Select(endpoint => Task.Factory.StartNew(endpoint.Stop)).ToArray();

            Task.WaitAll(tasks);
        }

        static List<ActiveRunner> InitializeRunners(RunDescriptor runDescriptor, IEnumerable<BehaviorDescriptor> behaviorDescriptors)
        {
            var runners = new List<ActiveRunner>();
            var routingTable = CreateRoutingTable(runDescriptor, behaviorDescriptors);

            Console.WriteLine("");
            Console.WriteLine("Endpoints:");

            foreach (var behaviorDescriptor in behaviorDescriptors)
            {
                var endpointName = GetEndpointNameForRun(runDescriptor, behaviorDescriptor);
          
                Console.Out.WriteLine("     - {0}", endpointName);
          
                var runner = PrepareRunner(endpointName);
                behaviorDescriptor.Init();

                if (!runner.Instance.Initialize(runDescriptor, behaviorDescriptor, routingTable))
                {
                    throw new ScenarioException(string.Format("Endpoint {0} failed to initialize", runner.Instance.Name()));
                }

                runners.Add(runner);

            }
            return runners;
        }

        static string GetEndpointNameForRun(RunDescriptor runDescriptor, BehaviorDescriptor behaviorDescriptor)
        {
            var endpointName = Conventions.EndpointNamingConvention(behaviorDescriptor.EndpointBuilderType) + "." + runDescriptor.Key + "." + 
                               runDescriptor.Permutation;
            return endpointName;
        }

        static ActiveRunner PrepareRunner(string appDomainName)
        {
            var domainSetup = new AppDomainSetup
                {
                    ApplicationBase = AppDomain.CurrentDomain.SetupInformation.ApplicationBase,
                    LoaderOptimization = LoaderOptimization.SingleDomain
                };

            var appDomain = AppDomain.CreateDomain(appDomainName, AppDomain.CurrentDomain.Evidence, domainSetup);

            return new ActiveRunner
                {
                    Instance = (EndpointRunner)appDomain.CreateInstanceAndUnwrap(Assembly.GetExecutingAssembly().FullName, typeof(EndpointRunner).FullName),
                    AppDomain = appDomain
                };
        }
    }

    class ActiveRunner
    {
        public EndpointRunner Instance { get; set; }
        public AppDomain AppDomain { get; set; }
    }
}