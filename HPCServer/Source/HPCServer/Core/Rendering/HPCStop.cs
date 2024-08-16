using System.Diagnostics;

namespace HPCServer.Core.Rendering
{
    public static class HPCStop
    {
        public static void Kill(Process process)
        {
            process.Kill();
        }
    }
}
