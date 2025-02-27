using Microsoft.AspNetCore.Mvc.RazorPages;
using RenderAPI.Core;
using RenderAPI.Core.Configuration;
using RenderAPI.Misc;
using System.Diagnostics;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Data;
using System.Net.Http;
using RenderAPI.Core.Database;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Identity.Data;
using RenderAPI.Core.Responses;
using RenderAPI.Core.Api;
using Microsoft.AspNetCore.Mvc.Formatters;
using RenderAPI.Core.Requests;
using System.Text.RegularExpressions;
using System.Reflection.PortableExecutable;
using RenderAPI.HPCServer;
using Mysqlx.Session;
using Org.BouncyCastle.Crypto.IO;
using System.IO.Compression;

namespace RenderAPI
{
    public class RequestHandler
    {
        private Core.Configuration.RenderServer _renderApiConfiguration;

        private HttpClientHandler _httpClientHandler;

        public RequestHandler(WebApplication app, Core.Configuration.RenderServer configuration)
        {
            this._renderApiConfiguration = configuration;

            this._httpClientHandler = new();

            if (!InitializeDatabaseConnection())
            {
                Console.WriteLine("Failed to connect to the database. Exiting...");
                Console.ReadKey();
                Environment.Exit(1);
            }

            app.UseAuthorization();

            // Get machine status.
            app.MapGet("/renderapi/v1/info", HandleInfoRequest);

            // Queue a rendering assignment.
            app.MapPost("/renderapi/v1/enqueue", HandleEnqueueRequest);

            // Remove a rendering assignment.
            app.MapPost("/renderapi/v1/dequeue", HandleDequeueRequest);

            // Download a rendering result.
            app.MapPost("/renderapi/v1/download", HandleDownloadRequest);

            // Delete a rendering result.
            app.MapPost("/renderapi/v1/delete", HandleDeleteRequest);

            // Start the polling service to monitor render tasks.
            StartPollingService();

            app.Run();
        }

        private Boolean InitializeDatabaseConnection()
        {
            try
            {
                using (MySqlConnection connection = new MySqlConnection(_renderApiConfiguration.ConnectionString))
                {
                    connection.Open();
                    Console.WriteLine("Database connection successful!");
                    return true;
                }
            }
            catch (MySqlException e)
            {
                Console.WriteLine($"Database connection error: {e.Message}");
                return false;
            }
        }

        private ApiUser? AuthenticateAndGetUserInDatabase(HttpContext httpContext)
        {
            String? emailHeader = httpContext.Request.Headers["email"];
            String? tokenHeader = httpContext.Request.Headers["token"];

            if (String.IsNullOrEmpty(emailHeader) || String.IsNullOrEmpty(tokenHeader))
            {
                return null;
            }

            String query = @"
                SELECT 
                    u.user_id,
                    u.first_name,
                    u.last_name,
                    u.email,
                    u.subscription_id,
                    u.is_active,
                    u.token
                FROM 
                    users u 
                WHERE 
                    u.email = @Email AND token = @Token AND u.is_active = 1";

            using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
            {
                connection.Open();
                using (MySqlCommand command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Email", emailHeader);
                    command.Parameters.AddWithValue("@Token", tokenHeader);

                    using (MySqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            UInt16 userId = reader.GetUInt16("user_id");
                            String firstName = reader.GetString("first_name");
                            String lastName = reader.GetString("last_name");
                            String email = reader.GetString("email");
                            Byte subscriptionId = reader.GetByte("subscription_id");
                            Boolean isActive = reader.GetBoolean("is_active");
                            String token = reader.GetString("token");

                            DbUser dbUser = new DbUser(userId, firstName, lastName, email, subscriptionId, isActive, token);
                            return new ApiUser(dbUser.UserId, dbUser.FirstName, dbUser.LastName, dbUser.Email, dbUser.SubscriptionId, dbUser.IsActive);
                        }
                    }
                }
            }

            return null;
        }

