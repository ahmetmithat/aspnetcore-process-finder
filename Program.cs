/* **********************************

AUTHOR: ahmetmb
DATE: 2018-11-23
The purpose of this tool:

Consider the following scenario:

- You are running several ASP.NET Core applications and hosting them in IIS working as reverse proxy.
- There are several dotnet.exe or other processes hosting your actual dotnet core application.
- You are facing problems and would like to attach ProcDump to the correct process and capture dumps using ProcDump swithces.
- As of date, it is not an easy task to find out which process actually hosts the application in interest.
- This tool gets the application pool name as an argument and find the actual process hosting the dotnet core application.
- So this tool depends on ProcDump availability.

TODO:

- REMOVE -debug IMPLEMENTATION. It is possible to let procdump instance running in the background without an UI. Killing that procdump will kill the process, very dangerous.
- Should the user have the possibility to choose which instance of a specific process to monitor? This means that the PID might be given as an argument.
- Check if .exe is not given in the config file, if so add it.
************************************ */

using System;
using System.Diagnostics;
using System.Configuration;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Management;

namespace ListAspNetCoreProcs
{
    class Program
    {
        static string appName;
        static string appPoolNameToMonitor;
        static bool errorOccured = false;
        static int numOfProcessToMonitor = 0;
        static int numOfProcessFound = 0;
        static string[] argsToProcDump;
        static List<ProcessDetails> ProcessList;
        static Dictionary<int, string> AppPoolProcessList;
        static int numberOfArgs;
        static bool isDebugMode;
        static string ProcDumpFullPath;

        // Console formatting settings
        static ConsoleColor defaultBackgroundColor = Console.BackgroundColor;
        static ConsoleColor defaultForegroundColor = Console.ForegroundColor;
        static ConsoleColor highlightColor = ConsoleColor.DarkRed;
        static ConsoleColor debugColor = ConsoleColor.DarkCyan;
        static ConsoleColor errorColor = ConsoleColor.Red;
        static ConsoleColor instructionsColor = ConsoleColor.Yellow;

        public struct ApplicationPoolDetails
        {
            public string appPoolName;
            public int processId;
            public string processorArchitecture;
        }

        // Each matching process will be kept as a struct
        // ...including the PID, bitness and processor type
        public struct ProcessDetails : IComparable<ProcessDetails>
        {
            public string processName;
            public string appPoolName;  // If the process has an APP_POOL_ID environment variable then it is our concern...
            public int processId;
            public int parentProcessId;
            public string processorArchitecture;
            public string fullUserName;

            public int CompareTo(ProcessDetails comparePart)
            {
                return this.appPoolName.CompareTo(comparePart.appPoolName);
            }
        }

