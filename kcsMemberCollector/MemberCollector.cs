using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
namespace kcsMemberCollector {
    public class MemberCollector {

        public class Range { public int lo; public int hi; }

        public static readonly List<string> WorldServerAddr = new List<string>() {
               "203.104.209.71"  , // 横須賀鎮守府   
               "203.104.209.87"  , // 呉鎮守府        
               "125.6.184.16"    , // 佐世保鎮守府    
               "125.6.187.205"   , // 舞鶴鎮守府      
               "125.6.187.229"   , // 大湊警備府      
               "125.6.187.253"   , // トラック泊地    
               "125.6.188.25"    , // リンガ泊地      
               "203.104.248.135" , // ラバウル基地    
               "125.6.189.7"     , // ショートランド泊地
               "125.6.189.39"    , // ブイン基地
               "125.6.189.71"    , // タウイタウイ泊地 
               "125.6.189.103"   , // パラオ泊地
               "125.6.189.135"   , // ブルネイ泊地    
               "125.6.189.167"   , // 単冠湾泊地      
               "125.6.189.215"   , // 幌筵泊地        
               "125.6.189.247"   , // 宿毛湾泊地      
               "203.104.209.23"  , // 鹿屋基地        
               "203.104.209.39"  , // 岩川基地        
               "203.104.209.55"  , // 佐伯湾泊地      
               "203.104.209.102" , // 柱島泊地        
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

        /// <summary>
        /// Command line arguments
        /// -t/--token 
        /// -s/--server
        /// -r/--range
        /// -n/--numberOfThread
        /// </summary>
        /// <param name="args"></param>
        public static void Main(string[] args) {
            CommandLineParser.CommandLineParser parser = new CommandLineParser.CommandLineParser();
            CommandLineParser.Arguments.ValueArgument<string> tokenArg =
                new CommandLineParser.Arguments.ValueArgument<string>('t', "token", "User token for Kancolle identification.");
            tokenArg.Optional = false;
            tokenArg.AllowMultiple = false;

            CommandLineParser.Arguments.ValueArgument<string> serverArg =
                new CommandLineParser.Arguments.ValueArgument<string>('s', "server", "Kancolle server IP address to connect.");
            serverArg.Optional = false;
            serverArg.AllowMultiple = false;

            CommandLineParser.Arguments.ValueArgument<long> rangeArg =
                new CommandLineParser.Arguments.ValueArgument<long>('r', "range", "Assuming member ID range.");
            rangeArg.Optional = true;
            rangeArg.AllowMultiple = true;

            CommandLineParser.Arguments.ValueArgument<long> singleIdArg =
                new CommandLineParser.Arguments.ValueArgument<long>('i', "id", "Single Id.");
            singleIdArg.Optional = true;
            singleIdArg.AllowMultiple = true;

            CommandLineParser.Arguments.ValueArgument<int> numberOfThreadArg =
                new CommandLineParser.Arguments.ValueArgument<int>('n', "thread", "Divided the task into multi threads (Max 15)");
            numberOfThreadArg.Optional = true;
            numberOfThreadArg.AllowMultiple = false;

            parser.Arguments.Add(tokenArg);
            parser.Arguments.Add(serverArg);
            parser.Arguments.Add(rangeArg);
            parser.Arguments.Add(singleIdArg);
            parser.Arguments.Add(numberOfThreadArg);

            try {
                parser.ParseCommandLine(args);
                int numOfThreads = 1;
                if (numberOfThreadArg.Parsed && numberOfThreadArg.Value > 1 && numberOfThreadArg.Value <= 100) {
                    numOfThreads = numberOfThreadArg.Value;
                }

                string token = tokenArg.Value;
                string server = serverArg.Value;

                if (singleIdArg.Parsed) {
                    List<long> ids = singleIdArg.Values;
                    var client = new kcClient(token, server, ids);
                    client.StartCacheMembersAsync().Wait();

                } else if (rangeArg.Parsed) {
                    long min = rangeArg.Values[0];
                    long max = rangeArg.Values[1];
                    List<Range> threadGroup = null;
                    List<Task> kcClients = new List<Task>();
                    if (numOfThreads > 1) {
                        int n = GenerateRanges((int)min, (int)max, numOfThreads, out threadGroup);

                        for (int i = 0; i < n; i++) {
                            kcClients.Add(new kcClient(token, server, threadGroup[i].lo, threadGroup[i].hi, "Thread ID: " + i).StartCacheMembersAsync());
                        }
                        Task.WhenAll(kcClients).Wait();

                    } else {
                        var client = new kcClient(token, server, min, max);
                        client.StartCacheMembersAsync().Wait();

                    }
                }

            } catch (System.Exception e) {
                System.Console.WriteLine(e.Message);
            }
        }
    }
}