        public async Task HandleInfoRequest(HttpContext httpContext)
        {
            ApiUser? user = AuthenticateAndGetUserInDatabase(httpContext);

            if (user == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsync(String.Empty);
                return;
            }

            String retrieveTaskQuery = @"
                SELECT 
                    t.task_id, 
                    t.user_id,
                    t.queue_time,
                    t.start_time, 
                    t.end_time, 
                    t.is_running, 
                    t.is_success,
                    t.render_id,
                    t.machine_id,
                    r.render_id,
                    r.file_name,
                    r.file_path,
                    r.file_size,
                    r.arguments,
                    r.engine_id,
                    e.engine_id,
                    e.name,
                    e.extension,
                    e.download_path,
                    e.render_argument
                FROM 
                    tasks t
                LEFT OUTER JOIN 
                    queue q ON t.task_id = q.task_id
                LEFT OUTER JOIN 
                    renders r ON t.render_id = r.render_id
                LEFT OUTER JOIN
                    engines e ON r.engine_id = e.engine_id
                WHERE 
                    t.user_id = @UserId
                ORDER BY t.task_id DESC
                LIMIT 15";

            using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
            {
                connection.Open();
                using (MySqlCommand command = new MySqlCommand(retrieveTaskQuery, connection))
                {
                    command.Parameters.AddWithValue("@UserId", user.UserId);
                    using (MySqlDataReader reader = command.ExecuteReader())
                    {
                        List<ApiTaskInfo> tasks = new();

                        while (reader.Read())
                        {
                            UInt64 taskId = reader.GetUInt64("task_id");
                            UInt16 userId = reader.GetUInt16("user_id");
                            DateTime? queueTime = !reader.IsDBNull("queue_time") ? reader.GetDateTime("queue_time") : null;
                            DateTime? startTime = !reader.IsDBNull("start_time") ? reader.GetDateTime("start_time") : null;
                            DateTime? endTime = !reader.IsDBNull("end_time") ? reader.GetDateTime("end_time") : null;
                            Boolean isRunning = reader.GetBoolean("is_running");
                            Boolean isSuccess = reader.GetBoolean("is_success");
                            UInt64 renderId = reader.GetUInt64("render_id");
                            Byte? machineId = !reader.IsDBNull("machine_id") ? reader.GetByte("machine_id") : null;

                            DbTask dbTask = new(taskId, userId, queueTime, startTime, endTime, isRunning, isSuccess, renderId, machineId);

                            String fileName = reader.GetString("file_name");
                            String filePath = reader.GetString("file_path");
                            UInt64 fileSize = reader.GetUInt64("file_size");
                            String arguments = reader.GetString("arguments");
                            Byte engineId = reader.GetByte("engine_id");

                            DbRender dbRender = new(renderId, fileName, filePath, fileSize, arguments, engineId);

                            String engineName = reader.GetString("name");
                            String extension = reader.GetString("extension");
                            String downloadPath = reader.GetString("download_path");
                            String renderArgument = reader.GetString("render_argument");

                            DbEngine dbEngine = new(engineId, engineName, extension, downloadPath, renderArgument);

                            ApiTask apiTask = new(dbTask.TaskId, dbTask.UserId, dbTask.QueueTime, dbTask.StartTime, dbTask.EndTime, dbTask.IsRunning, dbTask.IsSuccess, dbTask.RenderId, dbTask.MachineId);
                            ApiRender apiRender = new(dbRender.RenderId, dbRender.FileName, String.Empty, dbRender.FileSize, dbRender.Arguments, dbRender.EngineId);
                            ApiEngine apiEngine = new(dbEngine.EngineId, dbEngine.Name, dbEngine.Extension, dbEngine.DownloadPath, dbEngine.RenderArgument);

                            tasks.Add(new ApiTaskInfo(apiTask, apiRender, apiEngine));
                        }

                        ApiInfoResponse infoResponse = new(user, tasks);
                        httpContext.Response.StatusCode = StatusCodes.Status200OK;
                        await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(infoResponse));
                    }
                }
            }
        }


        public async Task HandleEnqueueRequest(HttpContext httpContext)
        {
            ApiUser? user = AuthenticateAndGetUserInDatabase(httpContext);

            if (user == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, "Unauthorized.")));
                return;
            }

            if (!httpContext.Request.HasFormContentType || !httpContext.Request.Form.Files.Any())
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, "No files were uploaded.")));
                return;
            }

            IFormCollection form = await httpContext.Request.ReadFormAsync();
            String? jsonRequestString = form["request"];

            if (jsonRequestString == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, "Header \"request\" is empty.")));
                return;
            }

            ApiEnqueueRequest? enqueueRequest = JsonConvert.DeserializeObject<ApiEnqueueRequest>(jsonRequestString);
            if (enqueueRequest == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, "Invalid enqueue request arguments.")));
                return;
            }

            IFormFileCollection uploadedFiles = httpContext.Request.Form.Files;

            if (uploadedFiles.Count > 1)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, "Only 1 file may be uploaded per request.")));
                return;
            }

            IFormFile uploadedFile = uploadedFiles[0];

            using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
            {
                connection.Open();

                String retrieveSubscriptionQuery = @"
                    SELECT 
                        s.queue_limit
                    FROM 
                        subscriptions s
                    WHERE 
                        s.subscription_id = @SubscriptionId";

                using (MySqlCommand retrieveSubscriptionCommand = new MySqlCommand(retrieveSubscriptionQuery, connection))
                {
                    retrieveSubscriptionCommand.Parameters.AddWithValue("@SubscriptionId", user.SubscriptionId);
                    Byte queueLimit = 0;

                    using (MySqlDataReader reader = retrieveSubscriptionCommand.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            queueLimit = reader.GetByte("queue_limit");
                        }
                    }

                    String countQueuedTasksQuery = @"
                        SELECT 
                            COUNT(*) 
                        FROM 
                            tasks t
                        JOIN 
                            queue q ON t.task_id = q.task_id
                        WHERE 
                            t.user_id = @UserId";

                    using (MySqlCommand countQueuedTasksCommand = new MySqlCommand(countQueuedTasksQuery, connection))
                    {
                        countQueuedTasksCommand.Parameters.AddWithValue("@UserId", user.UserId);
                        UInt64 currentQueuedTasks = Convert.ToUInt64(countQueuedTasksCommand.ExecuteScalar());

                        if (currentQueuedTasks >= queueLimit)
                        {
                            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Queue limit {queueLimit} reached for your subscription.")));
                            return;
                        }
                    }

                    String retrieveEngineQuery = @"
                        SELECT 
                            e.engine_id,
                            e.name,
                            e.extension,
                            e.download_path,
                            e.render_argument
                        FROM 
                            engines e
                        WHERE 
                            e.engine_id = @EngineId";

                    using (MySqlCommand retrieveEngineCommand = new MySqlCommand(retrieveEngineQuery, connection))
                    {
                        retrieveEngineCommand.Parameters.AddWithValue("@EngineId", enqueueRequest.EngineId);

                        ApiEngine? engine = null;

                        using (MySqlDataReader reader = retrieveEngineCommand.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                Byte engineId = reader.GetByte("engine_id");
                                String name = reader.GetString("name");
                                String extension = reader.GetString("extension");
                                String downloadPath = reader.GetString("download_path");
                                String renderArgument = reader.GetString("render_argument");

                                engine = new(engineId, name, extension, downloadPath, renderArgument);
                            }
                        }

                        if (engine == null)
                        {
                            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Engine with identifier {enqueueRequest.EngineId} not found.")));
                            return;
                        }

                        if (engine.Extension != Path.GetExtension(uploadedFile.FileName))
                        {
                            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Engine extension {engine.Extension} not matching with files extension {Path.GetExtension(uploadedFile.FileName)}.")));
                            return;
                        }

                        DateTime queueTime = DateTime.Now;
                        String directory = Path.Combine(engine.DownloadPath, user.UserId.ToString(), queueTime.Ticks.ToString());
                        if (!Directory.Exists(directory))
                            Directory.CreateDirectory(directory);

                        String filePath = Path.Combine(directory, uploadedFile.FileName);

                        await using (FileStream fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                        {
                            await uploadedFile.CopyToAsync(fileStream);
                        }

                        UInt64 fileSize = (UInt64)uploadedFile.Length;
                        String fileArguments = engine.RenderArgument;

                        foreach (ApiArgType argumentType in enqueueRequest.Arguments)
                        {
                            String retrieveArgType = @"
                            SELECT 
                                a.argtype_id,
                                a.type,
                                a.regex
                            FROM 
                                argtypes a
                            WHERE 
                                a.argtype_id = @ArgTypeId";

                            using (MySqlCommand argTypeCommand = new MySqlCommand(retrieveArgType, connection))
                            {
                                argTypeCommand.Parameters.AddWithValue("@ArgTypeId", argumentType.ArgTypeId);

                                DbArgType? argType = null;

                                using (MySqlDataReader reader = argTypeCommand.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        String argTypeId = reader.GetString("argtype_id");
                                        String type = reader.GetString("type");
                                        String? regex = !reader.IsDBNull("regex") ? reader.GetString("regex") : null;

                                        argType = new DbArgType(argTypeId, type, regex);
                                    }
                                }

                                if (argType == null)
                                {
                                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Argument type identifier {argumentType.ArgTypeId} must be present in database.")));
                                    return;
                                }

                                Boolean isValueAllowed = false;

                                if (String.IsNullOrEmpty(argType.Regex))
                                    switch (argType.Type)
                                    {
                                        default:
                                            {
                                                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Type {argType.Type} is not valid for RenderOnline.")));
                                                return;
                                            }
                                        case "file":
                                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^[a-zA-Z0-9_\-]+\.[a-zA-Z0-9]{1,4}$");
                                            break;
                                        case "path":
                                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^(/[a-zA-Z0-9_\-]+)+/?$");
                                            break;
                                        case "extension":
                                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^\.[a-zA-Z0-9]{1,4}$");
                                            break;
                                        case "word":
                                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^\w+$");
                                            break;
                                        case "sentence":
                                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^[a-zA-Z0-9\s,.!?'-]+$");
                                            break;
                                        case "natural":
                                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^\d+$");
                                            break;
                                        case "integer":
                                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^-?\d+$");
                                            break;
                                        case "real":
                                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^-?\d+(\.\d+)?$");
                                            break;
                                    }
                                else if (argType.Regex != null) isValueAllowed = Regex.IsMatch(argumentType.Value, argumentType.Regex);

                                if (!isValueAllowed)
                                {
                                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Value {argumentType.Value} must be of type {argType.Type}.")));
                                    return;
                                }

                                fileArguments = fileArguments.Replace($"$RENDERONLINE:{argumentType.ArgTypeId}", argumentType.Value);
                            }
                        }

                        fileArguments = fileArguments.Replace("$RENDERONLINE:@uploaded_file", filePath);

                        String addRenderQuery = @"
                            INSERT INTO renders (file_name, file_path, file_size, arguments, engine_id) 
                            VALUES (@FileName, @FilePath, @FileSize, @Arguments, @EngineId);
                            SELECT LAST_INSERT_ID();";

                        using (MySqlCommand addRenderCommand = new MySqlCommand(addRenderQuery, connection))
                        {
                            addRenderCommand.Parameters.AddWithValue("@FileName", uploadedFile.FileName);
                            addRenderCommand.Parameters.AddWithValue("@FilePath", filePath);
                            addRenderCommand.Parameters.AddWithValue("@FileSize", fileSize);
                            addRenderCommand.Parameters.AddWithValue("@Arguments", fileArguments);
                            addRenderCommand.Parameters.AddWithValue("@EngineId", engine.EngineId);

                            UInt64 renderId = Convert.ToUInt64(addRenderCommand.ExecuteScalar());

                            String addTaskQuery = @"
                            INSERT INTO tasks (user_id, queue_time, start_time, end_time, is_running, is_success, render_id, machine_id) 
                            VALUES (@UserId, @QueueTime, NULL, NULL, @IsRunning, @IsSuccess, @RenderId, NULL);
                            SELECT LAST_INSERT_ID();";

                            using (MySqlCommand addTaskCommand = new MySqlCommand(addTaskQuery, connection))
                            {
                                addTaskCommand.Parameters.AddWithValue("@UserId", user.UserId);
                                addTaskCommand.Parameters.AddWithValue("@QueueTime", queueTime);
                                addTaskCommand.Parameters.AddWithValue("@IsRunning", false);
                                addTaskCommand.Parameters.AddWithValue("@IsSuccess", false);
                                addTaskCommand.Parameters.AddWithValue("@RenderId", renderId);

                                UInt64 taskId = Convert.ToUInt64(addTaskCommand.ExecuteScalar());

                                String addQueueQuery = @"
                                INSERT INTO queue (task_id) 
                                VALUES (@TaskId);";

                                using (MySqlCommand addQueueCommand = new MySqlCommand(addQueueQuery, connection))
                                {
                                    addQueueCommand.Parameters.AddWithValue("@TaskId", taskId);
                                    addQueueCommand.ExecuteNonQuery();
                                }
                            }
                        }

                        httpContext.Response.StatusCode = StatusCodes.Status200OK;
                        await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(true, "Task successfully enqueued.")));
                    }
                }
            }
        }


        public async Task HandleDequeueRequest(HttpContext httpContext)
        {
            ApiUser? user = AuthenticateAndGetUserInDatabase(httpContext);

            if (user == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsync(String.Empty);
                return;
            }

            if (!httpContext.Request.HasJsonContentType())
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(false, "Request content type must be application/json.")));
                return;
            }

            ApiDequeueRequest? dequeueRequest = await httpContext.Request.ReadFromJsonAsync<ApiDequeueRequest>();

            if (dequeueRequest == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(false, "Request content has an invalid format.")));
                return;
            }

            using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
            {
                connection.Open();

                String checkTaskQuery = @"
            SELECT 
                t.task_id 
            FROM 
                tasks t 
            WHERE 
                t.task_id = @TaskId AND t.user_id = @UserId";

                using (MySqlCommand checkTaskCommand = new MySqlCommand(checkTaskQuery, connection))
                {
                    checkTaskCommand.Parameters.AddWithValue("@TaskId", dequeueRequest.TaskId);
                    checkTaskCommand.Parameters.AddWithValue("@UserId", user.UserId);

                    using (MySqlDataReader reader = checkTaskCommand.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            reader.Close();
                            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(false, $"Task with identifier {dequeueRequest.TaskId} not found for the current user.")));
                            return;
                        }
                    }
                }

                String checkQueueQuery = @"
            SELECT 
                q.queue_id 
            FROM 
                queue q 
            WHERE 
                q.task_id = @TaskId";

                using (MySqlCommand checkQueueCommand = new MySqlCommand(checkQueueQuery, connection))
                {
                    checkQueueCommand.Parameters.AddWithValue("@TaskId", dequeueRequest.TaskId);

                    using (MySqlDataReader reader = checkQueueCommand.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            reader.Close();
                            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(false, $"Task with identifier {dequeueRequest.TaskId} is not in the queue.")));
                            return;
                        }
                    }
                }

                String deleteQueueQuery = @"
            DELETE FROM queue 
            WHERE task_id = @TaskId";

                using (MySqlCommand deleteQueueCommand = new MySqlCommand(deleteQueueQuery, connection))
                {
                    deleteQueueCommand.Parameters.AddWithValue("@TaskId", dequeueRequest.TaskId);
                    deleteQueueCommand.ExecuteNonQuery();
                }

                String retrieveFullTaskQuery = @"
            SELECT 
                t.task_id, 
                t.user_id,
                t.queue_time,
                t.start_time, 
                t.end_time, 
                t.is_running, 
                t.is_success,
                t.render_id,
                t.machine_id,
                r.render_id,
                r.file_name,
                r.file_path,
                r.file_size,
                r.arguments,
                r.engine_id,
                e.engine_id,
                e.name,
                e.extension,
                e.download_path,
                e.render_argument
            FROM 
                tasks t
            JOIN 
                renders r ON t.render_id = r.render_id
            JOIN
                engines e ON r.engine_id = e.engine_id
            WHERE 
                t.task_id = @TaskId";

                ApiTaskInfo? task = null;

                using (MySqlCommand retrieveFullTaskCommand = new MySqlCommand(retrieveFullTaskQuery, connection))
                {
                    retrieveFullTaskCommand.Parameters.AddWithValue("@TaskId", dequeueRequest.TaskId);

                    using (MySqlDataReader reader = retrieveFullTaskCommand.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            UInt64 taskId = reader.GetUInt64("task_id");
                            UInt16 userId = reader.GetUInt16("user_id");
                            DateTime? queueTime = !reader.IsDBNull("queue_time") ? reader.GetDateTime("queue_time") : null;
                            DateTime? startTime = !reader.IsDBNull("start_time") ? reader.GetDateTime("start_time") : null;
                            DateTime? endTime = !reader.IsDBNull("end_time") ? reader.GetDateTime("end_time") : null;
                            Boolean isRunning = reader.GetBoolean("is_running");
                            Boolean isSuccess = reader.GetBoolean("is_success");
                            UInt64 renderId = reader.GetUInt64("render_id");
                            Byte? machineId = !reader.IsDBNull("machine_id") ? reader.GetByte("machine_id") : null;

                            DbTask dbTask = new(taskId, userId, queueTime, startTime, endTime, isRunning, isSuccess, renderId, machineId);

                            String fileName = reader.GetString("file_name");
                            String filePath = reader.GetString("file_path");
                            UInt64 fileSize = reader.GetUInt64("file_size");
                            String arguments = reader.GetString("arguments");
                            Byte engineId = reader.GetByte("engine_id");

                            DbRender dbRender = new(renderId, fileName, filePath, fileSize, arguments, engineId);

                            String engineName = reader.GetString("name");
                            String extension = reader.GetString("extension");
                            String downloadPath = reader.GetString("download_path");
                            String renderArgument = reader.GetString("render_argument");

                            DbEngine dbEngine = new(engineId, engineName, extension, downloadPath, renderArgument);

                            ApiTask apiTask = new(dbTask.TaskId, dbTask.UserId, dbTask.QueueTime, dbTask.StartTime, dbTask.EndTime, dbTask.IsRunning, dbTask.IsSuccess, dbTask.RenderId, dbTask.MachineId);
                            ApiRender apiRender = new(dbRender.RenderId, dbRender.FileName, dbRender.FilePath, dbRender.FileSize, dbRender.Arguments, dbRender.EngineId);
                            ApiEngine apiEngine = new(dbEngine.EngineId, dbEngine.Name, dbEngine.Extension, dbEngine.DownloadPath, dbEngine.RenderArgument);

                            task = new ApiTaskInfo(apiTask, apiRender, apiEngine);
                        }
                    }
                }

                if (task != null && task.Task.MachineId != null)
                {
                    String machineQuery = @"
                SELECT 
                    m.machine_id, 
                    m.ip_address,
                    m.port 
                FROM 
                    machines m
                WHERE m.machine_id = @MachineId";

                    using (MySqlCommand machineCommand = new MySqlCommand(machineQuery, connection))
                    {
                        machineCommand.Parameters.AddWithValue("@MachineId", task.Task.MachineId);

                        using (MySqlDataReader machineReader = machineCommand.ExecuteReader())
                        {
                            DbMachine? machine = null;

                            if (machineReader.Read())
                            {
                                Byte machineId = machineReader.GetByte("machine_id");
                                String ipAddress = machineReader.GetString("ip_address");
                                UInt16 port = machineReader.GetUInt16("port");

                                machine = new DbMachine(machineId, ipAddress, port);
                            }

                            if (machine != null)
                                await StopTaskOnMachine(machine, task);
                        }
                    }
                }

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(true, "Task successfully dequeued.")));
            }
        }


        public async Task HandleDownloadRequest(HttpContext httpContext)
        {
            ApiUser? user = AuthenticateAndGetUserInDatabase(httpContext);

            if (user == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsync(String.Empty);
                return;
            }

            if (!httpContext.Request.HasJsonContentType())
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDownloadResponse(false, "Request content type must be application/json.")));
                return;
            }

            ApiDownloadRequest? downloadRequest = await httpContext.Request.ReadFromJsonAsync<ApiDownloadRequest>();

            if (downloadRequest == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDownloadResponse(false, "Request content has an invalid format.")));
                return;
            }

            ApiTaskInfo? task = null;

            // Use a narrower scope for the MySQL connection
            using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
            {
                connection.Open();

                // Validate the task and user ownership
                String checkTaskQuery = @"
        SELECT 
            t.task_id 
        FROM 
            tasks t 
        WHERE 
            t.task_id = @TaskId AND t.user_id = @UserId";

                using (MySqlCommand checkTaskCommand = new MySqlCommand(checkTaskQuery, connection))
                {
                    checkTaskCommand.Parameters.AddWithValue("@TaskId", downloadRequest.TaskId);
                    checkTaskCommand.Parameters.AddWithValue("@UserId", user.UserId);

                    using (MySqlDataReader reader = checkTaskCommand.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            reader.Close();
                            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDownloadResponse(false, $"Task with identifier {downloadRequest.TaskId} not found for the current user.")));
                            return;
                        }
                    }
                }

                // Retrieve task information
                String retrieveFullTaskQuery = @"
        SELECT 
            t.task_id, 
            t.user_id,
            t.queue_time,
            t.start_time, 
            t.end_time, 
            t.is_running, 
            t.is_success,
            t.render_id,
            t.machine_id,
            r.render_id,
            r.file_name,
            r.file_path,
            r.file_size,
            r.arguments,
            r.engine_id
        FROM
            tasks t
        JOIN 
            renders r ON t.render_id = r.render_id
        WHERE 
            t.task_id = @TaskId";

                using (MySqlCommand retrieveFullTaskCommand = new MySqlCommand(retrieveFullTaskQuery, connection))
                {
                    retrieveFullTaskCommand.Parameters.AddWithValue("@TaskId", downloadRequest.TaskId);

                    using (MySqlDataReader reader = retrieveFullTaskCommand.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            UInt64 taskId = reader.GetUInt64("task_id");
                            UInt16 userId = reader.GetUInt16("user_id");
                            DateTime? queueTime = !reader.IsDBNull("queue_time") ? reader.GetDateTime("queue_time") : null;
                            DateTime? startTime = !reader.IsDBNull("start_time") ? reader.GetDateTime("start_time") : null;
                            DateTime? endTime = !reader.IsDBNull("end_time") ? reader.GetDateTime("end_time") : null;
                            Boolean isRunning = reader.GetBoolean("is_running");
                            Boolean isSuccess = reader.GetBoolean("is_success");
                            UInt64 renderId = reader.GetUInt64("render_id");
                            Byte? machineId = !reader.IsDBNull("machine_id") ? reader.GetByte("machine_id") : null;

                            DbTask dbTask = new(taskId, userId, queueTime, startTime, endTime, isRunning, isSuccess, renderId, machineId);

                            String fileName = reader.GetString("file_name");
                            String filePath = reader.GetString("file_path");
                            UInt64 fileSize = reader.GetUInt64("file_size");
                            String arguments = reader.GetString("arguments");
                            Byte engineId = reader.GetByte("engine_id");

                            DbRender dbRender = new(renderId, fileName, filePath, fileSize, arguments, engineId);

                            ApiTask apiTask = new(dbTask.TaskId, dbTask.UserId, dbTask.QueueTime, dbTask.StartTime, dbTask.EndTime, dbTask.IsRunning, dbTask.IsSuccess, dbTask.RenderId, dbTask.MachineId);
                            ApiRender apiRender = new(dbRender.RenderId, dbRender.FileName, dbRender.FilePath, dbRender.FileSize, dbRender.Arguments, dbRender.EngineId);

                            task = new ApiTaskInfo(apiTask, apiRender, null);
                        }
                    }
                }
            }

            if (task == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDownloadResponse(false, $"Task with identifier {downloadRequest.TaskId} not found.")));
                return;
            }

            String? parentDirectoryPath = task?.Render?.FilePath;

            if (String.IsNullOrEmpty(parentDirectoryPath))
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDownloadResponse(false, $"Filepath error for Task with identifier {downloadRequest.TaskId}.")));
                return;
            }

            DirectoryInfo? parentDirectory = Directory.GetParent(parentDirectoryPath);

            if (parentDirectory == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDownloadResponse(false, $"Unable to determine parent directory for Task with identifier {downloadRequest.TaskId}.")));
                return;
            }

            String zipFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");

            Boolean zipCreated = false;
            for (UInt32 i = 0; i < 15; i++)
            {
                try
                {
                    ZipFile.CreateFromDirectory(parentDirectory.FullName, zipFilePath);
                    zipCreated = true;
                    break;
                }
                catch (Exception)
                {
                    await Task.Delay(250);
                }
            }

            if (!zipCreated)
            {
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDownloadResponse(false, "Failed to create ZIP archive after multiple attempts!")));
                return;
            }

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/zip";
            httpContext.Response.Headers.Append("Content-Disposition", $"attachment; filename={Path.GetFileName(zipFilePath)}");

            await httpContext.Response.SendFileAsync(zipFilePath);

            for (UInt32 i = 0; i < 15; i++)
            {
                try
                {
                    File.Delete(zipFilePath);
                    break;
                }
                catch (Exception)
                {
                    await Task.Delay(250);
                }
            }
        }

        public async Task HandleDeleteRequest(HttpContext httpContext)
        {
            ApiUser? user = AuthenticateAndGetUserInDatabase(httpContext);

            if (user == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsync(String.Empty);
                return;
            }

            if (!httpContext.Request.HasJsonContentType())
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDeleteResponse(false, "Request content type must be application/json.")));
                return;
            }

            ApiDeleteRequest? deleteRequest = await httpContext.Request.ReadFromJsonAsync<ApiDeleteRequest>();

            if (deleteRequest == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDeleteResponse(false, "Request content has an invalid format.")));
                return;
            }

            using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
            {
                connection.Open();

                String checkTaskQuery = @"
            SELECT 
                t.task_id, 
                r.render_id,
                r.file_path 
            FROM 
                tasks t 
            JOIN 
                renders r ON t.render_id = r.render_id 
            WHERE 
                t.task_id = @TaskId AND t.user_id = @UserId";

                UInt64 renderId;
                String filePath;

                using (MySqlCommand checkTaskCommand = new MySqlCommand(checkTaskQuery, connection))
                {
                    checkTaskCommand.Parameters.AddWithValue("@TaskId", deleteRequest.TaskId);
                    checkTaskCommand.Parameters.AddWithValue("@UserId", user.UserId);

                    using (MySqlDataReader reader = checkTaskCommand.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDeleteResponse(false, $"Task with identifier {deleteRequest.TaskId} not found for the current user.")));
                            return;
                        }

                        renderId = reader.GetUInt64("render_id");
                        filePath = reader.GetString("file_path");
                    }
                }

                String deleteQueueQuery = @"
            DELETE FROM queue 
            WHERE task_id = @TaskId";

                using (MySqlCommand deleteQueueCommand = new MySqlCommand(deleteQueueQuery, connection))
                {
                    deleteQueueCommand.Parameters.AddWithValue("@TaskId", deleteRequest.TaskId);
                    deleteQueueCommand.ExecuteNonQuery();
                }

                String? parentDirectoryPath = Path.GetDirectoryName(filePath);

                if (parentDirectoryPath != null && Directory.Exists(parentDirectoryPath))
                {
                    for (UInt32 i = 0; i < 15; i++)
                    {
                        try
                        {
                            Directory.Delete(parentDirectoryPath, true);
                            break;
                        }
                        catch (Exception)
                        {
                            await Task.Delay(100);
                        }
                    }
                }

                String deleteTaskQuery = @"
            DELETE FROM tasks 
            WHERE task_id = @TaskId";

                using (MySqlCommand deleteTaskCommand = new MySqlCommand(deleteTaskQuery, connection))
                {
                    deleteTaskCommand.Parameters.AddWithValue("@TaskId", deleteRequest.TaskId);
                    deleteTaskCommand.ExecuteNonQuery();
                }

                String deleteRenderQuery = @"
            DELETE FROM renders 
            WHERE render_id = @RenderId";

                using (MySqlCommand deleteRenderCommand = new MySqlCommand(deleteRenderQuery, connection))
                {
                    deleteRenderCommand.Parameters.AddWithValue("@RenderId", renderId);
                    deleteRenderCommand.ExecuteNonQuery();
                }

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDeleteResponse(true, "Task and associated render successfully deleted.")));
            }
        }


        public void StartPollingService()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(15000);
                    Console.WriteLine("Polling...");

                    try
                    {
                        String query = @"
                        SELECT 
                            t.task_id, 
                            t.user_id,
                            t.queue_time,
                            t.start_time, 
                            t.end_time, 
                            t.is_running, 
                            t.is_success,
                            t.render_id,
                            t.machine_id,
                            r.render_id,
                            r.file_name,
                            r.file_path,
                            r.file_size,
                            r.arguments,
                            r.engine_id,
                            e.engine_id,
                            e.name,
                            e.extension,
                            e.download_path,
                            e.render_argument
                        FROM 
                            tasks t
                        JOIN 
                            queue q ON t.task_id = q.task_id
                        JOIN 
                            renders r ON t.render_id = r.render_id
                        JOIN
                            engines e ON r.engine_id = e.engine_id
                        WHERE 
                            t.is_success = 0";

                        List<ApiTaskInfo> tasks = new();

                        using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
                        {
                            connection.Open();
                            using (MySqlCommand command = new MySqlCommand(query, connection))
                            {
                                using (MySqlDataReader reader = command.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        // Read task details
                                        UInt64 taskId = reader.GetUInt64("task_id");
                                        UInt16 userId = reader.GetUInt16("user_id");
                                        DateTime? queueTime = !reader.IsDBNull("queue_time") ? reader.GetDateTime("queue_time") : null;
                                        DateTime? startTime = !reader.IsDBNull("start_time") ? reader.GetDateTime("start_time") : null;
                                        DateTime? endTime = !reader.IsDBNull("end_time") ? reader.GetDateTime("end_time") : null;
                                        Boolean isRunning = reader.GetBoolean("is_running");
                                        Boolean isSuccess = reader.GetBoolean("is_success");
                                        UInt64 renderId = reader.GetUInt64("render_id");
                                        Byte? machineId = !reader.IsDBNull("machine_id") ? reader.GetByte("machine_id") : null;

                                        DbTask dbTask = new(taskId, userId, queueTime, startTime, endTime, isRunning, isSuccess, renderId, machineId);

                                        // Read render details
                                        String fileName = reader.GetString("file_name");
                                        String filePath = reader.GetString("file_path");
                                        UInt64 fileSize = reader.GetUInt64("file_size");
                                        String arguments = reader.GetString("arguments");
                                        Byte engineId = reader.GetByte("engine_id");

                                        DbRender dbRender = new(renderId, fileName, filePath, fileSize, arguments, engineId);

                                        // Read engine details
                                        String engineName = reader.GetString("name");
                                        String extension = reader.GetString("extension");
                                        String downloadPath = reader.GetString("download_path");
                                        String renderArgument = reader.GetString("render_argument");

                                        DbEngine dbEngine = new(engineId, engineName, extension, downloadPath, renderArgument);

                                        ApiTask apiTask = new(dbTask.TaskId, dbTask.UserId, dbTask.QueueTime, dbTask.StartTime, dbTask.EndTime, dbTask.IsRunning, dbTask.IsSuccess, dbTask.RenderId, dbTask.MachineId);
                                        ApiRender apiRender = new(dbRender.RenderId, dbRender.FileName, dbRender.FilePath, dbRender.FileSize, dbRender.Arguments, dbRender.EngineId);
                                        ApiEngine apiEngine = new(dbEngine.EngineId, dbEngine.Name, dbEngine.Extension, dbEngine.DownloadPath, dbEngine.RenderArgument);

                                        tasks.Add(new ApiTaskInfo(apiTask, apiRender, apiEngine));
                                    }
                                }
                            }
                        }

                        foreach (ApiTaskInfo task in tasks)
                        {
                            if (task.Task.MachineId == null && task.Task.IsRunning == false)
                            {
                                Console.WriteLine($"Attempting to assign task {task.Task.TaskId} on machine: {task.Task.MachineId}");

                                // Assign tasks to machines if not already assigned
                                await AssignTaskToMachine(task);
                            }
                            else
                            {
                                Console.WriteLine($"Checking status of previously started task {task.Task.TaskId}");

                                // Check the status of tasks already assigned to machines
                                if (task?.Task != null)
                                    await CheckTaskStatusOnMachine(task.Task);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // Log any errors during polling
                        Console.WriteLine($"Polling service error: {e.Message}");
                        Console.WriteLine($"Stack Trace: {e.StackTrace}");
                    }
                }
            });
        }

        private async Task AssignTaskToMachine(ApiTaskInfo task)
        {
            try
            {
                String machineQuery = @"
                        SELECT 
                            m.machine_id, 
                            m.ip_address,
                            m.port 
                        FROM 
                            machines m";

                using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand machineCommand = new MySqlCommand(machineQuery, connection))
                    {
                        using (MySqlDataReader machineReader = machineCommand.ExecuteReader())
                        {
                            List<DbMachine> machines = new();

                            while (machineReader.Read())
                            {
                                Byte machineId = machineReader.GetByte("machine_id");
                                String ipAddress = machineReader.GetString("ip_address");
                                UInt16 port = machineReader.GetUInt16("port");

                                machines.Add(new DbMachine(machineId, ipAddress, port));
                            }

                            foreach (DbMachine machine in machines)
                            {
                                HttpClient httpClient = new HttpClient(this._httpClientHandler);
                                httpClient.BaseAddress = new Uri($"http://{machine.IpAddress}:{machine.Port}");
                                HttpResponseMessage statusResponse = await httpClient.GetAsync("/hpc/status");

                                if (statusResponse.IsSuccessStatusCode)
                                {
                                    String statusResponseContent = await statusResponse.Content.ReadAsStringAsync();
                                    HPCStatusResponse? status = JsonConvert.DeserializeObject<HPCStatusResponse>(statusResponseContent);

                                    if (status == null)
                                    {
                                        Console.WriteLine($"Machine {machine.MachineId} invalid response!");
                                        continue;
                                    }

                                    if (status.Task == null || !status.Task.IsRunning)
                                    {
                                        // Attempt to start task on available machine
                                        if (await StartTaskOnMachine(machine, task))
                                        {
                                            UpdateTaskStartDetails(task.Task.TaskId, machine.MachineId);
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"Failed to get status from machine {machine.MachineId}.");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in assigning task to machine: {e.Message}");
            }
        }

        private async Task<Boolean> StartTaskOnMachine(DbMachine machine, ApiTaskInfo task)
        {
            try
            {
                Console.WriteLine($"Attempting to start task {task.Task.TaskId} on machine {machine.MachineId}...");

                HPCStartArgs startArgs = new(task.Engine?.Name, task.Task.TaskId, task.Render?.Arguments);
                String startMessage = JsonConvert.SerializeObject(startArgs);

                HttpClient httpClient = new HttpClient(this._httpClientHandler);
                httpClient.BaseAddress = new Uri($"http://{machine.IpAddress}:{machine.Port}");
                HttpContent content = new StringContent(startMessage, Encoding.UTF8, "application/json");
                HttpResponseMessage startResponse = await httpClient.PostAsync("/hpc/start", content);

                if (startResponse.IsSuccessStatusCode)
                {
                    String startResponseContent = await startResponse.Content.ReadAsStringAsync();
                    HPCStartResponse? start = JsonConvert.DeserializeObject<HPCStartResponse>(startResponseContent);

                    if (start != null && start.IsSuccess)
                    {
                        Console.WriteLine($"Succeeded to start task {task.Task.TaskId} on machine {machine.MachineId}");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Failed to start task {task.Task.TaskId} on machine {machine.MachineId}");
                    }
                }
                else
                {
                    Console.WriteLine($"HTTP error while starting task {task.Task.TaskId} on machine {machine.MachineId}: {startResponse.StatusCode}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception while starting task {task.Task.TaskId} on machine {machine.MachineId}: {e.Message}");
            }

            return false;
        }

        private async Task<Boolean> StopTaskOnMachine(DbMachine machine, ApiTaskInfo task)
        {
            try
            {
                Console.WriteLine($"Attempting to stop task {task.Task.TaskId} on machine {machine.MachineId}...");

                HPCStopArgs stopArgs = new(task.Task.TaskId);
                String stopMessage = JsonConvert.SerializeObject(stopArgs);

                HttpClient httpClient = new HttpClient(this._httpClientHandler);
                httpClient.BaseAddress = new Uri($"http://{machine.IpAddress}:{machine.Port}");
                HttpContent content = new StringContent(stopMessage, Encoding.UTF8, "application/json");
                HttpResponseMessage stopResponse = await httpClient.PostAsync("/hpc/stop", content);

                if (stopResponse.IsSuccessStatusCode)
                {
                    String startResponseContent = await stopResponse.Content.ReadAsStringAsync();
                    HPCStartResponse? stop = JsonConvert.DeserializeObject<HPCStartResponse>(startResponseContent);

                    if (stop != null && stop.IsSuccess)
                    {
                        Console.WriteLine($"Succeeded to stop task {task.Task.TaskId} on machine {machine.MachineId}");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Failed to stop task {task.Task.TaskId} on machine {machine.MachineId}");
                    }
                }
                else
                {
                    Console.WriteLine($"HTTP error while stopping task {task.Task.TaskId} on machine {machine.MachineId}: {stopResponse.StatusCode}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception while stopping task {task.Task.TaskId} on machine {machine.MachineId}: {e.Message}");
            }

            return false;
        }

        private void UpdateTaskStartDetails(UInt64 taskId, Byte machineId)
        {
            try
            {
                String updateTaskQuery = @"
                        UPDATE tasks 
                        SET start_time = @StartTime, is_running = 1, machine_id = @MachineId 
                        WHERE task_id = @TaskId";

                using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand updateTaskCommand = new MySqlCommand(updateTaskQuery, connection))
                    {
                        updateTaskCommand.Parameters.AddWithValue("@StartTime", DateTime.Now);
                        updateTaskCommand.Parameters.AddWithValue("@MachineId", machineId);
                        updateTaskCommand.Parameters.AddWithValue("@TaskId", taskId);
                        updateTaskCommand.ExecuteNonQuery();
                    }
                }

                Console.WriteLine($"Task {taskId} successfully started on machine {machineId}.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to update task start details for Task {taskId}: {e.Message}");
            }
        }

        private async Task CheckTaskStatusOnMachine(ApiTask task)
        {
            if (task.MachineId == null)
            {
                Console.WriteLine("Task has no assigned machine.");
                return;
            }

            try
            {
                String machineQuery = @"
                SELECT 
                    m.machine_id, 
                    m.ip_address, 
                    m.port 
                FROM 
                    machines m
                WHERE 
                    m.machine_id = @MachineId";

                using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand machineCommand = new MySqlCommand(machineQuery, connection))
                    {
                        machineCommand.Parameters.AddWithValue("@MachineId", task.MachineId);
                        using (MySqlDataReader reader = machineCommand.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                String ipAddress = reader.GetString("ip_address");
                                UInt16 port = reader.GetUInt16("port");
                                reader.Close();

                                HttpClient httpClient = new HttpClient(this._httpClientHandler);
                                httpClient.BaseAddress = new Uri($"https://{ipAddress}:{port}");
                                HttpResponseMessage statusResponse = await httpClient.GetAsync("/hpc/status");

                                if (statusResponse.IsSuccessStatusCode)
                                {
                                    String statusResponseContent = await statusResponse.Content.ReadAsStringAsync();
                                    HPCStatusResponse? status = JsonConvert.DeserializeObject<HPCStatusResponse>(statusResponseContent);

                                    if (status == null)
                                    {
                                        Console.WriteLine($"Machine {task.MachineId} invalid response!");
                                        return;
                                    }

                                    if (status.Task != null)
                                    {
                                        if (!status.Task.IsRunning && status.Task.IsSuccess)
                                        {
                                            CompleteTask(task.TaskId);
                                        }
                                        else if (!status.Task.IsRunning && !status.Task.IsSuccess)
                                        {
                                            HandleTaskFailure(task.TaskId, (Byte)task.MachineId);
                                        }
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"Failed to get status from machine {task.MachineId}: {statusResponse.StatusCode}");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Machine with ID {task.MachineId} not found.");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error checking task status on machine {task.MachineId}: {e.Message}");
            }
        }

        private void CompleteTask(UInt64 taskId)
        {
            try
            {
                String completeTaskQuery = @"
                        UPDATE tasks 
                        SET end_time = @EndTime, is_running = 0, is_success = 1 
                        WHERE task_id = @TaskId";

                using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand completeTaskCommand = new MySqlCommand(completeTaskQuery, connection))
                    {
                        completeTaskCommand.Parameters.AddWithValue("@EndTime", DateTime.Now);
                        completeTaskCommand.Parameters.AddWithValue("@TaskId", taskId);
                        completeTaskCommand.ExecuteNonQuery();
                    }
                }

                RemoveTaskFromQueue(taskId);
                Console.WriteLine($"Task {taskId} completed successfully.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error completing task {taskId}: {e.Message}");
            }
        }

        private void HandleTaskFailure(UInt64 taskId, Byte machineId)
        {
            try
            {
                String failTaskQuery = @"
                            UPDATE tasks 
                            SET start_time = @StartTime, end_time = @EndTime, is_running = 0, is_success = 0, machine_id = @MachineId 
                            WHERE task_id = @TaskId";

                using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand failTaskCommand = new MySqlCommand(failTaskQuery, connection))
                    {
                        failTaskCommand.Parameters.AddWithValue("@StartTime", DateTime.Now);
                        failTaskCommand.Parameters.AddWithValue("@EndTime", DateTime.Now);
                        failTaskCommand.Parameters.AddWithValue("@MachineId", machineId);
                        failTaskCommand.Parameters.AddWithValue("@TaskId", taskId);
                        failTaskCommand.ExecuteNonQuery();
                    }
                }

                RemoveTaskFromQueue(taskId);
                Console.WriteLine($"Task {taskId} failed on machine {machineId}.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error handling task failure for Task {taskId} on Machine {machineId}: {e.Message}");
            }
        }

        private void RemoveTaskFromQueue(UInt64 taskId)
        {
            try
            {
                String deleteQueueQuery = @"
                        DELETE FROM queue 
                        WHERE task_id = @TaskId";

                using (MySqlConnection connection = new MySqlConnection(this._renderApiConfiguration.ConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand deleteQueueCommand = new MySqlCommand(deleteQueueQuery, connection))
                    {
                        deleteQueueCommand.Parameters.AddWithValue("@TaskId", taskId);
                        deleteQueueCommand.ExecuteNonQuery();
                    }
                }

                Console.WriteLine($"Task {taskId} removed from queue.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error removing task {taskId} from queue: {e.Message}");
            }
        }
    }
}