        static void Main(string[] args)
        {
            isDebugMode = false;
            numberOfArgs = args.Length;
            
            // Reserved for the next version implementations...
            if (numberOfArgs > 0 && args[numberOfArgs - 1] == "-debug")
            {
                isDebugMode = true;
                numberOfArgs = numberOfArgs - 1;
            }

            NameValueCollection appSettings = null;

            try
            {
                appSettings = ConfigurationManager.AppSettings;
            }
            catch (Exception exc)
            {
                Console.ForegroundColor = errorColor;
                Console.WriteLine("An exception has occured while reading the configuration file.\n");
                Console.WriteLine(exc.Message);
                Console.WriteLine("\nPlease make sure you are running the command in an elevated command prompt and the configuration file is not missing in the working directory.");
                Console.ForegroundColor = defaultForegroundColor;
                Environment.Exit(-1);
            }

            var AppPoolList = new List<ApplicationPoolDetails>();
            Dictionary<int, string> AppPoolProcessList = new Dictionary<int, string>();

            appName = Process.GetCurrentProcess().ProcessName;
            appPoolNameToMonitor = string.Empty;

            // Console formatting related
            if (errorColor == defaultBackgroundColor)
            {
                errorColor = ConsoleColor.DarkYellow;
            }

            // Console formatting related
            if (instructionsColor == defaultBackgroundColor)
            {
                errorColor = ConsoleColor.DarkBlue;
            }

            // Console formatting related
            if (highlightColor == defaultBackgroundColor)
            {
                highlightColor = ConsoleColor.DarkBlue;
            }

            // Console formatting related
            if (debugColor == defaultBackgroundColor)
            {
                debugColor = ConsoleColor.White;
            }

            Console.WriteLine();
            // Printing copyright notice...
            // PrintCopyright();

            // Set args to static object so it can be reached by different methods easily
            argsToProcDump = args;

            // Read ProcDump path from config file
            // ...and make sure that it is set correctly.
            ProcDumpFullPath = appSettings["ProcDumpFullPath"];
            if ((string.IsNullOrEmpty(ProcDumpFullPath) || !File.Exists(ProcDumpFullPath)) && numberOfArgs > 0)
            {
                Console.ForegroundColor = errorColor;
                Console.WriteLine("Cannot find ProcDump path. Please make sure that the ProcDump path is correctly set in the configuration file.");
                Console.ForegroundColor = instructionsColor;
                Console.WriteLine("\nSample:\n<add key=\"ProcDumpFullPath\" value=\"C:\\Downloads\\ProcDump.exe\"/>");
                Console.ForegroundColor = defaultForegroundColor;

                Console.WriteLine("\nDo you want to provide the full path of ProcDump.exe now (y/n)?");
                ConsoleKey keyPressed = Console.ReadKey().Key;
                if (keyPressed == ConsoleKey.Y)
                {
                    SetProcDumpPathManually();
                }
                else
                {
                    Environment.Exit(-1);
                }
            }

            
            // ProcessList is our list which contains the processes we are interested in
            // If there is no process found then show the sample
            ProcessList = GetDotnetCoreProcessList();

            if (ProcessList.Count == 0)
            {
                Console.WriteLine("Cannot find a process hosting an ASP.NET Core application associated with an IIS application pool.");
                Console.WriteLine("\nIf your ASP.NET Core application is self-hosted (other than dotnet.exe) please make sure it is set in the configuration file.");
                Console.WriteLine("\nPress H to see the instructions or any other key to exit.");
                ConsoleKey keyPressed = Console.ReadKey().Key;
                if (keyPressed == ConsoleKey.H)
                {
                    Console.WriteLine("\n");
                    PrintInstructions();
                }

                Environment.Exit(0);
            }

            // If no argument is passed then list the processes,
            // ...and give an option to show help
            if (args.Length == 0)
            {
                Console.WriteLine("No application pool name given, printing ASP.NET Core processes running.\n");
                PrintListOfProcesses();

                Console.WriteLine("\nPress H to see the instructions or any other key to exit.");
                ConsoleKey keyPressed = Console.ReadKey().Key;
                if (keyPressed == ConsoleKey.H)
                {
                    Console.WriteLine("\n");
                    PrintInstructions();
                }

                Environment.Exit(0);
            }

            // Show the process list if the first argument is passed as:
            // --list, -l, or *
            if (args[0] == "--list" || args[0] == "-l" || args[0] == "*")
            {
                PrintListOfProcesses();
                Environment.Exit(0);
            }

            // Show the help if the first argument is passed as:
            // --help, -h, /?, ? or -?
            if (args[0] == "--help" || args[0] == "-h" || args[0] == "/?" || args[0] == "?" || args[0] == "-?")
            {
                PrintInstructions();
                Environment.Exit(0);
            }

            // First argument is passed as application pool name
            // Call the SearchApplicationPoolInTheList method to build the list of matching processes
            appPoolNameToMonitor = args[0];
            SearchApplicationPoolInTheList(appPoolNameToMonitor);
        }

