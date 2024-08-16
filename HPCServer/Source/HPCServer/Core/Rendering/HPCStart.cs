using HPCServer.Core.Configuration;
using HPCServer.Core.Rendering;
using HPCServer.Misc;
using System.Diagnostics;

namespace HPCServer.Core.Engines
{
    public class HPCStart
    {
        public Configuration.HPCServer Configuration;

        public String SuccessMessage = String.Empty;
        public String ErrorMessage = String.Empty;

        public HPCStart(Configuration.HPCServer configuration)
        {
            Configuration = configuration;
        }

        public Process? StartRender(HPCStartArgs renderArgs)
        {
            SuccessMessage = String.Empty;
            ErrorMessage = String.Empty;

            Int32 engineIndex = -1;

            if (renderArgs.EngineId == null || renderArgs.Arguments == null)
            {
                ErrorMessage = UserWriter.Log(AppStrings.ErrorInvalidRenderArgs);
                return null;
            }

            if (Configuration.RenderingEngines == null)
            {
                ErrorMessage = UserWriter.Log(AppStrings.ErrorInvalidRenderEngineArgument);
                return null;
            }

            for (Int32 i = 0; i < Configuration.RenderingEngines.Length; i++)
            {
                String? identifier = Configuration.RenderingEngines[i].EngineId;
                if (identifier == null) continue;

                if (identifier.ToLower() == renderArgs.EngineId.ToLower())
                {
                    engineIndex = i;
                    break;
                }
            }

            if (engineIndex == -1)
            {
                ErrorMessage = UserWriter.Log(AppStrings.ErrorRequestedRenderingEngineNotConfigured + renderArgs.EngineId);
                return null;
            }

            HPCEngine renderingEngine = Configuration.RenderingEngines[engineIndex];

            SuccessMessage = UserWriter.Log(AppStrings.SuccessUsingRenderingEngine + renderArgs.EngineId);

            if (renderingEngine.ExecutablePath == null)
            {
                ErrorMessage = UserWriter.Log(AppStrings.ErrorRequestedRenderingEngineNotConfigured + renderArgs.EngineId);
                return null;
            }

            Process? process = CommandInvoker.InvokeProcess(renderingEngine.ExecutablePath, renderArgs.Arguments);
            if (process == null)
                return null;

            return process;
        }
    }
}
