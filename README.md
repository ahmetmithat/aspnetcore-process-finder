# aspnetcore-process-finder
This tool is used to list the processes hosting .NET Core web applications when hosted with IIS.

The purpose of this tool:

Consider the following scenario:

- You are running several ASP.NET Core applications and hosting them in IIS working as reverse proxy.
- There are several dotnet.exe or other processes hosting your actual dotnet core application.
- You are facing problems and would like to attach ProcDump to the correct process and capture dumps using ProcDump swithces.
- As of date, it is not an easy task to find out which process actually hosts the application in interest.
- This tool gets the application pool name as an argument and find the actual process hosting the dotnet core application.
- So this tool depends on ProcDump availability.

Usage
=============
Setting up and running the tool is easy:

•	Copy the ListAspNetCoreProcs.exe and ListAspNetCoreProcs.exe.config to the server.
•	Configure application settings in the ListAspNetCoreProcs.exe.config file:

  o	Set the full path of ProcDump, E.g.: <add key="ProcDumpFullPath" value="C:\Downloads\SysInternals\ProcDump.exe"/>.
    Note that if the ProcDumpFullPath is missing or empty, the tool will ask the path of the ProcDump when it is needed to run.
    
  o	Set the process names the tool will look for, with comma seperated values. E.g.: <add key="DotnetCoreProcessNames" value="dotnet.exe, myapp.exe, coreapp.exe"/>.
    If this is missing or empty, the tool will assume that the process name is dotnet.exe.
    
•	You need to run the tool in an elevated command prompt.
•	If you run the tool with /? or -h switch it will show the instructions and some sample commands for capturing memory dumps.
•	If you run the tool without any switch or with -l or --list switch, it will list the processes hosting the ASP.NET Core application, if there is any ASP.NET Core app running:
•	ProcDump is used to capture the memory dumps. Usual ProcDump switches work as expected.
•	To attach ProcDump and capture dumps, you need to give the name of the IIS application pool as first parameter and then add the usual ProcDump switch.
  For example, run the following command to creat first chance exception dumps in the c:\dumps folder for the processes running with “Def Leppard Fan Site” application pool:
    
    ListAspNetCoreProcs.exe "Def Leppard Fan Site" -e 1 -f * c:\dumps.