        private static void SetProcDumpPathManually()
        {
            Console.WriteLine("\nPlease provide the full path of ProcDump.exe. E.g.: c:\\downloads\\procdump.exe");
            ProcDumpFullPath = Console.ReadLine();
            if (!File.Exists(ProcDumpFullPath))
            {
                Console.WriteLine("\n{0} is not found. Press T to try again or any other key to exit and make sure that the ProcDump path is correctly set in the configuration file.", ProcDumpFullPath);
                ConsoleKey keyPressed = Console.ReadKey().Key;
                if (keyPressed == ConsoleKey.T)
                {
                    SetProcDumpPathManually();
                }
                else
                {
                    Environment.Exit(0);
                }
            }
            else
            {
                try
                {
                    Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                    config.AppSettings.Settings["ProcDumpFullPath"].Value = ProcDumpFullPath;
                    config.Save(ConfigurationSaveMode.Modified);
                    ConfigurationManager.RefreshSection("appSettings");
                    Console.BackgroundColor = highlightColor;
                    Console.WriteLine("\nConfiguration file is updated with the new ProcDump path.\n");
                    Console.BackgroundColor = defaultBackgroundColor;
                }
                catch (Exception)
                {
                    Console.BackgroundColor = highlightColor;
                    Console.WriteLine("\nNew ProcDump path will be used but the configuration file cannot be updated. Please update it manually.\n");
                    Console.BackgroundColor = defaultBackgroundColor;
                }
            }
        }

        // This method goes through each process in the process list
        // ...and checks if the environment variable matches the application pool name given as first parameter
        // It calls ProcDump helper methods which builds ProcDump command and then launches the ProcDump
        private static void SearchApplicationPoolInTheList(string appPoolNameToMonitor)
        {
            Console.WriteLine("Searching for the \"{0}\" application pool in the process list...", appPoolNameToMonitor);

            string processName;
            string appPoolName;
            string processorArchitecture;
            int processId;
            int compare;

            foreach (var process in ProcessList)
            {
                processName = process.processName;
                appPoolName = process.appPoolName;
                processorArchitecture = process.processorArchitecture;
                processId = process.processId;

                // Matches the pool name, this is what we are looking for...
                compare = String.Compare(appPoolName, appPoolNameToMonitor, StringComparison.InvariantCultureIgnoreCase);
                if (compare == 0)
                {
                    numOfProcessFound++;
                    Console.WriteLine("\nFound a {0}.exe process for \"{1}\" application pool and the process ID is {2}. Processor type is {3}.", processName, appPoolName, processId, processorArchitecture);
                    Console.WriteLine("Starting ProcDump...");
                    // BuildProcDumpCommand buils the parameters,
                    // ...and then calls the method which launches the ProcDump
                    // If an error occures during ProcDump launch errorOccured is increased
                    BuildProcDumpCommand(argsToProcDump, processName, processId, processorArchitecture);
                }
            }

            Console.WriteLine("\nTotal number of process(es) scanned: {0}", ProcessList.Count);
            Console.WriteLine("Total number of process(es) matched for \"{0}\" application pool: {1}", appPoolNameToMonitor, numOfProcessFound);
            Console.WriteLine("Total number of ProcDump instances started: {0}", numOfProcessToMonitor);

            // No process is being monitored
            // Either there is no matching process for the given application pool name,
            // ...or an error occured while ProcDump is launched
            if (numOfProcessToMonitor == 0)
            {
                // error occured while ProcDump is launched
                if (!errorOccured)
                {
                    Console.BackgroundColor = highlightColor;
                    Console.WriteLine("\nNo process found to attach with ProcDump.");
                    Console.BackgroundColor = defaultBackgroundColor;
                }

                // Shows options to list the processes,
                // ...or to show the help
                Console.WriteLine("\nPress L to list the available ASP.NET Core processes running.");
                Console.WriteLine("\nPress H to see the instructions and usage samples.");
                ConsoleKey keyPressed = Console.ReadKey().Key;
                if (keyPressed == ConsoleKey.L)
                {
                    Console.WriteLine("\n");
                    PrintListOfProcesses();
                }
                else if (keyPressed == ConsoleKey.H)
                {
                    Console.WriteLine("\n");
                    PrintInstructions();
                }
                errorOccured = false;
            }
            // Successfull...ProcDump is launched...
            // ...but this does not gurantee that the did not exit with an error
            // ...E.g.: parameters passed to ProcDump could be wrong so ProcDump could end without attaching
            // ...it is not an easy task to "inject" to ProcDump window so showing an explanation...
            // ...which prints the full ProcDump command run in the window,
            // ...and if there is a problem ProcDump command can be copied and run seperately to see what the problem is...
            else
            {
                Console.Write("\nPlease check the ProcDump window(s) launched for the results. If windows are closed quickly then it is possible that the parameteres you passed are not correct.");
                Console.ForegroundColor = instructionsColor;
                Console.Write("\nTIP: ");
                Console.ForegroundColor = defaultForegroundColor;
                Console.Write("You can try running the ");
                Console.BackgroundColor = highlightColor;
                Console.Write("ProcDump command printed above");
                Console.BackgroundColor = defaultBackgroundColor;
                Console.WriteLine(" on an elevated command prompt to see if you are getting an unexpected result / error.\n");
                Console.WriteLine("Press L to list the processes scanned or any other key to exit.");
                ConsoleKey keyPressed = Console.ReadKey().Key;
                if (keyPressed == ConsoleKey.L)
                {
                    Console.WriteLine("\n");
                    PrintListOfProcesses();
                }
            }
        }

