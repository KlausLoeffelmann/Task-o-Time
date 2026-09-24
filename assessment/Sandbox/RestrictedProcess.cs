using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Collections.Generic;

// Compiled only by the trusted guest controller. No host signing key or oracle is present.
public static class RestrictedProcess
{
    [StructLayout(LayoutKind.Sequential)] struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [StructLayout(LayoutKind.Sequential)] struct SecurityCapabilities { public IntPtr AppContainerSid,Capabilities; public uint Count,Reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct StartupInfo {
        public int Size; public string Reserved, Desktop, Title;
        public uint X,Y,XSize,YSize,XCount,YCount,Fill,Flags;
        public ushort Show, ReservedLength; public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct StartupInfoEx { public StartupInfo Startup; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits {
        public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorking, MaxWorking;
        public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits {
        public BasicLimits Basic; public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)] struct Accounting {
        public long UserTime,KernelTime,PeriodUserTime,PeriodKernelTime;
        public uint PageFaults,TotalProcesses,ActiveProcesses,TerminatedProcesses;
    }
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool CreateRestrictedToken(IntPtr token,uint flags,uint disableCount,ref SidAndAttributes disabled,uint privilegeCount,IntPtr privileges,uint restrictCount,[In] SidAndAttributes[] restricted,out IntPtr result);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool SetTokenInformation(IntPtr token,int kind,IntPtr info,int size);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr info,int size,out int length);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool IsTokenRestricted(IntPtr token);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text,uint revision,out IntPtr descriptor,out uint size);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetSecurityDescriptorDacl(IntPtr descriptor,out bool present,out IntPtr dacl,out bool defaulted);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetSecurityDescriptorSacl(IntPtr descriptor,out bool present,out IntPtr sacl,out bool defaulted);
    [DllImport("advapi32.dll",SetLastError=true)] static extern uint SetSecurityInfo(IntPtr handle,int type,uint information,IntPtr owner,IntPtr group,IntPtr dacl,IntPtr sacl);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint SetNamedSecurityInfo(string name,int type,uint information,IntPtr owner,IntPtr group,IntPtr dacl,IntPtr sacl);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool DeleteFile(string path);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool CreateProcessAsUser(IntPtr token,string executable,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfoEx startup,out ProcessInfo process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,uint flags,ref IntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr attribute,IntPtr value,IntPtr size,IntPtr previous,IntPtr returned);
    [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateFile(string path,uint access,uint share,ref SecurityAttributes security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr security,string name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int kind,ref ExtendedLimits limits,int size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateJobObject(IntPtr job,uint code);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool QueryInformationJobObject(IntPtr job,int kind,out Accounting accounting,int size,IntPtr returned);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetProcessWindowStation(IntPtr station);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateWindowStation(string name,uint flags,uint access,ref SecurityAttributes security);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,uint flags,uint access,ref SecurityAttributes security);
    [DllImport("user32.dll",SetLastError=true)] static extern bool CloseWindowStation(IntPtr station);
    [DllImport("user32.dll",SetLastError=true)] static extern bool CloseDesktop(IntPtr desktop);
    static IntPtr workerStation=IntPtr.Zero,workerDesktop=IntPtr.Zero;
    static string workerDesktopName;
    static string appContainerName;
    static IntPtr appContainerSid=IntPtr.Zero;
    [DllImport("userenv.dll",CharSet=CharSet.Unicode)] static extern int CreateAppContainerProfile(string name,string display,string description,IntPtr capabilities,uint count,out IntPtr sid);
    [DllImport("userenv.dll",CharSet=CharSet.Unicode)] static extern int DeleteAppContainerProfile(string name);
    [DllImport("advapi32.dll")] static extern IntPtr FreeSid(IntPtr sid);

    public static string CreateOwnedAppContainer() {
        if(appContainerSid!=IntPtr.Zero) throw new InvalidOperationException("Owned AppContainer already exists.");
        appContainerName="OwnedReplay"+Guid.NewGuid().ToString("N");
        int result=CreateAppContainerProfile(appContainerName,appContainerName,"Guest-only diagnostic; no capabilities.",IntPtr.Zero,0,out appContainerSid);
        if(result!=0) Marshal.ThrowExceptionForHR(result);
        return new SecurityIdentifier(appContainerSid).Value;
    }

    public static string CurrentIntegrity() {
        IntPtr token=IntPtr.Zero,info=IntPtr.Zero;
        try {
            Check(OpenProcessToken(GetCurrentProcess(),8,out token),"Inspect controller token");
            int length; GetTokenInformation(token,25,IntPtr.Zero,0,out length);
            info=Marshal.AllocHGlobal(length);
            Check(GetTokenInformation(token,25,info,length,out length),"Inspect controller integrity");
            return new SecurityIdentifier(((SidAndAttributes)Marshal.PtrToStructure(info,typeof(SidAndAttributes))).Sid).Value;
        } finally { if(info!=IntPtr.Zero) Marshal.FreeHGlobal(info); if(token!=IntPtr.Zero) CloseHandle(token); }
    }

    public static string InitializeWorkerDesktop() {
        if(workerDesktopName!=null) throw new InvalidOperationException("Worker desktop already initialized.");
        IntPtr descriptor=IntPtr.Zero; uint size;
        string name="OwnedReplay"+Guid.NewGuid().ToString("N");
        string user=WindowsIdentity.GetCurrent().User.Value;
        Check(ConvertStringSecurityDescriptorToSecurityDescriptor(
            "D:P(A;;GA;;;SY)(A;;GA;;;"+user+")(A;;GA;;;RC)"+
            (appContainerSid==IntPtr.Zero?"":"(A;;GA;;;"+new SecurityIdentifier(appContainerSid).Value+")")+
            "S:(ML;;NW;;;LW)",1,out descriptor,out size),"Private desktop security");
        var security=new SecurityAttributes { Length=Marshal.SizeOf(typeof(SecurityAttributes)),Descriptor=descriptor,Inherit=0 };
        IntPtr original=GetProcessWindowStation();
        try {
            workerStation=CreateWindowStation(name,0,0x000F037F,ref security);
            Check(workerStation!=IntPtr.Zero,"Create private worker window station");
            Check(SetProcessWindowStation(workerStation),"Select private worker window station");
            try {
                workerDesktop=CreateDesktop("worker",IntPtr.Zero,IntPtr.Zero,0,0x000F01FF,ref security);
                Check(workerDesktop!=IntPtr.Zero,"Create private worker desktop");
            } finally { Check(SetProcessWindowStation(original),"Restore controller window station"); }
            workerDesktopName=name+"\\worker";
            return workerDesktopName;
        } finally { if(descriptor!=IntPtr.Zero) LocalFree(descriptor); }
    }

    public static void CloseWorkerDesktop() {
        if(workerDesktop!=IntPtr.Zero) { Check(CloseDesktop(workerDesktop),"Close private worker desktop"); workerDesktop=IntPtr.Zero; }
        if(workerStation!=IntPtr.Zero) { Check(CloseWindowStation(workerStation),"Close private worker window station"); workerStation=IntPtr.Zero; }
        workerDesktopName=null;
        if(appContainerSid!=IntPtr.Zero) { FreeSid(appContainerSid); appContainerSid=IntPtr.Zero; }
        if(appContainerName!=null) {
            int result=DeleteAppContainerProfile(appContainerName); appContainerName=null;
            if(result!=0) Marshal.ThrowExceptionForHR(result);
        }
    }

    public static string EnvironmentBlock(string[] entries) {
        if(entries==null||entries.Length==0||entries.Length>256) throw new ArgumentException("Explicit bounded worker environment required.");
        var values=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string entry in entries) {
            if(entry==null||entry.IndexOf('\0')>=0) throw new ArgumentException("Invalid worker environment entry.");
            int split=entry.IndexOf('=');
            if(split<1) throw new ArgumentException("Worker environment name required.");
            string name=entry.Substring(0,split);
            if(name.IndexOfAny(new char[] {'\r','\n'})>=0||values.ContainsKey(name)) throw new ArgumentException("Duplicate or invalid environment name.");
            values.Add(name,entry.Substring(split+1));
        }
        var block=new StringBuilder();
        foreach(var entry in values) block.Append(entry.Key).Append('=').Append(entry.Value).Append('\0');
        block.Append('\0');
        if(block.Length>32767) throw new ArgumentException("Worker environment exceeds Windows limit.");
        return block.ToString();
    }

    static void Check(bool success,string operation) {
        if(!success) {
            int error=Marshal.GetLastWin32Error();
            throw new Win32Exception(error,operation+"; Win32="+error+": "+new Win32Exception(error).Message);
        }
    }
    static IntPtr Sid(string value) {
        var sid=new SecurityIdentifier(value); var bytes=new byte[sid.BinaryLength]; sid.GetBinaryForm(bytes,0);
        var pointer=Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes,0,pointer,bytes.Length); return pointer;
    }
    static string Quote(string value) {
        var result=new StringBuilder("\""); int slashes=0;
        foreach(char c in value) {
            if(c=='\\') { slashes++; continue; }
            if(c=='"') result.Append('\\',slashes*2+1).Append(c);
            else result.Append('\\',slashes).Append(c);
            slashes=0;
        }

        return result.Append('\\',slashes*2).Append('"').ToString();
    }

