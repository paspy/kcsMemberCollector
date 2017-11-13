using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace kcsMemberCollector {

    public class App {
        private static IServiceProvider BuildServices() {
            var services = new ServiceCollection();

            services.AddTransient<KancolleAuth>();

            services.AddSingleton<ILoggerFactory, LoggerFactory>();
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            services.AddLogging((builder) => builder.SetMinimumLevel(LogLevel.Trace));

            var serviceProvider = services.BuildServiceProvider();

            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            //configure NLog
            loggerFactory.AddNLog(new NLogProviderOptions { CaptureMessageTemplates = true, CaptureMessageProperties = true });
            loggerFactory.ConfigureNLog(Path.Combine(Directory.GetCurrentDirectory(), "nlog.config"));

            return serviceProvider;
        }

        /// <summary>
        /// Command line arguments
        /// -t/--token 
        /// -s/--server
        /// -r/--range
        /// -n/--numberOfThread
        /// </summary>
        /// <param name="args"></param>
        public static void Main(string[] args) {

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var service = BuildServices();
            var au = service.GetService<KancolleAuth>();
            au.Initialize("kyuubee@me.com", "");

            CommandLineParser.CommandLineParser parser = new CommandLineParser.CommandLineParser();
            CommandLineParser.Arguments.ValueArgument<string> tokenArg =
                new CommandLineParser.Arguments.ValueArgument<string>('t', "token", "User token for Kancolle identification.");
            tokenArg.Optional = false;
            tokenArg.AllowMultiple = false;

            CommandLineParser.Arguments.ValueArgument<int> serverIdArg =
                new CommandLineParser.Arguments.ValueArgument<int>('s', "server", "Kancolle server ID address to connect.");
            serverIdArg.Optional = false;
            serverIdArg.AllowMultiple = false;

            CommandLineParser.Arguments.ValueArgument<long> rangeArg =
                new CommandLineParser.Arguments.ValueArgument<long>('r', "range", "Assuming member ID range.");
            rangeArg.Optional = true;
            rangeArg.AllowMultiple = true;


            CommandLineParser.Arguments.ValueArgument<int> numberOfClientArg =
                new CommandLineParser.Arguments.ValueArgument<int>('n', "thread", "Divided the task into multi clients (Max 50)");
            numberOfClientArg.Optional = true;
            numberOfClientArg.AllowMultiple = false;

            parser.Arguments.Add(tokenArg);
            parser.Arguments.Add(serverIdArg);
            parser.Arguments.Add(rangeArg);
            parser.Arguments.Add(numberOfClientArg);

            try {
                parser.ParseCommandLine(args);

                int numOfClients = 1;
                if (numberOfClientArg.Parsed && numberOfClientArg.Value > 1 && numberOfClientArg.Value <= 50) {
                    numOfClients = numberOfClientArg.Value;
                }

                string token = tokenArg.Value;
                string serverAddr = WorldServerAddr[serverIdArg.Value - 1];
                string serverName = WorldServerName[serverIdArg.Value - 1];
                if (rangeArg.Parsed) {
                    long min = rangeArg.Values[0];
                    long max = rangeArg.Values[1];
                    List<Range> threadGroup = null;
                    List<Task> kcClients = new List<Task>();
                    if (numOfClients > 1) {
                        int n = GenerateRanges((int)min, (int)max, numOfClients, out threadGroup);

                        for (int i = 0; i < n; i++) {
                            kcClients.Add(new Collector(token, serverAddr, threadGroup[i].lo, threadGroup[i].hi, serverName, "Client ID: " + i).StartCacheMembersAsync());
                        }
                        Task.WhenAll(kcClients).Wait();

                    } else {
                        var client = new Collector(token, serverAddr, min, max, serverName);
                        client.StartCacheMembersAsync().Wait();

                    }
                }
            } catch (Exception e) {
                System.Console.WriteLine(e.Message);
            }
        }



        public class Range { public int lo; public int hi; }

        public static readonly List<string> WorldServerAddr = new List<string>() {
               "203.104.209.71"  , // 01.横須賀鎮守府   
               "203.104.209.87"  , // 02.呉鎮守府        
               "125.6.184.16"    , // 03.佐世保鎮守府    
               "125.6.187.205"   , // 04.舞鶴鎮守府      
               "125.6.187.229"   , // 05.大湊警備府      
               "203.104.209.134" , // 06.トラック泊地    
               "203.104.209.167" , // 07.リンガ泊地      
               "203.104.248.135" , // 08.ラバウル基地    
               "125.6.189.7"     , // 09.ショートランド泊地
               "125.6.189.39"    , // 10.ブイン基地
               "125.6.189.71"    , // 11.タウイタウイ泊地 
               "125.6.189.103"   , // 12.パラオ泊地
               "125.6.189.135"   , // 13.ブルネイ泊地    
               "125.6.189.167"   , // 14.単冠湾泊地      
               "125.6.189.215"   , // 15.幌筵泊地        
               "125.6.189.247"   , // 16.宿毛湾泊地      
               "203.104.209.23"  , // 17.鹿屋基地        
               "203.104.209.39"  , // 18.岩川基地        
               "203.104.209.55"  , // 19.佐伯湾泊地      
               "203.104.209.102" , // 20.柱島泊地        
        };

        public static readonly List<string> WorldServerName = new List<string>() {
              /*203.104.209.71 */ "01.横須賀鎮守府",
              /*203.104.209.87 */ "02.呉鎮守府",
              /*125.6.184.16   */ "03.佐世保鎮守府",
              /*125.6.187.205  */ "04.舞鶴鎮守府",
              /*125.6.187.229  */ "05.大湊警備府",
              /*203.104.209.134*/ "06.トラック泊地",
              /*203.104.209.167*/ "07.リンガ泊地",
              /*203.104.248.135*/ "08.ラバウル基地",
              /*125.6.189.7    */ "09.ショートランド泊地",
              /*125.6.189.39   */ "10.ブイン基地",
              /*125.6.189.71   */ "11.タウイタウイ泊地",
              /*125.6.189.103  */ "12.パラオ泊地",
              /*125.6.189.135  */ "13.ブルネイ泊地",
              /*125.6.189.167  */ "14.単冠湾泊地",
              /*125.6.189.215  */ "15.幌筵泊地",
              /*125.6.189.247  */ "16.宿毛湾泊地",
              /*203.104.209.23 */ "17.鹿屋基地",
              /*203.104.209.39 */ "18.岩川基地",
              /*203.104.209.55 */ "19.佐伯湾泊地",
              /*203.104.209.102*/ "20.柱島泊地",
        };

        public static int GenerateRanges(int min, int max, int numberOfRanges, out List<Range> ranges) {
            int i;
            int[] bucket_sizes = new int[numberOfRanges];
            ranges = new List<Range>();

            int even_length = (max - min + 1) / numberOfRanges;
            for (i = 0; i < numberOfRanges; ++i)
                bucket_sizes[i] = even_length;

            /* distribute surplus as evenly as possible across buckets */
            int surplus = (max - min + 1) % numberOfRanges;
            for (i = 0; surplus > 0; --surplus, i = (i + 1) % numberOfRanges)
                bucket_sizes[i] += 1;

            int n = 0, k = min;
            for (i = 0; i < numberOfRanges && k <= max; ++i, ++n) {
                //ranges[i].lo = k;
                //ranges[i].hi = k + bucket_sizes[i] - 1;
                Range r = new Range();
                r.lo = k;
                r.hi = k + bucket_sizes[i] - 1;
                ranges.Add(r);
                k += bucket_sizes[i];
            }
            return n;
        }
    }
}