        // Method which just prints the instructions
        private static void PrintInstructions()
        {
            Console.ForegroundColor = instructionsColor;
            Console.WriteLine("INSTRUCTIONS:");
            Console.ForegroundColor = defaultForegroundColor;

            Console.WriteLine("\n1) Set the process names hosting your ASP.NET Core application in the configuration file. E.g.:\n");

            Console.ForegroundColor = instructionsColor;
            Console.WriteLine("<add key=\"DotnetCoreProcessNames\" value=\"dotnet.exe, MyAspNetCoreApp.exe\"/>");
            Console.ForegroundColor = defaultForegroundColor;

            Console.WriteLine("\n2) This tool uses ProcDump to attach to processes and capture the dump so set the full path in the configuration file. E.g.:\n");
            Console.ForegroundColor = instructionsColor;
            Console.WriteLine("<add key=\"ProcDumpFullPath\" value=\"C:\\Downloads\\ProcDump.exe\"/>");
            Console.ForegroundColor = defaultForegroundColor;

            Console.WriteLine("\n3) Provide the IIS application pool name as first parameter then provide the other ProcDump parameters as usual. Do not give any PID or process name, this tool's purpose is to find the correct PID for you and attach it using ProcDump.");

            Console.ForegroundColor = instructionsColor;
            Console.Write("\nSAMPLE 1: ");
            Console.ForegroundColor = defaultForegroundColor;
            Console.WriteLine("Following command will create a memory dump of dotnet.exe process associated with 'My App Pool With Space in Name' in c:\\dumps folder.\n");
            Console.BackgroundColor = highlightColor;
            Console.WriteLine("{0}.exe \"My App Pool With Space in Name\" c:\\dumps", appName);
            Console.BackgroundColor = defaultBackgroundColor;

            Console.ForegroundColor = instructionsColor;
            Console.Write("\nSAMPLE 2: ");
            Console.ForegroundColor = defaultForegroundColor;
            Console.WriteLine("Following command will create a memory dump of dotnet.exe process associated with MyAppPool in the current folder when a first chance exception happens.\n");
            Console.BackgroundColor = highlightColor;
            Console.WriteLine("{0}.exe MyAppPool -ma -f * -e 1", appName);
            Console.BackgroundColor = defaultBackgroundColor;
        }