    public static void ProtectController(string directory) {
        IntPtr descriptor; uint size;
        Check(ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;OICI;NWNR;;;HI)",1,out descriptor,out size),"Controller label");
        try {
            bool present,defaulted; IntPtr sacl;
            Check(GetSecurityDescriptorSacl(descriptor,out present,out sacl,out defaulted)&&present,"Read controller label");
            uint error=SetNamedSecurityInfo(directory,1,0x10,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,sacl);
            if(error!=0) throw new Win32Exception((int)error,"Protect controller directory");
            error=SetSecurityInfo(GetCurrentProcess(),6,0x10,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,sacl);
            if(error!=0) throw new Win32Exception((int)error,"Protect controller process memory");
        }
        finally { LocalFree(descriptor); }
    }

    public static void Publish(string path,byte[] content) {
        // DeleteFile unlinks a file/reparse leaf; it never recursively traverses a candidate directory.
        if(!DeleteFile(path)) {
            int error=Marshal.GetLastWin32Error();
            if(error!=2) throw new Win32Exception(error,"Remove untrusted result leaf");
        }
        using(var stream=new System.IO.FileStream(path,System.IO.FileMode.CreateNew,System.IO.FileAccess.Write,System.IO.FileShare.None))
            stream.Write(content,0,content.Length);
    }

