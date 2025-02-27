using Microsoft.AspNetCore.Mvc.RazorPages;
using HPCServer.Core;
using HPCServer.Core.Engines;
using HPCServer.Core.Configuration;
using HPCServer.Misc;
using HPCServer.Core.Rendering;
using System.Diagnostics;
using HPCServer.Core.Responses;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;

namespace HPCServer
{
    public class RequestHandler
    {
        private Core.Configuration.HPCServer _hpcServerConfiguration;
        
        private Process? _renderProcess = null;
        private UInt64 _taskId { get; set; } = 0;
        private Boolean _isLastSuccessfull { get; set; } = true;
        private DateTime? _taskExitTime = null;

        public RequestHandler(WebApplication app, Core.Configuration.HPCServer configuration)
        {
            this._hpcServerConfiguration = configuration;

            app.UseAuthorization();

            // Get machine status.
            app.MapGet("/hpc/status", HandleStatusRequest);

            // Start a rendering assignment.
            app.MapPost("/hpc/start", HandleStartRenderRequest);

            // Stop a rendering assignment.
            app.MapPost("/hpc/stop", HandleStopRenderRequest);

            app.Run();
        }

        public async Task HandleStatusRequest(HttpContext httpContext)
        {
            HPCTask? renderTask;
            if (_renderProcess == null)
            {
                renderTask = null;
            }
            else
            {
                // Use exit time or current time, depending on whether the process is still busy.
                DateTime taskTime = this._taskExitTime ?? DateTime.Now;
                renderTask = new(this._taskId, this._isLastSuccessfull, this._taskExitTime == null, (UInt64)(taskTime - this._renderProcess.StartTime).TotalSeconds);
            }

            List<String> engineIds = new();

            if (_hpcServerConfiguration.RenderingEngines != null)
                foreach (HPCEngine engine in _hpcServerConfiguration.RenderingEngines)
                    engineIds.Add(engine.EngineId);

            HPCStatusRequestResponse statusResponse = new(engineIds.ToArray(), renderTask);

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(statusResponse));
        }

        public async Task HandleStartRenderRequest(HttpContext httpContext)
        {
            if (_hpcServerConfiguration == null) return;

            if (this._taskExitTime == null && this._renderProcess != null)
            {
                // There is another render in progress.
                await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, AppStrings.ErrorRenderInProgress, false);
                return;
            }

            HPCStart hpcRender = new(_hpcServerConfiguration);

            HPCStartArgs? arguments = await httpContext.Request.ReadFromJsonAsync<HPCStartArgs>();
            if (arguments == null)
            {
                // Request does not contain expected arguments object.
                await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, AppStrings.ErrorInvalidRenderArgs, false);
                return;
            }

            // Start the rendering task.
            this._taskId = arguments.TaskId;
            this._isLastSuccessfull = false;

            Process? toRender = hpcRender.StartRender(arguments);

            // Depending on whether the result does not exists, something went wrong in starting the rendering assignment.
            if (toRender != null)
                AssignWhenRenderFinished(toRender);
            else
                await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, hpcRender.ErrorMessage, false);

            await CreateRenderResponseObject(httpContext, StatusCodes.Status200OK, hpcRender.SuccessMessage, true);
        }

        public async Task HandleStopRenderRequest(HttpContext httpContext)
        {
            if (this._taskExitTime != null || this._renderProcess == null)
            {
                // There is no active render.
                await CreateRenderResponseObject(httpContext, StatusCodes.Status200OK, AppStrings.SuccessNoActiveRender, true);
                return;
            }
            else
                try
                {
                    HPCStopArgs? arguments = await httpContext.Request.ReadFromJsonAsync<HPCStopArgs>();
                    if (arguments == null)
                    {
                        // Request does not contain expected arguments object.
                        await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, AppStrings.ErrorInvalidRenderArgs, false);
                        return;
                    }

                    if (this._taskId != arguments.TaskId)
                    {
                        // Task identifier does not match.
                        await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, AppStrings.ErrorInvalidTaskId, false);
                        return;
                    }

                    // Terminate the rendering task.
                    HPCStop.Kill(this._renderProcess);
                }
                catch (Exception e)
                {
                    // An error occured.
                    UserWriter.Log(e.Message);

                    await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, AppStrings.ErrorStoppingRender, false);
                    return;
                }

            // Render was stopped successfully.
            await CreateRenderResponseObject(httpContext, StatusCodes.Status200OK, AppStrings.SuccessStoppingRender, true);
        }

        public async void AssignWhenRenderFinished(Process process)
        {
            // Remove potential previously allocated task completion time.
            this._taskExitTime = null;

            // Assign rendering process.
            this._renderProcess = process;

            // Wait until render is completed.
            await process.WaitForExitAsync();

            // Assign task completion time.
            this._taskExitTime = DateTime.Now;

            // If exit code is 0, the process was completed successfully.
            this._isLastSuccessfull = process.ExitCode == 0;

            // Render complete, reset process.
            // this._renderProcess = null;
        }

        private async Task CreateRenderResponseObject(HttpContext httpContext, Int32 statusCode, String message, Boolean isSuccess)
        {
            HPCRenderRequestResponse response = new(isSuccess, message);

            httpContext.Response.StatusCode = statusCode;
            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(response));
        }
    }
}