        // Method which just lists the processes
        private static void PrintListOfProcesses()
        {
            Console.WriteLine("Dotnet core processes with an application pool assigned:\n");
            Console.WriteLine("{0,-24} {1,-8} {2,-24} {3,-6}", "Process Name", "PID", "App Pool", "Arch");
            Console.Write(new string('-', 24) + " " + new string('-', 8) + " " + new string('-', 24) + " " + new string('-', 6) + "\n");
            ProcessList.Sort();

            foreach (var process in ProcessList)
            {
                string processName = process.processName;
                string appPoolName = process.appPoolName;
                string processorArchitecture = process.processorArchitecture;
                int processId = process.processId;
                string fullUserName = process.fullUserName;

                Console.WriteLine("{0,-24} {1,-8} {2,-24} {3,-6}", processName, processId, appPoolName, processorArchitecture);
            }

            Console.WriteLine("\nTotal {0} processes found.", ProcessList.Count);
        }

        // Method which just prints the copyright stuff
        private static void PrintCopyright()
        {
            // Do nothing - reserverd for later use.
        }

        // This method builds the ProcDump parameters,
        // ...and then calls the method which launches the ProcDump
        // If the architecture is 64bit then -64 switch will be used
        // PID or Process Name should not be given as parameter since the tool's purpose is to find it out...
        private static void BuildProcDumpCommand(string[] args, string processName, int ProcId, string ProcArc)
        {
            if (ProcArc == "Unknown")
            {
                Console.ForegroundColor = errorColor;
                Console.WriteLine("\nUnable to detect the bitness of the process.");
                Console.WriteLine("Press 1 if it is 32-bit, or press 2 if it is 64-bit process, or any other key to exit.");
                Console.ForegroundColor = defaultForegroundColor;
                ConsoleKey keyPressed = Console.ReadKey().Key;
                Console.WriteLine();

                if (keyPressed == ConsoleKey.D1)
                {
                    ProcArc = "x86";
                }
                else if (keyPressed == ConsoleKey.D2)
                {
                    ProcArc = "AMD64";
                }
                else
                {
                    Environment.Exit(0);
                }
            }

            string ProcDumpArgs = "-accepteula " + ProcId.ToString();

            if (ProcArc != "x86")
                ProcDumpArgs += " -64";

            /*
            FileAttributes attr;
            string timestamp, dumpFileName;
            timestamp = DateTime.Now.ToLocalTime().ToString().Replace(" ", "_").Replace(":","_").Replace(".","_");

            dumpFileName = processName + "__" + appPoolNameToMonitor + "__" + ProcId.ToString() + "__" + timestamp + ".dmp";
            bool pathGiven = false;
            */

            string iArg;

            for (int i = 1; i < numberOfArgs; i++)
            {
                iArg = args[i];
                ProcDumpArgs += " " + iArg;
            }

            /*
            if (!pathGiven)
            {
                ProcDumpArgs += " \"" + dumpFileName + "\"";
            }
            */

            /*
            Console.WriteLine(ProcDumpArgs);
            Environment.Exit(0);
            */

            // Call the method which actually starts the ProcDump
            StartProcDump(ProcDumpArgs, out numOfProcessToMonitor);
        }

        // This is the method where the ProcDump instance is started in its own window
        private static void StartProcDump(string procDumpArgs, out int number)
        {
            number = numOfProcessToMonitor;
            string ProcDumpFullCommand = ProcDumpFullPath + " " + procDumpArgs;

            Process p = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ProcDumpFullPath,
                    Arguments = procDumpArgs
                }
            };

            /* !!! VERY DANGEROUS OPERATION !!!
             * COULD CAUSE PROCDUMP TO LOSE ITS WINDOW
             * AND IT MAY BE NEEDED TO KILL THE PROCDUMP
             * WHICH WOULD KILL THE PROCESS ATTACHED !!!
             * DO NOT IMPLEMENT BELOW
             * RESERVERD FOR FUTURE USE WITH BETTER IMPLEMENTATION

            if (isDebugMode)
            {
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
            }
            
             */

