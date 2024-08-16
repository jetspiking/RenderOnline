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
        private Core.Configuration.HPCServer? _hpcServerArgs;
        private Process? _renderProcess = null;

        private UInt64? _taskId { get; set; } = null;
        private Boolean _isLastSuccessfull { get; set; } = true;

        public RequestHandler(WebApplication? app)
        {
            const String configuration = "Configuration.json";
            String configurationFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuration);
            Console.WriteLine("Searching for configuration file: "+configurationFilePath);

            //List<HPCEngine> engines = new();
            //engines.Add(new HPCEngine("Blender", "\"C:\\Program Files\\Blender Foundation\\Blender 4.2\\blender.exe\""));

            //Core.Configuration.HPCServer hpcServer = new(engines.ToArray(), "5000");
            //JsonManager.SerializeToFile(hpcServer, configurationFilePath);
            //return;

            _hpcServerArgs = JsonManager.DeserializeFromFile<Core.Configuration.HPCServer>(configurationFilePath);

            if (_hpcServerArgs == null)
            {
                Console.WriteLine($"{configuration} not found!");
                Console.ReadKey();
                return;
            }

            app.Urls.Add("http://localhost:"+ _hpcServerArgs.Port);
            app.UseHttpsRedirection();
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
            HPCStatusRequestResponse statusResponse = new(this._renderProcess == null, this._taskId, this._isLastSuccessfull);

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(statusResponse));
        }

        public async Task HandleStartRenderRequest(HttpContext httpContext)
        {
            if (_hpcServerArgs == null) return;

            // There is another render in progress.
            if (this._renderProcess != null)
            {
                await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, AppStrings.ErrorRenderInProgress, true);
                return;
            }

            HPCStart hpcRender = new(_hpcServerArgs);

            HPCStartArgs? arguments = await httpContext.Request.ReadFromJsonAsync<HPCStartArgs>();
            if (arguments == null)
            {
                // Request does not contain expected arguments object.
                await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, AppStrings.ErrorInvalidRenderArgs, true);
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
                await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, hpcRender.ErrorMessage, true);

            await CreateRenderResponseObject(httpContext, StatusCodes.Status200OK, hpcRender.SuccessMessage, false);
        }

        public async Task HandleStopRenderRequest(HttpContext httpContext)
        {
            if (this._renderProcess == null)
            {
                // There is no active render.
                await CreateRenderResponseObject(httpContext, StatusCodes.Status200OK, AppStrings.SuccessNoActiveRender, false);
            }
            else
                try
                {
                    HPCStopArgs? arguments = await httpContext.Request.ReadFromJsonAsync<HPCStopArgs>();
                    if (arguments == null)
                    {
                        // Request does not contain expected arguments object.
                        await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, AppStrings.ErrorInvalidRenderArgs, true);
                        return;
                    }

                    if (this._taskId != arguments.TaskId)
                    {
                        // Task identifier does not match.
                        await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, AppStrings.ErrorInvalidTaskId, true);
                        return;
                    }

                    // Terminate the rendering task.
                    HPCStop.Kill(this._renderProcess);
                }
                catch (Exception e)
                {
                    // An error occured.
                    UserWriter.Log(e.Message);

                    await CreateRenderResponseObject(httpContext, StatusCodes.Status400BadRequest, AppStrings.ErrorStoppingRender, true);
                }

            // Render was stopped successfully.
            await CreateRenderResponseObject(httpContext, StatusCodes.Status200OK, AppStrings.SuccessStoppingRender, false);
        }

        public async void AssignWhenRenderFinished(Process process)
        {
            // Assign rendering process.
            this._renderProcess = process;

            // Wait until render is completed.
            await process.WaitForExitAsync();

            // If exit code is 0, the process was completed successfully.
            this._isLastSuccessfull = process.ExitCode == 0;

            // Render complete, reset process.
            this._renderProcess = null;
        }

        private async Task CreateRenderResponseObject(HttpContext httpContext, Int32 statusCode, String message, Boolean isError)
        {
            HPCRenderRequestResponse response = new(isError, message);

            httpContext.Response.StatusCode = statusCode;
            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(response));
        }
    }
}