    public static uint Run(string executable,string[] arguments,string directory,string stdout,string stderr,int timeoutMilliseconds,string[] environment) {
        return RunCore(executable,arguments,directory,stdout,stderr,timeoutMilliseconds,environment,false);
    }

    public static uint RunAppContainerDiagnostic(string executable,string[] arguments,string directory,string stdout,string stderr,int timeoutMilliseconds,string[] environment) {
        if(appContainerSid==IntPtr.Zero) throw new InvalidOperationException("Guest-only AppContainer diagnostic must be initialized.");
        return RunCore(executable,arguments,directory,stdout,stderr,timeoutMilliseconds,environment,true);
    }

    static uint RunCore(string executable,string[] arguments,string directory,string stdout,string stderr,int timeoutMilliseconds,string[] environment,bool appContainer) {
        if(workerDesktopName==null) throw new InvalidOperationException("Private worker desktop required.");
        string environmentText=EnvironmentBlock(environment);
        IntPtr original=IntPtr.Zero,token=IntPtr.Zero,job=IntPtr.Zero,admin=IntPtr.Zero,restricted=IntPtr.Zero,world=IntPtr.Zero,
            low=IntPtr.Zero,label=IntPtr.Zero,descriptor=IntPtr.Zero,daclInfo=IntPtr.Zero,attributes=IntPtr.Zero,handleList=IntPtr.Zero;
        IntPtr input=IntPtr.Zero,output=IntPtr.Zero,error=IntPtr.Zero,environmentBlock=IntPtr.Zero,capabilities=IntPtr.Zero;
        var process=new ProcessInfo(); bool assigned=false,attributesInitialized=false;
        try {
            Check(OpenProcessToken(GetCurrentProcess(),0x000F01FF,out original),"OpenProcessToken");
            admin=Sid("S-1-5-32-544"); restricted=Sid("S-1-5-12"); world=Sid("S-1-1-0");
            var deny=new SidAndAttributes { Sid=admin };
            var restrict=new SidAndAttributes[] { new SidAndAttributes { Sid=restricted }, new SidAndAttributes { Sid=world } };
            // RC+World is the legacy write-restricted diagnostic, not an RC allowlist.
            // AppContainer instead adds the kernel's zero-capability confinement at process creation.
            Check(CreateRestrictedToken(original,appContainer?1u:1u|8u,1,ref deny,0,IntPtr.Zero,
                appContainer?0u:(uint)restrict.Length,restrict,out token),"CreateRestrictedToken");
            if(!appContainer) Check(IsTokenRestricted(token),"Token restriction missing");
            int labelSize=Marshal.SizeOf(typeof(SidAndAttributes))+new SecurityIdentifier("S-1-16-4096").BinaryLength;
            low=Sid("S-1-16-4096"); label=Marshal.AllocHGlobal(labelSize);
            Marshal.StructureToPtr(new SidAndAttributes { Sid=low, Attributes=0x20 },label,false);
            Check(SetTokenInformation(token,25,label,labelSize),"Set low integrity");
            string user=WindowsIdentity.GetCurrent().User.Value; uint descriptorSize;
            Check(ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;GA;;;SY)(A;;GA;;;"+user+")(A;;GA;;;RC)"+
                (appContainer?"(A;;GA;;;"+new SecurityIdentifier(appContainerSid).Value+")":""),1,out descriptor,out descriptorSize),"Default DACL");
            bool present,defaulted; IntPtr dacl;
            Check(GetSecurityDescriptorDacl(descriptor,out present,out dacl,out defaulted)&&present,"Read default DACL");
            daclInfo=Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(daclInfo,dacl);
            Check(SetTokenInformation(token,6,daclInfo,IntPtr.Size),"Set restricted default DACL");
            var security=new SecurityAttributes { Length=Marshal.SizeOf(typeof(SecurityAttributes)), Inherit=1 };
            input=CreateFile("NUL",0x80000000,3,ref security,3,0x80,IntPtr.Zero);
            output=CreateFile(stdout,0x40000000,1,ref security,2,0x80,IntPtr.Zero);
            error=CreateFile(stderr,0x40000000,1,ref security,2,0x80,IntPtr.Zero);
            Check(input!=new IntPtr(-1)&&output!=new IntPtr(-1)&&error!=new IntPtr(-1),"Open standard streams");
            int attributeCount=appContainer?2:1;
            IntPtr size=IntPtr.Zero; InitializeProcThreadAttributeList(IntPtr.Zero,attributeCount,0,ref size);
            attributes=Marshal.AllocHGlobal(size);
            Check(InitializeProcThreadAttributeList(attributes,attributeCount,0,ref size),"Initialize handle allowlist");
            attributesInitialized=true;
            handleList=Marshal.AllocHGlobal(IntPtr.Size*3);
            Marshal.Copy(new IntPtr[] { input,output,error },0,handleList,3);
            Check(UpdateProcThreadAttribute(attributes,0,new IntPtr(0x20002),handleList,new IntPtr(IntPtr.Size*3),IntPtr.Zero,IntPtr.Zero),"Set handle allowlist");
            if(appContainer) {
                capabilities=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SecurityCapabilities)));
                Marshal.StructureToPtr(new SecurityCapabilities { AppContainerSid=appContainerSid },capabilities,false);
                Check(UpdateProcThreadAttribute(attributes,0,new IntPtr(0x20009),capabilities,
                    new IntPtr(Marshal.SizeOf(typeof(SecurityCapabilities))),IntPtr.Zero,IntPtr.Zero),"Set zero-capability AppContainer");
            }
            var startup=new StartupInfoEx { Attributes=attributes };
            startup.Startup.Size=Marshal.SizeOf(typeof(StartupInfoEx));
            startup.Startup.Flags=0x100;
            startup.Startup.Input=input; startup.Startup.Output=output; startup.Startup.Error=error;
            startup.Startup.Desktop=workerDesktopName;
            job=CreateJobObject(IntPtr.Zero,null); Check(job!=IntPtr.Zero,"Create owned job");
            var limits=new ExtendedLimits(); limits.Basic.Flags=0x2000;
            Check(SetInformationJobObject(job,9,ref limits,Marshal.SizeOf(typeof(ExtendedLimits))),"Set kill-on-close job");
            var command=new StringBuilder(Quote(executable));
            foreach(string argument in arguments) command.Append(' ').Append(Quote(argument));
            environmentBlock=Marshal.StringToHGlobalUni(environmentText);
            Check(CreateProcessAsUser(token,executable,command,IntPtr.Zero,IntPtr.Zero,true,0x00080000|0x00000004|0x08000000|0x00000400,
                environmentBlock,directory,ref startup,out process),"Create restricted process");
            if(appContainer) {
                IntPtr childToken=IntPtr.Zero,info=IntPtr.Zero;
                try {
                    Check(OpenProcessToken(process.Process,8,out childToken),"Inspect AppContainer token before execution");
                    int length; info=Marshal.AllocHGlobal(256);
                    Check(GetTokenInformation(childToken,29,info,256,out length)&&Marshal.ReadInt32(info)==1,"Actual AppContainer token required");
                    Check(GetTokenInformation(childToken,31,info,256,out length)&&
                        new SecurityIdentifier(Marshal.ReadIntPtr(info)).Value==new SecurityIdentifier(appContainerSid).Value,"Unexpected AppContainer SID");
                    Check(GetTokenInformation(childToken,30,info,256,out length)&&Marshal.ReadInt32(info)==0,"Unexpected AppContainer capabilities");
                    Check(GetTokenInformation(childToken,25,info,256,out length),"Inspect AppContainer integrity");
                    Check(new SecurityIdentifier(((SidAndAttributes)Marshal.PtrToStructure(info,typeof(SidAndAttributes))).Sid).Value=="S-1-16-"+4096,"Actual LOW AppContainer required");
                } finally { if(info!=IntPtr.Zero) Marshal.FreeHGlobal(info); if(childToken!=IntPtr.Zero) CloseHandle(childToken); }
            }
            Check(AssignProcessToJobObject(job,process.Process),"Assign restricted process to owned job"); assigned=true;
            Check(ResumeThread(process.Thread)!=0xFFFFFFFF,"Resume restricted process");
            uint wait=WaitForSingleObject(process.Process,(uint)timeoutMilliseconds);
            bool timedOut=wait==258;
            Check(timedOut||wait==0,"Wait for restricted process");
            uint exit=124;
            if(!timedOut) Check(GetExitCodeProcess(process.Process,out exit),"Read actual process exit");
            // No descendants may remain active while trusted output collection runs.
            Check(TerminateJobObject(job,exit),"Stop remaining owned descendants");
            var deadline=DateTime.UtcNow.AddSeconds(10);
            while(true) {
                Accounting accounting;
                Check(QueryInformationJobObject(job,1,out accounting,Marshal.SizeOf(typeof(Accounting)),IntPtr.Zero),"Query owned descendants");
                if(accounting.ActiveProcesses==0) break;
                if(DateTime.UtcNow>=deadline) throw new TimeoutException("Owned descendants did not terminate.");
                System.Threading.Thread.Sleep(10);
            }
            if(timedOut) throw new TimeoutException("Restricted command timed out after owned descendants terminated.");
            return exit;
        }
        finally {
            if(environmentBlock!=IntPtr.Zero) Marshal.FreeHGlobal(environmentBlock);
            if(capabilities!=IntPtr.Zero) Marshal.FreeHGlobal(capabilities);
            if(process.Process!=IntPtr.Zero&&!assigned) TerminateProcess(process.Process,125);
            foreach(IntPtr handle in new IntPtr[] { job,process.Thread,process.Process,input,output,error,token,original })
                if(handle!=IntPtr.Zero&&handle!=new IntPtr(-1)) CloseHandle(handle);
            if(attributes!=IntPtr.Zero) {
                if(attributesInitialized) DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
            foreach(IntPtr pointer in new IntPtr[] { admin,restricted,world,low,label,daclInfo,handleList })
                if(pointer!=IntPtr.Zero) Marshal.FreeHGlobal(pointer);
            if(descriptor!=IntPtr.Zero) LocalFree(descriptor);
        }
    }
}
