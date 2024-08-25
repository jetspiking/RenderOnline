using System.Diagnostics;

namespace RenderAPI.Misc
{
    public static class CommandInvoker
    {
        public static Process? InvokeProcess(String processName, String processArgument, String? workingDirectory = null)
        {
            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo(processName);
                processStartInfo.Arguments = processArgument;
                if (workingDirectory != null) processStartInfo.WorkingDirectory = workingDirectory;

                Process? _process = Process.Start(processStartInfo);
                if (_process == null) return null;

                return _process;

                //_process.WaitForExit();
                //return _process.ExitCode == 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return null;
            }
        }
    }
}