            try
            {
                bool isStarted = p.Start();
                if (isStarted)
                {
                    Console.WriteLine("Successfully started the following command:");
                    Console.BackgroundColor = highlightColor;
                    Console.WriteLine(ProcDumpFullCommand);
                    Console.BackgroundColor = defaultBackgroundColor;
                    number++;

                    // write the output to the same console for debugging purpose
                    /* !!! DO NOT USE !!! VERY DANGEROUS OPERATION
                     * MAY CAUSE PROCDUMP TO LOSE ITS WINDOW
                     * AND IT MAY BE NEEDED TO KILL THE PROCDUMP
                     * WHICH WOULD KILL THE PROCESS ATTACHED !!!
                     
                    if (isDebugMode)
                    {
                        Console.BackgroundColor = debugColor;
                        Console.WriteLine("\n-----debug logs---");
                        while (!p.StandardOutput.EndOfStream)
                        {
                            string line = p.StandardOutput.ReadLine();
                            Console.WriteLine(line);
                        }
                        Console.WriteLine("-----debug logs---");
                        Console.BackgroundColor = defaultBackgroundColor;
                    }
                    */
                }
                else
                {
                    Console.ForegroundColor = errorColor;
                    Console.WriteLine("An error occured while starting {0}", ProcDumpFullCommand);
                    Console.ForegroundColor = defaultForegroundColor;
                }
            }
            catch (Exception exc)
            {
                errorOccured = true;
                Console.ForegroundColor = errorColor;
                Console.WriteLine("\nAn exception has occured while starting the following command:\n\n{0}\n", ProcDumpFullCommand);
                Console.WriteLine(exc.Message);
                Console.WriteLine("\nPlease make sure that the debugger path is correctly set as ProcDumpFullPath in the application configuration file.");
                Console.ForegroundColor = defaultForegroundColor;

                if (numOfProcessFound > 0)
                {
                    Console.WriteLine("\nPress T to set ProcDump path manually and try launching the ProcDump.exe for the NEXT process found, or press any other key to exit to fix the ProcDumpFullPath.\n");
                    ConsoleKey keyPressed = Console.ReadKey().Key;
                    if (keyPressed == ConsoleKey.T)
                    {
                        SetProcDumpPathManually();
                    }
                    else
                    {
                        Environment.Exit(-1);
                    }
                }
            }
        }

        static List<ProcessDetails> GetDotnetCoreProcessList()
        {
            ProcessList = new List<ProcessDetails>();
            
            var appSettings = ConfigurationManager.AppSettings;
            string DotnetCoreProcessNames = appSettings["DotnetCoreProcessNames"];

            if (string.IsNullOrEmpty(DotnetCoreProcessNames) || DotnetCoreProcessNames == "")
            {
                Console.WriteLine("Processes hosting the ASP.NET Core apps is not set in the configuration file.");
                Console.WriteLine("Only dotnet.exe processes will be checked.");
                DotnetCoreProcessNames = "dotnet.exe";
            }

            string[] processList = DotnetCoreProcessNames.Split(',');
            // var list = new List<ProcessDetails>();

            BuildRunningAppPoolProcessesList();

            foreach (string processName in processList)
            {
                BuildDotNetCoreProcessList(processName.Trim());
            }

            return ProcessList;
        }
        static void BuildRunningAppPoolProcessesList()
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = @"c:\windows\system32\inetsrv\appcmd.exe",
                    Arguments = "list wp",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                proc.Start();
            }
            catch (Exception exc)
            {
                Console.ForegroundColor = errorColor;
                Console.WriteLine("\nAn exception has occured while getting the application pool list.");
                Console.WriteLine(exc.Message);
                Console.WriteLine("\nPlease make sure that you are running in elevated command prompt and if the IIS is running.");
                Console.ForegroundColor = defaultForegroundColor;
            }
            int numberOfAppPoolsRunning = 0;
            string[] appPoolDetails;
            int AppPoolProcId;
            string AppPoolName;
            string AppPoolProcessorArchitecture = null;

            AppPoolProcessList = new Dictionary<int, string>();

            while (!proc.StandardOutput.EndOfStream)
            {
                string line = proc.StandardOutput.ReadLine();
                if (!string.IsNullOrEmpty(line))
                {
                    if (!line.StartsWith("ERROR"))
                    {
                        appPoolDetails = line.Split(new char[] { ' ' }, 3);

                        AppPoolProcId = Convert.ToInt32(appPoolDetails[1].Replace("\"", ""));
                        AppPoolName = appPoolDetails[2].Replace("(applicationPool:", "");
                        AppPoolName = AppPoolName.Substring(0, AppPoolName.Length - 1);

                        Process wp = Process.GetProcessById(AppPoolProcId);
                        AppPoolProcessorArchitecture = wp.StartInfo.EnvironmentVariables["PROCESSOR_ARCHITECTURE"];
                        // 

                        ApplicationPoolDetails appPool = new ApplicationPoolDetails();
                        appPool.processId = AppPoolProcId;
                        appPool.appPoolName = AppPoolName;
                        appPool.processorArchitecture = AppPoolProcessorArchitecture;

                        AppPoolProcessList.Add(AppPoolProcId, AppPoolName);

                        numberOfAppPoolsRunning++;
                    }
                    else
                    {
                        Console.WriteLine("WAS is not running or you are not running in an elevated command prompt.");
                        Environment.Exit(-1);
                    }
                }
            }
        }

        static void BuildDotNetCoreProcessList(string processName)
        {
            try
            {
                SelectQuery query = new SelectQuery(@"SELECT * FROM Win32_Process
                                                    where Name = '"
                                                    + processName + "'");

                using (ManagementObjectSearcher getQueryResults = new ManagementObjectSearcher(query))
                {
                    //execute the query
                    ManagementObjectCollection processes = getQueryResults.Get();
                    if (processes.Count > 0)
                    {
                        foreach (ManagementObject process in processes)
                        {
                            process.Get();
                            PropertyDataCollection processProperties = process.Properties;
                            ProcessDetails pd = new ProcessDetails
                            {
                                processName = processName,
                                processId = Convert.ToInt32(processProperties["ProcessID"].Value),
                                parentProcessId = Convert.ToInt32(processProperties["ParentProcessID"].Value)
                            };

                            Process procToGetBitness = Process.GetProcessById(pd.processId);
                            
                            string userDomain = procToGetBitness.StartInfo.EnvironmentVariables["USERDOMAIN"];
                            string userName = procToGetBitness.StartInfo.EnvironmentVariables["USERNAME"];
                            string fullUserName = userDomain + "\\" + userName;

                            pd.fullUserName = fullUserName;

                            string procArc;

                            try
                            {
                                if (CheckProcessBitness(procToGetBitness))
                                {
                                    procArc = "x86";
                                }
                                else
                                {
                                    procArc = "AMD64";
                                }
                            }
                            catch (Exception)
                            {
                                procArc = "Unknown";
                            }

                            pd.processorArchitecture = procArc;

                            try
                            {
                                string appPoolName = AppPoolProcessList[pd.parentProcessId];
                                pd.appPoolName = appPoolName;
                                ProcessList.Add(pd);
                            }
                            catch (Exception)
                            {
                                //
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error occured while executing the query: {0}", ex.Message);
            }
        }

        static bool CheckProcessBitness(Process process)
        {
            if ((Environment.OSVersion.Version.Major > 5)
                || ((Environment.OSVersion.Version.Major == 5) && (Environment.OSVersion.Version.Minor >= 1)))
            {
                bool retVal;

                return Win32Access.IsWow64Process(process.Handle, out retVal) && retVal;
            }

            return false; // not on 64-bit Windows Emulator
        }

        internal static class Win32Access
        {
            [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWow64Process([In] IntPtr process, [Out] out bool wow64Process);
        }
    }
}
